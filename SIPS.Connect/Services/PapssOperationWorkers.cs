using System.Text;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using SIPS.Connect.Config;
using SIPS.Core.Interfaces;
using SIPS.Core.Services;
using SIPS.ISO20022.Helpers;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Options;

namespace SIPS.Connect.Services;

/// <summary>
/// Shared claim/retry mechanics for the PAPSS outboxes. All state lives in PostgreSQL, so a restarted
/// process (or another instance) resumes where the previous one stopped. An item is claimed with a
/// compare-and-set on (state, attempts): only one instance can win a given attempt. The claim also
/// pushes next_attempt_at out by the claim lease, so an item whose worker crashed mid-flight is retried
/// by another instance after the lease (at-least-once; receivers de-duplicate: the bank on
/// X-Idempotency-Key, the gateway on the byte-identical reply).
/// </summary>
public abstract class PapssOutboxWorkerBase(IServiceScopeFactory scopes, PapssFacingOptions options, IPapssOutboxSignal signal, TimeProvider clock, ILogger logger) : BackgroundService
{
    protected const int BatchSize = 25;
    protected IServiceScopeFactory Scopes { get; } = scopes;
    protected PapssFacingOptions Options { get; } = options;
    protected TimeProvider Clock { get; } = clock;
    protected ILogger Logger { get; } = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                if (Options.Enabled) processed = await RunOnceAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                Logger.LogError(error, "{Worker} scan failed", GetType().Name);
            }
            if (processed >= BatchSize) continue; // more work is probably due
            try { await signal.WaitAsync(TimeSpan.FromSeconds(Math.Max(1, Options.Delivery.PollIntervalSeconds)), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    /// <summary>Processes one batch of due items. Returns the number of items claimed.</summary>
    public abstract Task<int> RunOnceAsync(CancellationToken ct);

    protected PapssParticipantBinding Binding(XadesOptions xades)
        => new(xades.BIC.Trim().ToUpperInvariant(), Options.LocalCountry.Trim().ToUpperInvariant(),
            Options.SendingCurrencies.Select(x => x.Trim().ToUpperInvariant()).ToArray(), Options.CallbackMappingProfile, Options.CallbackUrl);

    protected static string Truncate(string? value) => value is null ? string.Empty : value.Length <= 1000 ? value : value[..1000];
}

/// <summary>Pushes stored PAPSS results (papss_operation_events with push_state PENDING) to the bank callback.</summary>
public sealed class PapssBankPushWorker(IServiceScopeFactory scopes, PapssFacingOptions options, XadesOptions xades, IParticipantCallbackContext context, IPapssOutboxSignal signal, TimeProvider clock, ILogger<PapssBankPushWorker> logger)
    : PapssOutboxWorkerBase(scopes, options, signal, clock, logger)
{
    public override async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var delivery = scope.ServiceProvider.GetRequiredService<IVerificationResultDelivery>();
        var now = Clock.GetUtcNow();
        var due = await db.PapssOperationEvents.AsNoTracking()
            .Where(x => x.PushState == PapssDeliveryState.Pending && (x.PushNextAttemptAt == null || x.PushNextAttemptAt <= now))
            .OrderBy(x => x.PushNextAttemptAt).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.PushAttempts, x.OperationId })
            .Take(BatchSize).ToListAsync(ct);
        var claimedCount = 0;
        foreach (var item in due)
        {
            var attempt = item.PushAttempts + 1;
            var lease = Clock.GetUtcNow().AddSeconds(Math.Max(10, Options.Delivery.ClaimLeaseSeconds));
            var claimed = await db.PapssOperationEvents
                .Where(x => x.Id == item.Id && x.PushState == PapssDeliveryState.Pending && x.PushAttempts == item.PushAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PushAttempts, attempt).SetProperty(x => x.PushNextAttemptAt, lease), ct);
            if (claimed == 0) continue;
            claimedCount++;
            await DeliverAsync(db, delivery, item.Id, item.OperationId, attempt, ct);
        }
        return claimedCount;
    }

    private async Task DeliverAsync(IStorageBroker db, IVerificationResultDelivery delivery, long eventId, Guid? operationId, int attempt, CancellationToken ct)
    {
        var raw = await db.PapssOperationEvents.AsNoTracking().Where(x => x.Id == eventId).Select(x => x.RawXml).SingleAsync(ct);
        SIPS.ISO20022.Models.DTOs.CB.CBVerificationResultDto result;
        try
        {
            result = IncomingVerificationResponseHandler.Map(PayeeVerificationResponseBuilder.Parse(Encoding.UTF8.GetString(raw)));
        }
        catch (Exception error)
        {
            Logger.LogError(error, "Stored PAPSS result event {EventId} cannot be parsed; marking its bank push FAILED", eventId);
            await CompleteAsync(db, eventId, operationId, attempt, PapssDeliveryState.Failed, "UNPARSEABLE_STORED_RESULT", ct);
            return;
        }

        try
        {
            // No request context exists in a worker: re-establish the PAPSS mapping profile and callback URL.
            using (context.Push(Binding(xades)))
                await delivery.DeliverAsync(result, ct);
            await CompleteAsync(db, eventId, operationId, attempt, PapssDeliveryState.Delivered, null, ct);
            Logger.LogInformation("PAPSS result {RequestMessageId} pushed to the bank on attempt {Attempt}", result.RequestMessageId, attempt);
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            if (attempt >= Math.Max(1, Options.Delivery.MaxAttempts))
            {
                await CompleteAsync(db, eventId, operationId, attempt, PapssDeliveryState.Failed, error.Message, ct);
                Logger.LogError(error, "PAPSS result {RequestMessageId} could not be pushed after {Attempt} attempts; bank push FAILED (the result stays available on the lookup API)", result.RequestMessageId, attempt);
                return;
            }
            var next = Clock.GetUtcNow().Add(Options.Delivery.Backoff(attempt));
            var message = Truncate(error.Message);
            await db.PapssOperationEvents.Where(x => x.Id == eventId && x.PushState == PapssDeliveryState.Pending && x.PushAttempts == attempt)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PushNextAttemptAt, next).SetProperty(x => x.PushLastError, message), ct);
            Logger.LogWarning(error, "PAPSS result {RequestMessageId} push attempt {Attempt} failed; retrying at {NextAttemptAt:o}", result.RequestMessageId, attempt, next);
        }
    }

    private async Task CompleteAsync(IStorageBroker db, long eventId, Guid? operationId, int attempt, PapssDeliveryState state, string? error, CancellationToken ct)
    {
        var now = Clock.GetUtcNow();
        var message = error is null ? null : Truncate(error);
        DateTimeOffset? deliveredAt = state == PapssDeliveryState.Delivered ? now : null;
        DateTimeOffset? noNextAttempt = null;
        var updated = await db.PapssOperationEvents.Where(x => x.Id == eventId && x.PushState == PapssDeliveryState.Pending && x.PushAttempts == attempt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PushState, state)
                .SetProperty(x => x.PushNextAttemptAt, noNextAttempt)
                .SetProperty(x => x.PushLastError, message)
                .SetProperty(x => x.PushDeliveredAt, deliveredAt), ct);
        if (updated == 1 && operationId is { } id)
            await db.PapssOperations.Where(x => x.Id == id && x.BankDeliveryState == PapssDeliveryState.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.BankDeliveryState, state).SetProperty(x => x.UpdatedAt, now), ct);
    }
}

/// <summary>Submits queued gateway-bound replies (papss_outbound_responses) byte-identically until admitted or rejected.</summary>
public sealed class PapssResponseOutboxWorker(IServiceScopeFactory scopes, PapssFacingOptions options, XadesOptions xades, IPapssOutboxSignal signal, TimeProvider clock, ILogger<PapssResponseOutboxWorker> logger)
    : PapssOutboxWorkerBase(scopes, options, signal, clock, logger)
{
    private static readonly Counter LateReplies = Metrics.CreateCounter(
        "sips_papss_reply_submitted_after_deadline_total",
        "PAPSS replies submitted (or held) after their recorded, configured response deadline.",
        new CounterConfiguration { LabelNames = ["message_type", "policy"] });

    public override async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var papss = scope.ServiceProvider.GetRequiredService<IPapssFacingSipsClient>();
        var now = Clock.GetUtcNow();
        var due = await db.PapssOutboundResponses.AsNoTracking()
            .Where(x => x.State == PapssResponseState.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
            .OrderBy(x => x.NextAttemptAt).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Attempts, x.OperationId, x.BizMsgIdr, x.MessageType, x.Operation!.DeadlineAt, x.Operation.RequestMessageId })
            .Take(BatchSize).ToListAsync(ct);
        var claimedCount = 0;
        foreach (var item in due)
        {
            var attempt = item.Attempts + 1;
            var lease = Clock.GetUtcNow().AddSeconds(Math.Max(10, Options.Delivery.ClaimLeaseSeconds));
            var claimed = await db.PapssOutboundResponses
                .Where(x => x.Id == item.Id && x.State == PapssResponseState.Pending && x.Attempts == item.Attempts)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Attempts, attempt).SetProperty(x => x.NextAttemptAt, lease), ct);
            if (claimed == 0) continue;
            claimedCount++;

            var submitAt = Clock.GetUtcNow();
            if (item.DeadlineAt is { } deadline && submitAt > deadline)
            {
                var policy = Options.Inbound.LateResponsePolicy;
                LateReplies.WithLabels(item.MessageType, policy.ToString()).Inc();
                if (policy == PapssLateResponsePolicy.Hold)
                {
                    await FinishAsync(db, item.Id, item.OperationId, attempt, PapssResponseState.Held, null, "PAST_DEADLINE", ct);
                    Logger.LogWarning("PAPSS reply {ReplyId} for {SourceMessageId} is past its recorded deadline {DeadlineAt:o}; held (LateResponsePolicy=Hold)", item.BizMsgIdr, item.RequestMessageId, deadline);
                    continue;
                }
                Logger.LogWarning("PAPSS reply {ReplyId} for {SourceMessageId} is submitted after its recorded deadline {DeadlineAt:o} (late by {LateSeconds:F1}s)", item.BizMsgIdr, item.RequestMessageId, deadline, (submitAt - deadline).TotalSeconds);
            }

            var signed = await db.PapssOutboundResponses.AsNoTracking().Where(x => x.Id == item.Id).Select(x => x.SignedXml).SingleAsync(ct);
            try
            {
                var admission = await papss.SubmitSignedAsync(Binding(xades), Encoding.UTF8.GetString(signed), ct);
                await FinishAsync(db, item.Id, item.OperationId, attempt, PapssResponseState.Admitted, admission.Code, null, ct);
                Logger.LogInformation("PAPSS reply {ReplyId} for {SourceMessageId} admitted by WP-SIPS with {AdmissionCode}", item.BizMsgIdr, item.RequestMessageId, admission.Code);
            }
            catch (ParticipantRailException rejected)
            {
                await FinishAsync(db, item.Id, item.OperationId, attempt, PapssResponseState.Rejected, rejected.Code, rejected.Code, ct);
                Logger.LogError(rejected, "PAPSS reply {ReplyId} for {SourceMessageId} was rejected by WP-SIPS with {AdmissionCode}", item.BizMsgIdr, item.RequestMessageId, rejected.Code);
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                // Transport errors, timeouts and unverifiable admissions are ambiguous: the gateway may have
                // stored the reply. Re-submitting the identical signed bytes is safe (EXACT_REPLAY).
                var message = Truncate(error.Message);
                var now2 = Clock.GetUtcNow();
                if (attempt >= Math.Max(1, Options.Delivery.MaxAttempts))
                {
                    await FinishAsync(db, item.Id, item.OperationId, attempt, PapssResponseState.Failed, null, message, ct);
                    Logger.LogError(error, "PAPSS reply {ReplyId} for {SourceMessageId} was not admitted after {Attempt} attempts; FAILED (re-queued automatically if PAPSS redelivers the enquiry)", item.BizMsgIdr, item.RequestMessageId, attempt);
                    continue;
                }
                var next = now2.Add(Options.Delivery.Backoff(attempt));
                await db.PapssOutboundResponses.Where(x => x.Id == item.Id && x.State == PapssResponseState.Pending && x.Attempts == attempt)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, next).SetProperty(x => x.LastError, message)
                        .SetProperty(x => x.SubmittedAt, now2).SetProperty(x => x.UpdatedAt, now2), ct);
                await db.PapssOperations.Where(x => x.Id == item.OperationId && x.GatewayState == PapssGatewayState.Submitting)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.GatewayState, PapssGatewayState.SubmissionUnknown).SetProperty(x => x.UpdatedAt, now2), ct);
                Logger.LogWarning(error, "PAPSS reply {ReplyId} submission attempt {Attempt} is ambiguous; retrying the identical signed bytes at {NextAttemptAt:o}", item.BizMsgIdr, attempt, next);
            }
        }
        return claimedCount;
    }

    private async Task FinishAsync(IStorageBroker db, long replyId, Guid operationId, int attempt, PapssResponseState state, string? admissionCode, string? error, CancellationToken ct)
    {
        var now = Clock.GetUtcNow();
        DateTimeOffset? submittedAt = state == PapssResponseState.Held ? null : now;
        DateTimeOffset? admittedAt = state == PapssResponseState.Admitted ? now : null;
        DateTimeOffset? noNextAttempt = null;
        var updated = await db.PapssOutboundResponses.Where(x => x.Id == replyId && x.State == PapssResponseState.Pending && x.Attempts == attempt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.State, state)
                .SetProperty(x => x.NextAttemptAt, noNextAttempt)
                .SetProperty(x => x.AdmissionCode, admissionCode)
                .SetProperty(x => x.LastError, error)
                .SetProperty(x => x.SubmittedAt, submittedAt)
                .SetProperty(x => x.AdmittedAt, admittedAt)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (updated != 1) return;

        var operation = db.PapssOperations.Where(x => x.Id == operationId);
        switch (state)
        {
            case PapssResponseState.Admitted:
                await operation.ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.GatewayState, PapssGatewayState.Admitted)
                    .SetProperty(x => x.AdmissionCode, admissionCode)
                    .SetProperty(x => x.CompletedAt, now)
                    .SetProperty(x => x.UpdatedAt, now), ct);
                break;
            case PapssResponseState.Rejected:
                await operation.ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.GatewayState, PapssGatewayState.Rejected)
                    .SetProperty(x => x.AdmissionCode, admissionCode)
                    .SetProperty(x => x.ReasonCode, admissionCode)
                    .SetProperty(x => x.CompletedAt, now)
                    .SetProperty(x => x.UpdatedAt, now), ct);
                break;
            case PapssResponseState.Held:
                await operation.ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.GatewayState, PapssGatewayState.NotSubmitted)
                    .SetProperty(x => x.ReasonCode, "PAST_DEADLINE")
                    .SetProperty(x => x.UpdatedAt, now), ct);
                break;
            default:
                await operation.ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.GatewayState, PapssGatewayState.SubmissionUnknown)
                    .SetProperty(x => x.UpdatedAt, now), ct);
                break;
        }
    }
}

/// <summary>Optional retention purge. Disabled unless PapssFacing:Store:RetentionDays is set.</summary>
public sealed class PapssStoreRetentionWorker(IServiceScopeFactory scopes, PapssFacingOptions options, TimeProvider clock, ILogger<PapssStoreRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Store.RetentionDays is null) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PurgeAsync(stoppingToken); }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested) { logger.LogError(error, "PAPSS store retention purge failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Store.PurgeIntervalMinutes)), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Deletes completed operations and settled events older than the retention period. Returns (operations, events).</summary>
    public async Task<(int Operations, int Events)> PurgeAsync(CancellationToken ct)
    {
        if (options.Store.RetentionDays is not { } days || days <= 0) return (0, 0);
        var cutoff = clock.GetUtcNow().AddDays(-days);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var events = await db.PapssOperationEvents
            .Where(x => x.ReceivedAt < cutoff && x.PushState != PapssDeliveryState.Pending)
            .ExecuteDeleteAsync(ct);
        // Replies cascade with their operation; never purge work that is still in flight.
        var operations = await db.PapssOperations
            .Where(x => x.CompletedAt != null && x.CompletedAt < cutoff && x.BankDeliveryState != PapssDeliveryState.Pending)
            .Where(x => !db.PapssOutboundResponses.Any(r => r.OperationId == x.Id && r.State == PapssResponseState.Pending))
            .Where(x => !db.PapssOperationEvents.Any(e => e.OperationId == x.Id && e.PushState == PapssDeliveryState.Pending))
            .ExecuteDeleteAsync(ct);
        if (operations + events > 0)
            logger.LogInformation("PAPSS store retention purged {Operations} operations and {Events} events older than {Cutoff:o}", operations, events, cutoff);
        return (operations, events);
    }
}
