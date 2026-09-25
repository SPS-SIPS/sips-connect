using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using SIPS.Adapter;
using SIPS.Connect.Config;
using SIPS.Connect.Controllers;
using SIPS.Connect.Services;
using SIPS.Core.Interfaces;
using SIPS.Core.Options;
using SIPS.ISO20022.Interfaces;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using Xunit;

namespace SIPS.Connect.PostgresTests;

[Trait("Category", "Postgres")]
public sealed class PapssOperationStoreTests
{
    [Fact]
    public async Task Migrations_apply_on_an_empty_database()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var connection = new NpgsqlConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await using var tables = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_name IN ('papss_operations','papss_operation_events','papss_outbound_responses','isomessages')", connection);
        Assert.Equal(4L, (long)(await tables.ExecuteScalarAsync())!);
        await using var history = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE migrationid LIKE '%_AddPapssOperationStore'", connection);
        Assert.Equal(1L, (long)(await history.ExecuteScalarAsync())!);
        await using var unique = new NpgsqlCommand("SELECT count(*) FROM pg_indexes WHERE indexname IN ('ux_papss_event_type_source_msg','ux_papss_op_direction_request_msg','ux_papss_response_biz_msg_idr','ux_papss_response_operation')", connection);
        Assert.Equal(4L, (long)(await unique.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Outbound_verify_is_stored_before_submission_and_follows_the_admission_and_result()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        string? storedStateAtSubmit = null;
        harness.Gateway_.OnSubmit = (_, xml) =>
        {
            // The row must already exist, SUBMITTING, with the exact signed bytes, when the gateway is called.
            var id = PostgresHarness.Header(xml, "BizMsgIdr");
            storedStateAtSubmit = Task.Run(() => harness.WithStorageAsync(async db =>
            {
                var row = await db.PapssOperations.AsNoTracking().SingleAsync(x => x.RequestMessageId == id);
                return row.GatewayState + "|" + (Encoding.UTF8.GetString(row.SignedRequest!) == xml);
            })).GetAwaiter().GetResult();
            return "RECEIVED_AND_DURABLY_ADMITTED";
        };

        var response = await Verify(harness, provider);
        var requestMessageId = response["requestMessageId"]!.GetValue<string>();
        Assert.Equal("RECEIVED_AND_DURABLY_ADMITTED", response["code"]!.GetValue<string>());
        Assert.True(response["durablyAdmitted"]!.GetValue<bool>());
        Assert.Equal("Submitting|True", storedStateAtSubmit);

        var admitted = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.RequestMessageId == requestMessageId));
        Assert.Equal(PapssGatewayState.Admitted, admitted.GatewayState);
        Assert.Equal(PapssOutcome.Pending, admitted.PapssOutcome);
        Assert.Equal("RECEIVED_AND_DURABLY_ADMITTED", admitted.AdmissionCode);
        Assert.Equal("PENDING", (await Lookup(harness, provider, requestMessageId))["status"]!.GetValue<string>());

        // The acmt.024 result arrives, is stored, and is pushed to the bank by the worker.
        await HandleResult(provider, harness, PostgresHarness.VerificationResult(requestMessageId, verified: true));
        var completed = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.RequestMessageId == requestMessageId));
        Assert.Equal(PapssOutcome.VerifiedMatch, completed.PapssOutcome);
        Assert.Equal(PapssDeliveryState.Pending, completed.BankDeliveryState);
        Assert.Equal("FORTRESS GLOBAL", completed.AccountName);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(0, harness.Bank.Count); // not delivered inline any more

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, harness.Bank.Count);
        var call = harness.Bank.Calls.Single();
        Assert.Equal(PostgresHarness.CallbackUrl, call.Url);
        Assert.Equal(requestMessageId, call.Headers!["X-Idempotency-Key"]);
        Assert.Equal(requestMessageId, call.Body["requestMessageId"]!.GetValue<string>());
        Assert.True(call.Body["verified"]!.GetValue<bool>());

        var lookup = await Lookup(harness, provider, requestMessageId);
        Assert.Equal("COMPLETED", lookup["status"]!.GetValue<string>());
        Assert.Equal("OUTBOUND", lookup["direction"]!.GetValue<string>());
        Assert.Equal("VERIFICATION", lookup["operation"]!.GetValue<string>());
        Assert.Equal("ADMITTED", lookup["gatewayState"]!.GetValue<string>());
        Assert.Equal("VERIFIED_MATCH", lookup["papssOutcome"]!.GetValue<string>());
        Assert.Equal("DELIVERED", lookup["bankDeliveryState"]!.GetValue<string>());
        Assert.True(lookup["verified"]!.GetValue<bool>());
        Assert.Equal("FORTRESS GLOBAL", lookup["accountName"]!.GetValue<string>());
        Assert.Equal("0012030321735", lookup["accountNumber"]!.GetValue<string>());
        Assert.Equal("SLE", lookup["currency"]!.GetValue<string>());
        Assert.NotNull(lookup["completedAt"]);
    }

    [Fact]
    public async Task Outbound_verify_records_rejection_and_transport_ambiguity()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();

        harness.Gateway_.OnSubmit = (_, _) => throw new ParticipantRailException("DUPLICATE_CONFLICT", "rejected");
        var rejected = await VerifyRaw(harness, provider);
        Assert.IsType<BadRequestObjectResult>(rejected);
        harness.Gateway_.OnSubmit = (_, _) => throw new HttpRequestException("connection reset");
        var ambiguous = await VerifyRaw(harness, provider);
        Assert.Equal(503, StatusCodes503(ambiguous));

        var rows = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.GatewayState).ToListAsync());
        Assert.Contains(rows, x => x.GatewayState == PapssGatewayState.Rejected && x.AdmissionCode == "DUPLICATE_CONFLICT" && x.PapssOutcome == PapssOutcome.Rejected);
        var unknown = Assert.Single(rows, x => x.GatewayState == PapssGatewayState.SubmissionUnknown);
        Assert.Equal("WP_SIPS_UNAVAILABLE", unknown.ReasonCode);
        Assert.Equal("UNKNOWN", (await Lookup(harness, provider, unknown.RequestMessageId))["status"]!.GetValue<string>());

        // A result for the ambiguous submission proves admission.
        await HandleResult(provider, harness, PostgresHarness.VerificationResult(unknown.RequestMessageId, verified: false));
        var settled = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == unknown.Id));
        Assert.Equal(PapssGatewayState.Admitted, settled.GatewayState);
        Assert.Equal(PapssOutcome.VerifiedNoMatch, settled.PapssOutcome);
    }

    [Fact]
    public async Task Duplicate_result_callback_is_stored_once_and_pushed_once()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var requestMessageId = (await Verify(harness, provider))["requestMessageId"]!.GetValue<string>();
        var report = PostgresHarness.VerificationResult(requestMessageId, verified: true);

        Assert.Equal(string.Empty, await HandleResult(provider, harness, report));
        Assert.Equal(string.Empty, await HandleResult(provider, harness, report));
        var worker = provider.GetRequiredService<PapssBankPushWorker>();
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(string.Empty, await HandleResult(provider, harness, report)); // redelivered after the push
        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.VerificationResult)));
        Assert.Equal(1, harness.Bank.Count);
    }

    [Fact]
    public async Task Concurrent_duplicate_result_deliveries_store_exactly_one_event()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var requestMessageId = (await Verify(harness, provider))["requestMessageId"]!.GetValue<string>();
        var report = PostgresHarness.VerificationResult(requestMessageId, verified: true);
        var parsed = SIPS.ISO20022.Helpers.PayeeVerificationResponseBuilder.Parse(report);
        var dto = SIPS.Core.Services.IncomingVerificationResponseHandler.Map(parsed);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var scope = provider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
            return await store.IngestVerificationResultAsync(report, parsed, dto, CancellationToken.None);
        }));

        Assert.Single(outcomes, x => x == VerificationResultInboxOutcome.Stored);
        Assert.Equal(5, outcomes.Count(x => x == VerificationResultInboxOutcome.Duplicate));
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.PushState == PapssDeliveryState.Pending)));
    }

    [Fact]
    public async Task Unmatched_result_is_stored_uncorrelated_and_still_pushed()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await HandleResult(provider, harness, PostgresHarness.VerificationResult("SIPS-UNKNOWN-0000000000001", verified: true));
        var stored = await harness.WithStorageAsync(db => db.PapssOperationEvents.AsNoTracking().SingleAsync());
        Assert.Null(stored.OperationId);
        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, harness.Bank.Count);
    }

    [Fact]
    public async Task Bank_push_retries_with_backoff_then_delivers()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var requestMessageId = (await Verify(harness, provider))["requestMessageId"]!.GetValue<string>();
        await HandleResult(provider, harness, PostgresHarness.VerificationResult(requestMessageId, verified: true));
        var worker = provider.GetRequiredService<PapssBankPushWorker>();

        harness.Bank.Status = HttpStatusCode.ServiceUnavailable;
        await worker.RunOnceAsync(CancellationToken.None);
        var pending = await harness.WithStorageAsync(db => db.PapssOperationEvents.AsNoTracking().SingleAsync());
        Assert.Equal(PapssDeliveryState.Pending, pending.PushState);
        Assert.Equal(1, pending.PushAttempts);
        Assert.Equal(harness.Clock.GetUtcNow().AddSeconds(5), pending.PushNextAttemptAt);
        Assert.NotNull(pending.PushLastError);
        await worker.RunOnceAsync(CancellationToken.None); // not due yet
        Assert.Equal(1, harness.Bank.Count);

        harness.Bank.Status = HttpStatusCode.OK;
        harness.Clock.Advance(TimeSpan.FromSeconds(6));
        await worker.RunOnceAsync(CancellationToken.None);
        var delivered = await harness.WithStorageAsync(db => db.PapssOperationEvents.AsNoTracking().SingleAsync());
        Assert.Equal(PapssDeliveryState.Delivered, delivered.PushState);
        Assert.Equal(2, delivered.PushAttempts);
        Assert.Equal(2, harness.Bank.Count);
        Assert.Equal(PapssDeliveryState.Delivered, (await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync())).BankDeliveryState);
    }

    [Fact]
    public async Task Inbound_enquiry_redelivery_calls_the_core_bank_once_and_queues_one_reply()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var enquiry = PostgresHarness.InboundEnquiry("PAPSS-MSG-000001", "PAPSS-VID-000001");

        await HandleEnquiry(provider, harness, enquiry);
        await HandleEnquiry(provider, harness, enquiry);
        Assert.Equal(1, harness.CoreBank.Calls);

        var worker = provider.GetRequiredService<PapssResponseOutboxWorker>();
        await worker.RunOnceAsync(CancellationToken.None);
        await HandleEnquiry(provider, harness, enquiry); // redelivered after the reply was admitted
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, harness.CoreBank.Calls);
        Assert.Equal(1, harness.Gateway_.Count);

        var operation = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync());
        Assert.Equal(PapssDirection.Inbound, operation.Direction);
        Assert.Equal(PapssOperationType.VerificationEnquiry, operation.Operation);
        Assert.Equal("PAPSS-MSG-000001", operation.RequestMessageId);
        Assert.Equal("PAPSS-VID-000001", operation.VerificationId);
        Assert.Equal(PapssGatewayState.Admitted, operation.GatewayState);
        Assert.Equal(PapssOutcome.VerifiedMatch, operation.PapssOutcome);
        Assert.Equal(PapssDeliveryState.Delivered, operation.BankDeliveryState);
        Assert.Null(operation.DeadlineAt);

        // The reply honours the cross-repo contract.
        var reply = XDocument.Parse(harness.Gateway_.Submitted.Single());
        string Value(string name) => reply.Descendants().First(x => x.Name.LocalName == name).Value;
        var header = reply.Descendants().First(x => x.Name.LocalName == "AppHdr");
        Assert.Equal("acmt.024.001.03", header.Elements().First(x => x.Name.LocalName == "MsgDefIdr").Value);
        Assert.StartsWith("SIPS-", header.Elements().First(x => x.Name.LocalName == "BizMsgIdr").Value);
        Assert.Equal(PostgresHarness.LocalBic, header.Elements().First(x => x.Name.LocalName == "Fr").Descendants().First(x => x.Name.LocalName == "Id").Value);
        Assert.Equal(PostgresHarness.Gateway, header.Elements().First(x => x.Name.LocalName == "To").Descendants().First(x => x.Name.LocalName == "Id").Value);
        Assert.Equal("PAPSS-MSG-000001", header.Elements().First(x => x.Name.LocalName == "Rltd").Elements().First(x => x.Name.LocalName == "BizMsgIdr").Value);
        Assert.Equal("PAPSS-MSG-000001", reply.Descendants().First(x => x.Name.LocalName == "OrgnlAssgnmt").Elements().First(x => x.Name.LocalName == "MsgId").Value);
        Assert.Equal("PAPSS-VID-000001", Value("OrgnlId"));
        Assert.Equal("true", Value("Vrfctn"));
        Assert.Equal("MATCH", reply.Descendants().First(x => x.Name.LocalName == "Rsn").Elements().Single().Value);
        Assert.Equal("AMINA ALI", Value("Nm"));

        var lookup = await Lookup(harness, provider, "PAPSS-MSG-000001", operations: true);
        Assert.Equal("COMPLETED", lookup["status"]!.GetValue<string>());
        Assert.Equal("INBOUND", lookup["direction"]!.GetValue<string>());
        Assert.Equal("ADMITTED", lookup["replyState"]!.GetValue<string>());
    }

    [Fact]
    public async Task Concurrent_inbound_redelivery_calls_the_core_bank_once()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        harness.CoreBank.Delay = TimeSpan.FromMilliseconds(200);
        var enquiry = PostgresHarness.InboundEnquiry("PAPSS-MSG-000002", "PAPSS-VID-000002");
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => HandleEnquiry(provider, harness, enquiry)));
        Assert.Equal(1, harness.CoreBank.Calls);
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOutboundResponses.CountAsync()));
    }

    [Fact]
    public async Task Core_bank_failure_records_unknown_and_sends_no_reply()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        harness.CoreBank.Answer = false;
        await HandleEnquiry(provider, harness, PostgresHarness.InboundEnquiry("PAPSS-MSG-000003", "PAPSS-VID-000003"));
        await HandleEnquiry(provider, harness, PostgresHarness.InboundEnquiry("PAPSS-MSG-000003", "PAPSS-VID-000003"));
        await provider.GetRequiredService<PapssResponseOutboxWorker>().RunOnceAsync(CancellationToken.None);

        var operation = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync());
        Assert.Equal(PapssDeliveryState.Failed, operation.BankDeliveryState);
        Assert.Equal(PapssOutcome.Unknown, operation.PapssOutcome);
        Assert.Equal("CORE_BANK_TIMEOUT", operation.ReasonCode);
        Assert.Equal(0, await harness.WithStorageAsync(db => db.PapssOutboundResponses.CountAsync()));
        Assert.Equal(0, harness.Gateway_.Count);
        Assert.Equal(1, harness.CoreBank.Calls);
        Assert.Equal("FAILED", (await Lookup(harness, provider, "PAPSS-MSG-000003", operations: true))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Missing_core_bank_reason_is_not_replaced_by_a_default()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        harness.CoreBank.Verified = false;
        harness.CoreBank.BankReason = string.Empty;
        await HandleEnquiry(provider, harness, PostgresHarness.InboundEnquiry("PAPSS-MSG-000004", "PAPSS-VID-000004"));
        await provider.GetRequiredService<PapssResponseOutboxWorker>().RunOnceAsync(CancellationToken.None);

        var reply = XDocument.Parse(harness.Gateway_.Submitted.Single());
        Assert.Equal("false", reply.Descendants().First(x => x.Name.LocalName == "Vrfctn").Value);
        Assert.DoesNotContain(reply.Descendants(), x => x.Name.LocalName == "Rsn");
        var operation = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync());
        Assert.Equal(PapssOutcome.VerifiedNoMatch, operation.PapssOutcome);
        Assert.Null(operation.Reason);
    }

    [Fact]
    public async Task Reply_is_retried_byte_identically_after_a_gateway_failure()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        harness.Gateway_.OnSubmit = (call, _) => call == 1 ? throw new HttpRequestException("gateway down") : "EXACT_REPLAY";
        await HandleEnquiry(provider, harness, PostgresHarness.InboundEnquiry("PAPSS-MSG-000005", "PAPSS-VID-000005"));
        var worker = provider.GetRequiredService<PapssResponseOutboxWorker>();

        await worker.RunOnceAsync(CancellationToken.None);
        var retrying = await harness.WithStorageAsync(db => db.PapssOutboundResponses.AsNoTracking().SingleAsync());
        Assert.Equal(PapssResponseState.Pending, retrying.State);
        Assert.Equal(1, retrying.Attempts);
        Assert.Equal(PapssGatewayState.SubmissionUnknown, (await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync())).GatewayState);

        harness.Clock.Advance(TimeSpan.FromSeconds(6));
        await worker.RunOnceAsync(CancellationToken.None);
        var admitted = await harness.WithStorageAsync(db => db.PapssOutboundResponses.AsNoTracking().SingleAsync());
        Assert.Equal(PapssResponseState.Admitted, admitted.State);
        Assert.Equal("EXACT_REPLAY", admitted.AdmissionCode);
        Assert.Equal(2, harness.Gateway_.Count);
        Assert.Equal(harness.Gateway_.Submitted[0], harness.Gateway_.Submitted[1]);
        Assert.Equal(Encoding.UTF8.GetString(admitted.SignedXml), harness.Gateway_.Submitted[0]);
        var operation = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync());
        Assert.Equal(PapssGatewayState.Admitted, operation.GatewayState);
        Assert.Equal("EXACT_REPLAY", operation.AdmissionCode);
    }

    [Fact]
    public async Task Gateway_rejection_of_a_reply_is_terminal()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        harness.Gateway_.OnSubmit = (_, _) => throw new ParticipantRailException("DUPLICATE_CONFLICT", "conflict");
        await HandleEnquiry(provider, harness, PostgresHarness.InboundEnquiry("PAPSS-MSG-000006", "PAPSS-VID-000006"));
        var worker = provider.GetRequiredService<PapssResponseOutboxWorker>();
        await worker.RunOnceAsync(CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, harness.Gateway_.Count);
        var reply = await harness.WithStorageAsync(db => db.PapssOutboundResponses.AsNoTracking().SingleAsync());
        Assert.Equal(PapssResponseState.Rejected, reply.State);
        Assert.Equal("DUPLICATE_CONFLICT", reply.AdmissionCode);
        Assert.Equal("REJECTED", (await Lookup(harness, provider, "PAPSS-MSG-000006", operations: true))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Pending_push_and_reply_complete_exactly_once_after_a_restart()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        string requestMessageId;
        // Process 1 stores work, then "crashes" before any worker runs.
        await using (var first = harness.BuildProvider())
        {
            requestMessageId = (await Verify(harness, first))["requestMessageId"]!.GetValue<string>();
            await HandleResult(first, harness, PostgresHarness.VerificationResult(requestMessageId, verified: true));
            await HandleEnquiry(first, harness, PostgresHarness.InboundEnquiry("PAPSS-MSG-000007", "PAPSS-VID-000007"));
        }
        var submittedBeforeRestart = harness.Gateway_.Count; // the outbound Verify only

        // Process 2: brand-new container, contexts and worker instances.
        await using var second = harness.BuildProvider();
        var push = second.GetRequiredService<PapssBankPushWorker>();
        var replies = second.GetRequiredService<PapssResponseOutboxWorker>();
        await Task.WhenAll(push.RunOnceAsync(CancellationToken.None), replies.RunOnceAsync(CancellationToken.None));
        await Task.WhenAll(push.RunOnceAsync(CancellationToken.None), replies.RunOnceAsync(CancellationToken.None));
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await Task.WhenAll(push.RunOnceAsync(CancellationToken.None), replies.RunOnceAsync(CancellationToken.None));

        Assert.Equal(1, harness.Bank.Count);
        Assert.Equal(submittedBeforeRestart + 1, harness.Gateway_.Count);
        Assert.Equal(PapssDeliveryState.Delivered, (await harness.WithStorageAsync(db => db.PapssOperationEvents.AsNoTracking().SingleAsync(x => x.EventType == PapssEventTypes.VerificationResult))).PushState);
        Assert.Equal(PapssResponseState.Admitted, (await harness.WithStorageAsync(db => db.PapssOutboundResponses.AsNoTracking().SingleAsync())).State);
    }

    [Fact]
    public async Task Two_worker_instances_do_not_double_deliver()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        for (var i = 0; i < 8; i++)
            await HandleResult(provider, harness, PostgresHarness.VerificationResult($"SIPS-UNMATCHED-{i:D12}", verified: true));
        await using var other = harness.BuildProvider();
        await Task.WhenAll(
            provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None),
            other.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None));
        Assert.Equal(8, harness.Bank.Count);
        Assert.Equal(8, harness.Bank.Calls.Select(x => x.Body["requestMessageId"]!.GetValue<string>()).Distinct().Count());
    }

    [Fact]
    public async Task Deadline_is_only_computed_when_configured()
    {
        await using (var unset = await PostgresHarness.CreateAsync())
        await using (var provider = unset.BuildProvider())
        {
            await HandleEnquiry(provider, unset, PostgresHarness.InboundEnquiry("PAPSS-MSG-000008", "PAPSS-VID-000008"));
            Assert.Null((await unset.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync())).DeadlineAt);
        }

        await using var received = await PostgresHarness.CreateAsync(o =>
        {
            o.Inbound.Acmt023.ResponseDeadlineSeconds = 30;
            o.Inbound.Acmt023.DeadlineClock = PapssDeadlineClock.ReceivedAt;
        });
        await using (var provider = received.BuildProvider())
        {
            await HandleEnquiry(provider, received, PostgresHarness.InboundEnquiry("PAPSS-MSG-000009", "PAPSS-VID-000009"));
            var operation = await received.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync());
            Assert.Equal(received.Clock.GetUtcNow().AddSeconds(30), operation.DeadlineAt);
            Assert.NotNull((await Lookup(received, provider, "PAPSS-MSG-000009", operations: true))["deadlineAt"]);
        }

        var created = new DateTime(2026, 9, 25, 7, 59, 50, DateTimeKind.Utc);
        await using var source = await PostgresHarness.CreateAsync(o =>
        {
            o.Inbound.Acmt023.ResponseDeadlineSeconds = 20;
            o.Inbound.Acmt023.DeadlineClock = PapssDeadlineClock.SourceCreationTime;
            o.Inbound.LateResponsePolicy = PapssLateResponsePolicy.Hold;
        });
        await using (var provider = source.BuildProvider())
        {
            await HandleEnquiry(provider, source, PostgresHarness.InboundEnquiry("PAPSS-MSG-000010", "PAPSS-VID-000010", created));
            var operation = await source.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync());
            Assert.Equal(new DateTimeOffset(created).AddSeconds(20), operation.DeadlineAt);

            // Hold: a reply that is past its recorded deadline is not submitted.
            source.Clock.Advance(TimeSpan.FromSeconds(30));
            await provider.GetRequiredService<PapssResponseOutboxWorker>().RunOnceAsync(CancellationToken.None);
            Assert.Equal(0, source.Gateway_.Count);
            Assert.Equal(PapssResponseState.Held, (await source.WithStorageAsync(db => db.PapssOutboundResponses.AsNoTracking().SingleAsync())).State);
        }
    }

    [Fact]
    public async Task Retention_purges_only_settled_rows_when_configured()
    {
        await using var harness = await PostgresHarness.CreateAsync(o => o.Store.RetentionDays = 7);
        await using var provider = harness.BuildProvider();
        await HandleEnquiry(provider, harness, PostgresHarness.InboundEnquiry("PAPSS-MSG-000011", "PAPSS-VID-000011"));
        await provider.GetRequiredService<PapssResponseOutboxWorker>().RunOnceAsync(CancellationToken.None);
        var requestMessageId = (await Verify(harness, provider))["requestMessageId"]!.GetValue<string>(); // pending, never completed

        var retention = provider.GetRequiredService<PapssStoreRetentionWorker>();
        Assert.Equal((0, 0), await retention.PurgeAsync(CancellationToken.None));
        harness.Clock.Advance(TimeSpan.FromDays(8));
        var purged = await retention.PurgeAsync(CancellationToken.None);
        Assert.Equal(1, purged.Operations);
        Assert.Equal(1, purged.Events);
        var remaining = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync());
        Assert.Equal(requestMessageId, remaining.RequestMessageId);
        Assert.Equal(0, await harness.WithStorageAsync(db => db.PapssOutboundResponses.CountAsync()));
    }

    [Fact]
    public async Task Lookup_returns_404_for_unknown_ids_and_reports_expiry_when_configured()
    {
        await using var harness = await PostgresHarness.CreateAsync(o => o.Outbound.VerificationResultExpirySeconds = 60);
        await using var provider = harness.BuildProvider();
        var controller = Controller(harness, provider.CreateScope().ServiceProvider);
        Assert.IsType<NotFoundObjectResult>(await controller.GetVerification("SIPS-NOPE", null, CancellationToken.None));

        var requestMessageId = (await Verify(harness, provider))["requestMessageId"]!.GetValue<string>();
        Assert.Equal("PENDING", (await Lookup(harness, provider, requestMessageId))["status"]!.GetValue<string>());
        harness.Clock.Advance(TimeSpan.FromSeconds(61));
        var expired = await Lookup(harness, provider, requestMessageId);
        Assert.Equal("EXPIRED", expired["status"]!.GetValue<string>());
        Assert.Equal(61, expired["ageSeconds"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------------------------------------

    private static int StatusCodes503(IActionResult result) => result is ObjectResult { StatusCode: { } code } ? code : 0;

    private static GatewayController Controller(PostgresHarness harness, IServiceProvider services)
    {
        var router = new Mock<IParticipantOperationRouter>();
        router.Setup(x => x.Select(It.IsAny<ParticipantOperation>(), It.IsAny<string?>())).Returns(DownstreamRail.Papss);
        router.Setup(x => x.ResolvePapss()).Returns(harness.Binding());
        return new GatewayController(
            services.GetRequiredService<IJsonAdapter>(),
            Mock.Of<IOutgoingVerificationHandler>(), Mock.Of<IOutgoingTransactionHandler>(), Mock.Of<IOutgoingTransactionStatusHandler>(),
            Mock.Of<IOutgoingReturnTransactionHandler>(), Mock.Of<IReturnRetryHandler>(), Microsoft.Extensions.Options.Options.Create(new CoreOptions()),
            router.Object, services.GetRequiredService<IPapssFacingSipsClient>(), services.GetRequiredService<PapssOperationStore>(),
            harness.Options, harness.Clock);
    }

    private static async Task<ActionResult> VerifyRaw(PostgresHarness harness, ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var body = new JsonObject { ["rail"] = "PAPSS", ["accNo"] = "0012030321735", ["accType"] = "BBAN", ["agent"] = PostgresHarness.ForeignBic };
        return await Controller(harness, scope.ServiceProvider).VerifyPayee(body, CancellationToken.None);
    }

    private static async Task<JsonObject> Verify(PostgresHarness harness, ServiceProvider provider)
        => (JsonObject)Assert.IsType<OkObjectResult>(await VerifyRaw(harness, provider)).Value!;

    private static async Task<JsonObject> Lookup(PostgresHarness harness, ServiceProvider provider, string requestMessageId, bool operations = false)
    {
        using var scope = provider.CreateScope();
        var controller = Controller(harness, scope.ServiceProvider);
        var result = operations
            ? await controller.GetOperation(requestMessageId, null, CancellationToken.None)
            : await controller.GetVerification(requestMessageId, null, CancellationToken.None);
        return (JsonObject)Assert.IsType<OkObjectResult>(result).Value!;
    }

    private static async Task<string> HandleResult(ServiceProvider provider, PostgresHarness harness, string report)
    {
        using var scope = provider.CreateScope();
        using var mapping = scope.ServiceProvider.GetRequiredService<IParticipantCallbackContext>().Push(harness.Binding());
        return await scope.ServiceProvider.GetRequiredService<IIncomingVerificationResponseHandler>().HandleAsync(report, CancellationToken.None);
    }

    private static async Task HandleEnquiry(ServiceProvider provider, PostgresHarness harness, string enquiry)
    {
        using var scope = provider.CreateScope();
        using var mapping = scope.ServiceProvider.GetRequiredService<IParticipantCallbackContext>().Push(harness.Binding());
        await scope.ServiceProvider.GetRequiredService<IPapssInboundVerificationService>().HandleAsync(harness.Binding(), enquiry, CancellationToken.None);
    }
}
