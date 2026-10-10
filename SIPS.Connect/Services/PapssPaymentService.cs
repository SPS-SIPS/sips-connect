using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SIPS.Connect.Config;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using Rules = SIPS.Connect.Services.PapssPaymentStatusRules;

namespace SIPS.Connect.Services;

/// <summary>Result of /Status on the PAPSS rail.</summary>
/// <param name="Payment">The stored payment (null when the store does not know the TxId).</param>
/// <param name="Enquiry">Admission of the pacs.028 sent for this request, if one was sent.</param>
/// <param name="EnquiryError">Why a pacs.028 that was attempted was not admitted (the stored state is still returned).</param>
public sealed record PapssStatusResult(PapssOperation? Payment, PapssAdmissionResponse? Enquiry, string? EnquiryError, string? EnquiryRequestMessageId);

public interface IPapssPaymentService
{
    Task<PapssAdmissionResponse> PayAsync(PapssParticipantBinding participant, PaymentRequestDto request, CancellationToken ct);
    Task<PapssAdmissionResponse> ReturnAsync(PapssParticipantBinding participant, ReturnPaymentRequestDto request, CancellationToken ct);
    Task<PapssStatusResult> StatusAsync(PapssParticipantBinding participant, StatusRequestDto request, CancellationToken ct);
    /// <summary>
    /// Recalls (camt.056) one of this participant's settled OUTBOUND PAPSS payments. The recall is stored before submission;
    /// the admission handling is the payment one (recall id = idempotency key).
    /// </summary>
    Task<PapssAdmissionResponse> RecallAsync(PapssParticipantBinding participant, PapssRecallRequest request, CancellationToken ct)
        => throw new NotSupportedException("This PAPSS payment service cannot recall payments.");
    /// <summary>
    /// R2: the core bank's accept/reject decision on an inbound recall (a counterparty recalling a payment this institution
    /// received). ACCEPT delegates to the existing <see cref="ReturnAsync"/> pipeline (a pacs.004 is a pacs.004 regardless of
    /// why it is sent); REJECT builds and submits a new camt.029.001.08 rejection request.
    /// </summary>
    Task<PapssInboundRecallDecisionResponse> DecideInboundRecallAsync(PapssParticipantBinding participant, string recallId, PapssInboundRecallDecisionRequest request, CancellationToken ct)
        => throw new NotSupportedException("This PAPSS payment service cannot decide inbound recalls.");
    /// <summary>Bank-facing view of a stored operation, with payment fields and status history where relevant.</summary>
    Task<PapssOperationResult> DescribeAsync(PapssOperation operation, CancellationToken ct);
}

/// <summary>
/// Outbound PAPSS payments, returns and status enquiries. Every message is persisted (with its signed bytes)
/// before the gateway is called, and the bank's TxId / ReturnId is the idempotency key: the same key with the
/// same content replays the stored admission (or re-submits the stored bytes when the earlier outcome was
/// ambiguous); the same key with different content is DUPLICATE_CONFLICT without calling the gateway.
/// </summary>
public sealed class PapssPaymentService(
    PapssOperationStore store,
    IPapssFacingSipsClient papss,
    PapssFacingOptions options,
    TimeProvider clock,
    ILogger<PapssPaymentService> logger) : IPapssPaymentService
{
    public async Task<PapssAdmissionResponse> PayAsync(PapssParticipantBinding participant, PaymentRequestDto request, CancellationToken ct)
    {
        var txId = Required(request.TxId, "transaction identifier");
        var fingerprint = Fingerprint(request);
        if (await store.FindOutboundPaymentByTxIdAsync(txId, ct) is { } existing)
            return await ReplayAsync(participant, existing, fingerprint, "payment", ct);

        var prepared = await papss.PreparePaymentAsync(participant, request, ct);
        var (operation, created) = await store.CreateOutboundPaymentAsync(prepared, request, fingerprint, ct);
        if (!created) return await ReplayAsync(participant, operation, fingerprint, "payment", ct);
        return await SubmitAsync(participant, operation, prepared.SignedXml, ct);
    }

    public async Task<PapssAdmissionResponse> ReturnAsync(PapssParticipantBinding participant, ReturnPaymentRequestDto request, CancellationToken ct)
    {
        var returnId = Required(request.ReturnId, "return identifier");
        var fingerprint = Fingerprint(request);
        if (await store.FindOutboundReturnAsync(returnId, ct) is { } existing)
            return await ReplayAsync(participant, existing, fingerprint, "return", ct);

        var prepared = papss.PrepareReturn(participant, request);
        var original = await store.FindOriginalPaymentAsync(request.OriginalTxId, request.OriginalEndToEndId, preferInbound: true, ct);
        if (original is null)
            logger.LogWarning("PAPSS return {ReturnId} refers to payment {TxId} that is not in the operation store; stored without a link", returnId, request.OriginalTxId);
        var (operation, created) = await store.CreateOutboundReturnAsync(prepared, request, fingerprint, original, ct);
        if (!created) return await ReplayAsync(participant, operation, fingerprint, "return", ct);
        return await SubmitAsync(participant, operation, prepared.SignedXml, ct);
    }

    public async Task<PapssAdmissionResponse> RecallAsync(PapssParticipantBinding participant, PapssRecallRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Rail) && !request.Rail.Trim().Equals("PAPSS", StringComparison.OrdinalIgnoreCase))
            throw new ParticipantRailException("OPERATION_NOT_SUPPORTED", "Recall is only available on the PAPSS rail.");
        var reason = PapssRecallMessages.NormalizeReason(request.Reason);
        var txId = NullIfBlank(request.TxId);
        var endToEndId = NullIfBlank(request.EndToEndId);
        if (txId is null && endToEndId is null) throw new ArgumentException("The transaction identifier (txId) or end-to-end identifier of the payment to recall is required.");
        var recallId = NullIfBlank(request.RecallId);
        if (recallId is not null && !PapssRecallMessages.IsRecallId(recallId))
            throw new ArgumentException("recallId must be 'SIPS-' followed by 24 lowercase hexadecimal characters (or be omitted).");

        var payment = await FindRecallablePaymentAsync(txId, endToEndId, ct);
        var fingerprint = Hash("RECALL", payment.TxId, payment.EndToEndId, reason);
        if (recallId is not null && await store.FindOutboundRecallAsync(recallId, ct) is { } existing)
            return await ReplayAsync(participant, existing, fingerprint, "recall", ct);

        // Preconditions for a NEW recall (a replay above is answered from the stored recall whatever the payment state now is).
        if (payment.GatewayState == PapssGatewayState.Rejected || payment.PapssOutcome is PapssOutcome.Rejected or PapssOutcome.Returned
            || options.Recall.RequireSettledOriginal && payment.PapssOutcome != PapssOutcome.Settled)
            throw new ParticipantRailException("ORIGINAL_NOT_SETTLED",
                $"Payment {payment.TxId} is {UpperSnakeEnumConverter<PapssOutcome>.Of(payment.PapssOutcome)}; only a settled PAPSS payment can be recalled.");
        // PAPSS-confirmed 2026-09-26: recall is possible up to 30 days after settlement (PapssFacing:Recall:MaxAgeDays).
        var settledAt = payment.CompletedAt ?? payment.StatusAt ?? payment.CreatedAt;
        if (options.Recall.MaxAgeDays is { } maxAge && clock.GetUtcNow() - settledAt > TimeSpan.FromDays(maxAge))
            throw new ParticipantRailException("RECALL_WINDOW_EXPIRED", $"Payment {payment.TxId} settled more than {maxAge} days ago and can no longer be recalled.");
        if (await store.FindOpenRecallAsync(payment.Id, ct) is { } open)
            throw new ParticipantRailException("RECALL_ALREADY_OPEN", $"Payment {payment.TxId} already has an open recall {open.RequestMessageId}; only one recall per payment may be open.");
        if (payment.MsgId is null || payment.EndToEndId is null || payment.Amount is null || string.IsNullOrWhiteSpace(payment.Currency))
            throw new ArgumentException($"Payment {payment.TxId} is stored without its message id, end-to-end id, amount or currency and cannot be recalled.");

        var id = recallId ?? PapssFacingSipsClient.Id();
        var createdAt = clock.GetUtcNow();
        var unsigned = PapssRecallMessages.BuildRecallRequest(new PapssRecallInstruction(
            id, participant.Bic, options.RemoteWpSipsIdentity, payment.MsgId, payment.EndToEndId, payment.TxId!, payment.Amount.Value, payment.Currency!, reason, createdAt));
        var signed = new PapssSignedMessage(id, papss.SignForSubmission(unsigned, id), createdAt, id);
        var (operation, created) = await store.CreateOutboundRecallAsync(signed, payment, reason, fingerprint, ct);
        if (!created) return await ReplayAsync(participant, operation, fingerprint, "recall", ct);
        logger.LogInformation("PAPSS recall {RecallId} of payment {TxId} (reason {Reason}) stored; submitting camt.056", id, payment.TxId, reason);
        return await SubmitAsync(participant, operation, signed.SignedXml, ct);
    }

    /// <summary>
    /// R2: ACCEPT delegates to <see cref="ReturnAsync"/> with a deterministic ReturnId (derived from the recall id), so a
    /// retried decision replays through ReturnAsync's own idempotency rather than needing separate handling here. REJECT
    /// builds a deterministic camt.029.001.08 rejection (also derived from the recall id, so a retry resubmits byte-identical
    /// content and the gateway's own exact-replay detection applies) and submits it directly. A decision already recorded
    /// (ACCEPTED_BY_BANK/REJECTED_BY_BANK/REPLY_SUBMITTED) refuses a DIFFERENT decision outright (DECISION_ALREADY_MADE,
    /// recorded for audit but never applied -- see RecordInboundRecallDecisionAsync); the bank must GET the recall first to
    /// see what was already decided before retrying the SAME one past REPLY_SUBMITTED.
    /// </summary>
    public async Task<PapssInboundRecallDecisionResponse> DecideInboundRecallAsync(PapssParticipantBinding participant, string recallId, PapssInboundRecallDecisionRequest request, CancellationToken ct)
    {
        var decision = (request.Decision ?? string.Empty).Trim().ToUpperInvariant();
        if (decision is not ("ACCEPT" or "REJECT")) throw new ArgumentException("decision must be exactly ACCEPT or REJECT.");
        var recall = await store.FindInboundRecallAsync(recallId, ct)
            ?? throw new ParticipantRailException("OPERATION_NOT_FOUND", $"No inbound PAPSS recall is stored for recallId {recallId}.");
        if (recall.OriginalOperationId is not { } paymentId)
            throw new ParticipantRailException("ORIGINAL_PAYMENT_NOT_FOUND", "This inbound recall is not linked to a stored received payment and cannot be decided.");
        var payment = await store.FindByIdAsync(paymentId, ct)
            ?? throw new ParticipantRailException("ORIGINAL_PAYMENT_NOT_FOUND", "The received payment this recall names is no longer in the store.");

        var accept = decision == "ACCEPT";
        var outcome = await store.RecordInboundRecallDecisionAsync(recall.Id, accept, request.Reason, null, ct);
        if (outcome == PapssInboundRecallDecisionOutcome.Conflict)
            throw new ParticipantRailException("DECISION_ALREADY_MADE", $"Inbound recall {recallId} already has a different decision recorded ({UpperSnakeEnumConverter<PapssOutcome>.Of(recall.PapssOutcome)}); this one was logged but not applied.");

        if (accept)
        {
            var returnRequest = new ReturnPaymentRequestDto
            {
                ReturnId = DeterministicId("RETURN", recallId),
                OriginalTxId = Required(payment.TxId, "received payment TxId"),
                OriginalEndToEndId = Required(payment.EndToEndId, "received payment EndToEndId"),
                OriginalAmount = payment.Amount ?? throw new InvalidDataException("The received payment has no stored amount."),
                OriginalCurrency = Required(payment.Currency, "received payment currency"),
                LocalInstrument = Required(payment.LocalInstrument, "received payment local instrument"),
                // PAPSS's own pacs.004 carries no CtgyPurp (see PapssOutboundEffectMapper.Return, gateway side) and the
                // inbound pacs.008 parser does not capture one (PapssPaymentMessage has no CategoryPurpose field): this is
                // validated by the WP-SIPS pacs.004 builder but never reaches PAPSS, so a neutral placeholder is safe here.
                CategoryPurpose = "CASH",
                Reason = PapssRecallMessages.NormalizeReason(request.Reason ?? recall.Reason ?? "DUPL"),
                AdditionalInfo = string.Empty,
                ToBIC = Required(payment.CounterpartyBic, "received payment counterparty BIC")
            };
            var admission = await ReturnAsync(participant, returnRequest, ct);
            await store.RecordInboundRecallReplySubmittedAsync(recall.Id, ct);
            logger.LogInformation("Inbound PAPSS recall {RecallId} ACCEPTED; pacs.004 {ReturnId} submitted for payment {TxId}", recallId, returnRequest.ReturnId, payment.TxId);
            return new(recallId, "ACCEPT", admission, returnRequest.ReturnId);
        }
        else
        {
            var reasonCode = PapssRecallMessages.NormalizeReason(request.Reason);
            var rejectionId = DeterministicId("REJECT", recallId);
            var unsigned = PapssRecallMessages.BuildInboundRecallRejection(new PapssRecallMessages.PapssInboundRecallRejection(
                rejectionId, participant.Bic, options.RemoteWpSipsIdentity, Required(payment.TxId, "received payment TxId"), Required(payment.EndToEndId, "received payment EndToEndId"),
                Required(payment.CounterpartyBic, "received payment counterparty BIC"), reasonCode, clock.GetUtcNow()));
            var signed = papss.SignForSubmission(unsigned, rejectionId);
            var admission = await papss.SubmitSignedAsync(participant, signed, ct);
            await store.RecordInboundRecallReplySubmittedAsync(recall.Id, ct);
            logger.LogInformation("Inbound PAPSS recall {RecallId} REJECTED (reason {Reason}); camt.029 {RejectionId} submitted for payment {TxId}", recallId, reasonCode, rejectionId, payment.TxId);
            return new(recallId, "REJECT", admission, null);
        }
    }

    /// <summary>Deterministic, stable across retries (same recall id + same purpose = same wire id), in the contract id shape.</summary>
    private static string DeterministicId(string purpose, string recallId)
        => "SIPS-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"INBOUND-RECALL-{purpose}:{recallId}")))[..24].ToLowerInvariant();

    /// <summary>The OUTBOUND payment a recall names (only the debtor agent may recall: a received payment is refused).</summary>
    private async Task<PapssOperation> FindRecallablePaymentAsync(string? txId, string? endToEndId, CancellationToken ct)
    {
        PapssOperation? payment;
        if (txId is not null)
        {
            payment = await store.FindOutboundPaymentByTxIdAsync(txId, ct);
            if (payment is not null && endToEndId is not null && payment.EndToEndId is not null && !string.Equals(endToEndId, payment.EndToEndId, StringComparison.Ordinal))
                throw new ArgumentException("The end-to-end identifier does not match the stored PAPSS payment for this transaction identifier.");
        }
        else
        {
            var matches = await store.FindOutboundPaymentsByEndToEndIdAsync(endToEndId!, ct);
            if (matches.Count > 1) throw new ArgumentException("More than one PAPSS payment has this end-to-end identifier; recall it by txId.");
            payment = matches.SingleOrDefault();
        }
        if (payment is not null) return payment;
        if (await store.HasInboundPaymentAsync(txId, endToEndId, ct))
            throw new ParticipantRailException("RECALL_NOT_ALLOWED", "Only a payment this participant sent (OUTBOUND) can be recalled; this payment was received.");
        throw new ParticipantRailException("ORIGINAL_PAYMENT_NOT_FOUND", "No PAPSS payment sent by this participant is stored for this identifier.");
    }

    public async Task<PapssStatusResult> StatusAsync(PapssParticipantBinding participant, StatusRequestDto request, CancellationToken ct)
    {
        Required(request.TxId, "transaction identifier");
        var payment = await store.FindPaymentAsync(request.TxId, ct);
        if (payment is null)
        {
            // Not in the store (e.g. submitted before the store existed): unchanged behaviour, a pacs.028 is sent.
            var (admission, enquiryId) = await EnquireAsync(participant, request, null, ct);
            return new PapssStatusResult(null, admission, null, enquiryId);
        }

        if (!string.IsNullOrWhiteSpace(request.EndToEnd) && payment.EndToEndId is not null && !string.Equals(request.EndToEnd, payment.EndToEndId, StringComparison.Ordinal))
            throw new ArgumentException("The end-to-end identifier does not match the stored PAPSS payment for this transaction identifier.");

        if (!ShouldEnquire(payment)) return new PapssStatusResult(payment, null, null, null);

        request.EndToEnd = string.IsNullOrWhiteSpace(request.EndToEnd) ? payment.EndToEndId! : request.EndToEnd;
        request.ToBIC = string.IsNullOrWhiteSpace(request.ToBIC) ? payment.CounterpartyBic! : request.ToBIC;
        try
        {
            var (admission, enquiryId) = await EnquireAsync(participant, request, payment, ct);
            return new PapssStatusResult(await store.FindByIdAsync(payment.Id, ct) ?? payment, admission, null, enquiryId);
        }
        catch (ParticipantRailException rejected)
        {
            return new PapssStatusResult(payment, null, rejected.Code, null);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(error, "PAPSS status enquiry for payment {TxId} was not admitted; returning the stored state", payment.TxId);
            return new PapssStatusResult(payment, null, SubmissionFailure(error), null);
        }
    }

    /// <summary>
    /// A pacs.028 is only useful while the payment is non-final (evidence §E11: no pacs.002 received, stuck pending,
    /// confirming finality) and only this participant's own (OUTBOUND) payments are enquired. Any minimum age is
    /// NOT ESTABLISHED by PAPSS: PapssFacing:Status:EnquiryMinimumAgeSeconds (default unset = always enquire).
    /// </summary>
    public bool ShouldEnquire(PapssOperation payment)
    {
        if (payment.Direction != PapssDirection.Outbound || Rules.IsFinal(payment.PapssOutcome) || payment.GatewayState == PapssGatewayState.Rejected) return false;
        return options.Status.EnquiryMinimumAgeSeconds is not { } minimum || clock.GetUtcNow() - payment.CreatedAt >= TimeSpan.FromSeconds(minimum);
    }

    private async Task<(PapssAdmissionResponse Admission, string EnquiryId)> EnquireAsync(PapssParticipantBinding participant, StatusRequestDto request, PapssOperation? payment, CancellationToken ct)
    {
        var prepared = papss.PrepareStatus(participant, request);
        var enquiry = await store.CreateStatusEnquiryAsync(prepared, request, payment, ct);
        return (await SubmitAsync(participant, enquiry, prepared.SignedXml, ct), enquiry.RequestMessageId);
    }

    public async Task<PapssOperationResult> DescribeAsync(PapssOperation operation, CancellationToken ct)
    {
        // The decision outbox (isomessages) is authoritative for an inbound payment's decision; refresh the mirror.
        if (operation is { Direction: PapssDirection.Inbound, Operation: PapssOperationType.Payment } && operation.GatewayState is not (PapssGatewayState.Admitted or PapssGatewayState.Rejected))
        {
            await store.SyncInboundPaymentDecisionAsync(operation.Id, ct);
            operation = await store.FindByIdAsync(operation.Id, ct) ?? operation;
        }
        var reply = operation.Direction == PapssDirection.Inbound && operation.Operation == PapssOperationType.VerificationEnquiry
            ? await store.FindReplyAsync(operation.Id, ct) : null;
        var result = PapssOperationResult.From(operation, reply, clock.GetUtcNow(), options.Outbound.VerificationResultExpirySeconds);
        if (!PapssOperationResult.IsPaymentLike(operation)) return result;
        var original = operation.OriginalOperationId is { } originalId ? await store.FindByIdAsync(originalId, ct) : null;
        return result.WithPayment(operation, await store.StatusHistoryAsync(operation.Id, ct), original, await store.FindLinkedAsync(operation.Id, ct), clock.GetUtcNow());
    }

    // ----------------------------------------------------------------------------------------

    private async Task<PapssAdmissionResponse> ReplayAsync(PapssParticipantBinding participant, PapssOperation operation, string fingerprint, string kind, CancellationToken ct)
    {
        if (!string.Equals(operation.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            logger.LogWarning("PAPSS {Kind} {RequestMessageId} (TxId={TxId}, ReturnId={ReturnId}) was requested again with different content; DUPLICATE_CONFLICT, gateway not called",
                kind, operation.RequestMessageId, operation.TxId, operation.ReturnId);
            throw new ParticipantRailException("DUPLICATE_CONFLICT", $"The {kind} identifier was already used for a different {kind}.");
        }
        switch (operation.GatewayState)
        {
            case PapssGatewayState.Admitted:
                return new PapssAdmissionResponse(operation.RequestMessageId, operation.AdmissionCode ?? "EXACT_REPLAY", true);
            case PapssGatewayState.Rejected:
                throw new ParticipantRailException(operation.AdmissionCode ?? "REJECTED_BEFORE_EXTERNAL_EFFECT", $"The {kind} was previously rejected by WP-SIPS.");
        }
        // Earlier attempt ambiguous (or still in flight): re-submit the stored signed bytes unchanged; the gateway answers EXACT_REPLAY.
        return await SubmitAsync(participant, operation, Encoding.UTF8.GetString(operation.SignedRequest!), ct);
    }

    private async Task<PapssAdmissionResponse> SubmitAsync(PapssParticipantBinding participant, PapssOperation operation, string signed, CancellationToken ct)
    {
        PapssAdmissionResponse admission;
        try
        {
            admission = await papss.SubmitSignedAsync(participant, signed, ct);
        }
        catch (ParticipantRailException rejected)
        {
            await store.MarkGatewayRejectedAsync(operation.Id, rejected.Code, CancellationToken.None);
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await store.MarkGatewaySubmissionUnknownAsync(operation.Id, SubmissionFailure(error), CancellationToken.None);
            throw;
        }
        await store.MarkGatewayAdmittedAsync(operation.Id, admission.Code, CancellationToken.None);
        if (operation.Operation == PapssOperationType.Recall)
            await store.RecordRecallDeadlineAsync(operation.Id, CancellationToken.None);
        return admission;
    }

    private static string SubmissionFailure(Exception error) => error switch
    {
        UnauthorizedAccessException => "INVALID_SIGNED_RESPONSE",
        InvalidDataException => "INVALID_WP_SIPS_RESPONSE",
        OperationCanceledException => "WP_SIPS_TIMEOUT",
        _ => "WP_SIPS_UNAVAILABLE"
    };

    /// <summary>SHA-256 over the bank request's business content, normalized (codes upper-cased, amount invariant).</summary>
    public static string Fingerprint(PaymentRequestDto x) => Hash(
        "PAYMENT", Code(x.ToBIC), x.TxId, x.EndToEndId, Amount(x.Amount), Code(x.Currency), Code(x.ReceiverCurrency), Code(x.LocalInstrument), Code(x.CategoryPurpose),
        x.DebtorName, x.DebtorAccount, x.DebtorAccountType, Code(x.DebtorAgentBIC), x.CreditorName, x.CreditorAccount, x.CreditorAccountType, Code(x.CreditorAgentBIC),
        x.RemittanceInformation);

    public static string Fingerprint(ReturnPaymentRequestDto x) => Hash(
        "RETURN", Code(x.ToBIC), x.ReturnId, x.OriginalTxId, x.OriginalEndToEndId, Amount(x.OriginalAmount), Code(x.OriginalCurrency), Code(x.LocalInstrument), Code(x.CategoryPurpose),
        x.Reason, x.AdditionalInfo);

    private static string Hash(params string?[] parts)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts.Select(p => p?.Trim() ?? string.Empty))))).ToLowerInvariant();
    private static string? Code(string? value) => value?.Trim().ToUpperInvariant();
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Amount(decimal value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
    private static string Required(string? value, string name) => !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Required PAPSS field missing: {name}.");
}
