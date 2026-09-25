using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
using SIPS.PostgreSQL.Models;
using Xunit;

namespace SIPS.Connect.PostgresTests;

/// <summary>PAPSS payments, returns and status enquiries against a real PostgreSQL (Phase 2).</summary>
[Trait("Category", "Postgres")]
public sealed class PapssPaymentOperationTests
{
    [Fact]
    public async Task Migration_adds_the_payment_columns_and_unique_keys()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var connection = new NpgsqlConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await using var history = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE migrationid LIKE '%_AddPapssPaymentOperations'", connection);
        Assert.Equal(1L, (long)(await history.ExecuteScalarAsync())!);
        await using var unique = new NpgsqlCommand("SELECT count(*) FROM pg_indexes WHERE indexname IN ('ux_papss_op_payment_txid','ux_papss_op_return_id') AND indexdef LIKE 'CREATE UNIQUE INDEX%'", connection);
        Assert.Equal(2L, (long)(await unique.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Payment_is_persisted_before_submission_and_follows_the_admission()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        string? storedAtSubmit = null;
        harness.Gateway_.OnSubmit = (_, xml) =>
        {
            var id = PostgresHarness.Header(xml.Replace("<!--signed-->", string.Empty), "BizMsgIdr");
            storedAtSubmit = Task.Run(() => harness.WithStorageAsync(async db =>
            {
                var row = await db.PapssOperations.AsNoTracking().SingleAsync(x => x.RequestMessageId == id);
                return $"{row.Operation}|{row.GatewayState}|{row.TxId}|{Encoding.UTF8.GetString(row.SignedRequest!) == xml}";
            })).GetAwaiter().GetResult();
            return "RECEIVED_AND_DURABLY_ADMITTED";
        };

        var response = await Pay(harness, provider, Payment("TX-P1"));
        Assert.Equal("RECEIVED_AND_DURABLY_ADMITTED", response["code"]!.GetValue<string>());
        Assert.Equal("Payment|Submitting|TX-P1|True", storedAtSubmit);

        var row = await Stored(harness, "TX-P1");
        Assert.Equal(PapssGatewayState.Admitted, row.GatewayState);
        Assert.Equal(PapssOutcome.Pending, row.PapssOutcome);
        Assert.Equal(response["requestMessageId"]!.GetValue<string>(), row.RequestMessageId);
        Assert.NotNull(row.MsgId);
        Assert.NotEqual(row.RequestMessageId, row.MsgId);
        Assert.Equal(10m, row.Amount);
        Assert.Equal("USD", row.Currency);
        Assert.Equal("USDP", row.LocalInstrument);

        var lookup = await Lookup(harness, provider, c => c.GetPayment("TX-P1", null, CancellationToken.None));
        Assert.Equal("PENDING", lookup["status"]!.GetValue<string>());
        Assert.Equal("PAYMENT", lookup["operation"]!.GetValue<string>());
        Assert.Equal("PENDING", lookup["paymentOutcome"]!.GetValue<string>());
        Assert.Equal("E2E-TX-P1", lookup["endToEndId"]!.GetValue<string>());
        Assert.Equal(10m, lookup["amount"]!.GetValue<decimal>());
        Assert.Empty(lookup["statusHistory"]!.AsArray());
    }

    [Fact]
    public async Task Same_txid_replays_same_content_and_conflicts_on_different_content_without_calling_the_gateway()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();

        // First attempt is ambiguous (transport error): stored as SUBMISSION_UNKNOWN.
        harness.Gateway_.OnSubmit = (call, _) => call == 1 ? throw new HttpRequestException("connection reset") : "EXACT_REPLAY";
        var ambiguous = await PayRaw(harness, provider, Payment("TX-P2"));
        Assert.Equal(503, Assert.IsType<ObjectResult>(ambiguous).StatusCode);
        Assert.Equal(PapssGatewayState.SubmissionUnknown, (await Stored(harness, "TX-P2")).GatewayState);

        // Same TxId + same content: the stored signed bytes are re-submitted unchanged (no new preparation).
        var replay = await Pay(harness, provider, Payment("TX-P2"));
        Assert.Equal("EXACT_REPLAY", replay["code"]!.GetValue<string>());
        Assert.Equal(2, harness.Gateway_.Count);
        Assert.Equal(harness.Gateway_.Submitted[0], harness.Gateway_.Submitted[1]);
        Assert.Equal(1, harness.Gateway_.Prepared);
        Assert.Equal(PapssGatewayState.Admitted, (await Stored(harness, "TX-P2")).GatewayState);

        // Admitted: replayed from the store, the gateway is not called again.
        var again = await Pay(harness, provider, Payment("TX-P2"));
        Assert.Equal("EXACT_REPLAY", again["code"]!.GetValue<string>());
        Assert.Equal(replay["requestMessageId"]!.GetValue<string>(), again["requestMessageId"]!.GetValue<string>());
        Assert.Equal(2, harness.Gateway_.Count);

        // Same TxId, different content: DUPLICATE_CONFLICT locally.
        var different = Payment("TX-P2"); different["amount"] = 11;
        var conflict = Assert.IsType<BadRequestObjectResult>(await PayRaw(harness, provider, different));
        Assert.Contains("DUPLICATE_CONFLICT", System.Text.Json.JsonSerializer.Serialize(conflict.Value));
        Assert.Equal(2, harness.Gateway_.Count);
        Assert.Equal(1, harness.Gateway_.Prepared);
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperations.CountAsync(x => x.TxId == "TX-P2")));
    }

    [Fact]
    public async Task Acsp_then_acsc_settles_the_payment_and_each_status_is_pushed_once()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await Pay(harness, provider, Payment("TX-P3"));
        var payment = await Stored(harness, "TX-P3");

        var acsp = await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-31", payment.MsgId!, "pacs.008.001.10", "TX-P3", "E2E-TX-P3", "ACSP"));
        Assert.Equal((PapssCorrelation.MessageId, PapssEventDisposition.Applied), (acsp.Correlation, acsp.Disposition));
        var accepted = await Stored(harness, "TX-P3");
        Assert.Equal((PapssOutcome.Accepted, "ACSP", PapssDeliveryState.Pending), (accepted.PapssOutcome, accepted.PaymentStatus, accepted.BankDeliveryState));

        // A late ACCP must not move the payment back.
        var late = await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-32", payment.MsgId!, "pacs.008.001.10", "TX-P3", "E2E-TX-P3", "ACCP"));
        Assert.Equal(PapssEventDisposition.NotAdvancing, late.Disposition);
        Assert.Equal("ACSP", (await Stored(harness, "TX-P3")).PaymentStatus);

        await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-33", payment.MsgId!, "pacs.008.001.10", "TX-P3", "E2E-TX-P3", "ACSC"));
        var settled = await Stored(harness, "TX-P3");
        Assert.Equal((PapssOutcome.Settled, "ACSC"), (settled.PapssOutcome, settled.PaymentStatus));
        Assert.NotNull(settled.CompletedAt);

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, harness.Bank.Count);
        Assert.All(harness.Bank.Calls, c => Assert.Equal(PostgresHarness.CallbackUrl, c.Url));
        Assert.Equal(["ACSP", "ACSC"], harness.Bank.Calls.Select(c => c.Body["status"]!.GetValue<string>()));
        Assert.Equal(["TX-P3:ACSP", "TX-P3:ACSC"], harness.Bank.Calls.Select(c => c.Headers!["X-Idempotency-Key"]));
        Assert.All(harness.Bank.Calls, c => Assert.Equal("TX-P3", c.Body["txId"]!.GetValue<string>()));
        Assert.Equal(PapssDeliveryState.Delivered, (await Stored(harness, "TX-P3")).BankDeliveryState);

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, harness.Bank.Count);

        var lookup = await Lookup(harness, provider, c => c.GetPayment("TX-P3", null, CancellationToken.None));
        Assert.Equal("COMPLETED", lookup["status"]!.GetValue<string>());
        Assert.Equal("SETTLED", lookup["paymentOutcome"]!.GetValue<string>());
        Assert.Equal("ACSC", lookup["paymentStatus"]!.GetValue<string>());
        var history = lookup["statusHistory"]!.AsArray();
        Assert.Equal(["ACSP", "ACCP", "ACSC"], history.Select(h => h!["status"]!.GetValue<string>()));
        Assert.Equal(["APPLIED", "NOT_ADVANCING", "APPLIED"], history.Select(h => h!["disposition"]!.GetValue<string>()));
        Assert.Equal(["DELIVERED", "NOT_REQUIRED", "DELIVERED"], history.Select(h => h!["pushState"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Rjct_rejects_the_payment_with_its_reason()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await Pay(harness, provider, Payment("TX-P4"));
        var payment = await Stored(harness, "TX-P4");

        await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-41", payment.MsgId!, "pacs.008.001.10", "TX-P4", "E2E-TX-P4", "RJCT", reason: "AM04"));
        var rejected = await Stored(harness, "TX-P4");
        Assert.Equal((PapssOutcome.Rejected, "RJCT", "AM04"), (rejected.PapssOutcome, rejected.PaymentStatus, rejected.StatusReasonCode));
        Assert.Equal(PapssGatewayState.Admitted, rejected.GatewayState);

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        var call = Assert.Single(harness.Bank.Calls);
        Assert.Equal("RJCT", call.Body["status"]!.GetValue<string>());
        Assert.Equal("AM04", call.Body["reason"]!.GetValue<string>());
        Assert.Equal("REJECTED", (await Lookup(harness, provider, c => c.GetPayment("TX-P4", null, CancellationToken.None)))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Conflicting_second_final_status_is_flagged_and_not_applied()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await Pay(harness, provider, Payment("TX-P5"));
        var payment = await Stored(harness, "TX-P5");

        await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-51", payment.MsgId!, "pacs.008.001.10", "TX-P5", "E2E-TX-P5", "ACSC"));
        var conflict = await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-52", payment.MsgId!, "pacs.008.001.10", "TX-P5", "E2E-TX-P5", "RJCT", reason: "AB05"));
        Assert.Equal(PapssEventDisposition.Conflict, conflict.Disposition);
        var duplicateFinal = await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-53", payment.MsgId!, "pacs.008.001.10", "TX-P5", "E2E-TX-P5", "ACSC"));
        Assert.Equal(PapssEventDisposition.DuplicateFinal, duplicateFinal.Disposition);

        var stored = await Stored(harness, "TX-P5");
        Assert.Equal((PapssOutcome.Settled, "ACSC", true), (stored.PapssOutcome, stored.PaymentStatus, stored.StatusConflict));
        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Equal("ACSC", Assert.Single(harness.Bank.Calls).Body["status"]!.GetValue<string>());

        var lookup = await Lookup(harness, provider, c => c.GetPayment("TX-P5", null, CancellationToken.None));
        Assert.True(lookup["statusConflict"]!.GetValue<bool>());
        var unresolved = await Unresolved(harness, provider);
        var flagged = Assert.Single(unresolved);
        Assert.Equal(("PAPSS-S-52", "CONFLICT", true), (flagged["sourceMessageId"]!.GetValue<string>(), flagged["disposition"]!.GetValue<string>(), flagged["attached"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task Duplicate_pacs002_with_the_same_source_id_is_stored_once_and_pushed_once()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await Pay(harness, provider, Payment("TX-P6"));
        var payment = await Stored(harness, "TX-P6");
        var report = PostgresHarness.StatusReport("PAPSS-S-61", payment.MsgId!, "pacs.008.001.10", "TX-P6", "E2E-TX-P6", "ACSC");

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Status(provider, harness, report))));
        Assert.Single(results, r => !r.Duplicate);
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.SourceMessageId == "PAPSS-S-61")));
        Assert.True((await Status(provider, harness, report)).Duplicate);

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, harness.Bank.Count);
    }

    [Fact]
    public async Task Uncorrelated_or_mismatching_pacs002_is_stored_for_operators_not_rejected_as_an_orphan()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await Pay(harness, provider, Payment("TX-P7"));
        var payment = await Stored(harness, "TX-P7");

        var unknown = await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-71", "NO-SUCH-MSG", "pacs.008.001.10", "TX-NOPE", "E2E-NOPE", "ACSC"));
        Assert.Equal((PapssCorrelation.None, PapssEventDisposition.Uncorrelated, false), (unknown.Correlation, unknown.Disposition, unknown.Pushed));

        // The message id is ours but the transaction is not: never attached.
        var mismatch = await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-72", payment.MsgId!, "pacs.008.001.10", "TX-OTHER", "E2E-TX-P7", "RJCT"));
        Assert.Equal((PapssCorrelation.Mismatch, PapssEventDisposition.Uncorrelated), (mismatch.Correlation, mismatch.Disposition));
        Assert.Equal(PapssOutcome.Pending, (await Stored(harness, "TX-P7")).PapssOutcome);

        var events = await harness.WithStorageAsync(db => db.PapssOperationEvents.AsNoTracking().Where(x => x.EventType == PapssEventTypes.PaymentStatus).ToListAsync());
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Null(e.OperationId));
        Assert.All(events, e => Assert.Equal(PapssDeliveryState.NotRequired, e.PushState));
        Assert.Equal(0, await harness.WithStorageAsync(db => db.ISOMessages.CountAsync()));
        Assert.Equal(2, (await Unresolved(harness, provider)).Count);

        // Secondary key: without a message-id match, OrgnlTxId + OrgnlEndToEndId correlate.
        var byTx = await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-73", "GATEWAY-WIRE-ID", "pacs.008.001.10", "TX-P7", "E2E-TX-P7", "ACSC"));
        Assert.Equal((PapssCorrelation.TransactionId, PapssEventDisposition.Applied), (byTx.Correlation, byTx.Disposition));
        Assert.Equal(PapssOutcome.Settled, (await Stored(harness, "TX-P7")).PapssOutcome);
    }

    [Fact]
    public async Task Return_status_correlates_to_the_return_and_a_settled_return_marks_the_received_payment_returned()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        // A payment we received (the common case for returning).
        var received = await RecordInbound(provider, harness, "CT02-IN-81", "TX-IN8", "E2E-IN8", "ACCP");
        Assert.Equal(PapssOutcome.Accepted, received.PapssOutcome);

        var admission = await Return(harness, provider, ReturnBody("RTN-8", "TX-IN8", "E2E-IN8"));
        Assert.Equal("RECEIVED_AND_DURABLY_ADMITTED", admission["code"]!.GetValue<string>());
        var ret = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.ReturnId == "RTN-8"));
        Assert.Equal((PapssDirection.Outbound, PapssOperationType.Return, received.Id, PapssGatewayState.Admitted), (ret.Direction, ret.Operation, ret.OriginalOperationId!.Value, ret.GatewayState));

        // ACSP on the return does not settle it by default (ACSC vs ACSP is contradicted; default ACSC).
        var acsp = await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-81", ret.MsgId!, "pacs.004.001.11", "TX-IN8", "E2E-IN8", "ACSP"));
        Assert.Equal(ret.Id, acsp.Operation!.Id);
        Assert.Equal(PapssOutcome.Accepted, (await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == ret.Id))).PapssOutcome);
        Assert.Equal(PapssOutcome.Accepted, (await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == received.Id))).PapssOutcome);

        // The return's pacs.002 carries the ORIGINAL payment TxId: it still correlates to the return, not the payment.
        var acsc = await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-82", ret.MsgId!, "pacs.004.001.11", "TX-IN8", "E2E-IN8", "ACSC"));
        Assert.Equal((ret.Id, PapssEventDisposition.Applied), (acsc.Operation!.Id, acsc.Disposition));
        Assert.Equal(PapssOutcome.Settled, (await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == ret.Id))).PapssOutcome);
        var payment = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == received.Id));
        Assert.Equal(PapssOutcome.Returned, payment.PapssOutcome);
        Assert.Equal(2, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.OperationId == ret.Id)));

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, harness.Bank.Count);
        var last = harness.Bank.Calls[^1];
        Assert.Equal(("RTN-8", "RTN-8:ACSC", "TX-IN8"), (last.Headers!["X-Return-Id"], last.Headers["X-Idempotency-Key"], last.Body["txId"]!.GetValue<string>()));

        var lookup = await Lookup(harness, provider, c => c.GetReturnOperation("RTN-8", null, CancellationToken.None));
        Assert.Equal(("RETURN", "SETTLED", "TX-IN8", received.RequestMessageId), (lookup["operation"]!.GetValue<string>(), lookup["paymentOutcome"]!.GetValue<string>(), lookup["originalTxId"]!.GetValue<string>(), lookup["originalRequestMessageId"]!.GetValue<string>()));
        var paymentLookup = await Lookup(harness, provider, c => c.GetPayment("TX-IN8", null, CancellationToken.None));
        Assert.Equal("RETURNED", paymentLookup["paymentOutcome"]!.GetValue<string>());
        Assert.Equal("OUTBOUND RTN-8 SETTLED", Assert.Single(paymentLookup["returns"]!.AsArray())!.GetValue<string>());
    }

    [Fact]
    public async Task Return_settlement_status_follows_configuration()
    {
        await using var harness = await PostgresHarness.CreateAsync(o => o.Returns.SettledStatuses = "ACSC,ACSP");
        await using var provider = harness.BuildProvider();
        await Pay(harness, provider, Payment("TX-P9"));
        await Return(harness, provider, ReturnBody("RTN-9", "TX-P9", "E2E-TX-P9"));
        var ret = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.ReturnId == "RTN-9"));
        await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-91", ret.MsgId!, "pacs.004.001.11", "TX-P9", "E2E-TX-P9", "ACSP"));
        Assert.Equal(PapssOutcome.Returned, (await Stored(harness, "TX-P9")).PapssOutcome);
    }

    [Fact]
    public async Task Return_id_is_idempotent()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var first = await Return(harness, provider, ReturnBody("RTN-10", "TX-X", "E2E-X"));
        var again = await Return(harness, provider, ReturnBody("RTN-10", "TX-X", "E2E-X"));
        Assert.Equal(first["requestMessageId"]!.GetValue<string>(), again["requestMessageId"]!.GetValue<string>());
        var different = ReturnBody("RTN-10", "TX-X", "E2E-X"); different["reason"] = "AC03";
        Assert.IsType<BadRequestObjectResult>(await ReturnRaw(harness, provider, different));
        Assert.Equal(1, harness.Gateway_.Count);
        // Not in the store: kept without a link.
        Assert.Null((await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.ReturnId == "RTN-10"))).OriginalOperationId);
    }

    [Fact]
    public async Task Inbound_pacs008_is_recorded_with_the_bank_decision_and_the_decision_outbox_state()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var operation = await RecordInbound(provider, harness, "CT02-IN-11", "TX-IN11", "E2E-IN11", "ACCP");
        Assert.Equal((PapssDirection.Inbound, PapssOperationType.Payment, PapssOutcome.Accepted, "ACCP", PapssDeliveryState.Delivered, PapssGatewayState.Submitting),
            (operation.Direction, operation.Operation, operation.PapssOutcome, operation.PaymentStatus, operation.BankDeliveryState, operation.GatewayState));
        Assert.NotNull(operation.IsoMessageId);
        var decision = await Lookup(harness, provider, c => c.GetPayment("TX-IN11", null, CancellationToken.None));
        Assert.Equal(("INBOUND", "PENDING"), (decision["direction"]!.GetValue<string>(), decision["decisionState"]!.GetValue<string>()));

        // Redelivery: still one operation, one event.
        await RecordInbound(provider, harness, "CT02-IN-11", "TX-IN11", "E2E-IN11", "ACCP", insertIso: false);
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperations.CountAsync(x => x.TxId == "TX-IN11")));
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.PaymentReceived)));

        // The decision outbox publishes the pacs.002: mirrored into the operation.
        await harness.WithStorageAsync(db => db.ISOMessages.Where(x => x.Id == operation.IsoMessageId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.PapssDecisionPublishedAt, DateTimeOffset.UtcNow).SetProperty(x => x.PapssDecisionAdmissionCode, "RECEIVED_AND_DURABLY_ADMITTED")));
        var published = await Lookup(harness, provider, c => c.GetPayment("TX-IN11", null, CancellationToken.None));
        Assert.Equal(("PUBLISHED", "ADMITTED", "RECEIVED_AND_DURABLY_ADMITTED"), (published["decisionState"]!.GetValue<string>(), published["gatewayState"]!.GetValue<string>(), published["admissionCode"]!.GetValue<string>()));

        // PAPSS's later final status for the received payment settles it.
        var final = await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-111", "CT02-IN-11", "pacs.008.001.10", "TX-IN11", "E2E-IN11", "ACSC"));
        Assert.Equal(PapssEventDisposition.Applied, final.Disposition);
        Assert.Equal(PapssOutcome.Settled, (await Stored(harness, "TX-IN11", PapssDirection.Inbound)).PapssOutcome);
        // Re-mirroring the decision later must neither undo the PAPSS status nor mark its pending push delivered.
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IPapssPaymentCallbackService>().SyncInboundPaymentAsync(PostgresHarness.InboundPayment("CT02-IN-11", "TX-IN11", "E2E-IN11"), CancellationToken.None);
        var afterSync = await Stored(harness, "TX-IN11", PapssDirection.Inbound);
        Assert.Equal((PapssOutcome.Settled, "ACSC", PapssDeliveryState.Pending), (afterSync.PapssOutcome, afterSync.PaymentStatus, afterSync.BankDeliveryState));
        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(PapssDeliveryState.Delivered, (await Stored(harness, "TX-IN11", PapssDirection.Inbound)).BankDeliveryState);

        // A rejected decision is final.
        var rejected = await RecordInbound(provider, harness, "CT02-IN-12", "TX-IN12", "E2E-IN12", "RJCT", reason: "AC04");
        Assert.Equal((PapssOutcome.Rejected, "RJCT", "AC04"), (rejected.PapssOutcome, rejected.PaymentStatus, rejected.StatusReasonCode));
    }

    [Fact]
    public async Task Inbound_pacs004_marks_our_payment_returned_and_is_pushed_once()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await Pay(harness, provider, Payment("TX-P12"));
        var payment = await Stored(harness, "TX-P12");
        await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-121", payment.MsgId!, "pacs.008.001.10", "TX-P12", "E2E-TX-P12", "ACSC"));

        var pacs004 = PostgresHarness.InboundReturn("CT02-RTN-121", "RTN-IN-12", "TX-P12", "E2E-TX-P12");
        var result = await InboundReturn(provider, harness, pacs004);
        Assert.Equal((PapssEventDisposition.Applied, PapssCorrelation.TransactionId), (result.Disposition, result.Correlation));
        Assert.Equal(PapssOutcome.Returned, (await Stored(harness, "TX-P12")).PapssOutcome);
        var ret = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.ReturnId == "RTN-IN-12"));
        Assert.Equal((PapssDirection.Inbound, payment.Id, PapssOutcome.Settled, "FOCR"), (ret.Direction, ret.OriginalOperationId!.Value, ret.PapssOutcome, ret.StatusReasonCode));

        Assert.True((await InboundReturn(provider, harness, pacs004)).Duplicate);
        // The same RtrId under a new source id is audit only.
        Assert.True((await InboundReturn(provider, harness, PostgresHarness.InboundReturn("CT02-RTN-122", "RTN-IN-12", "TX-P12", "E2E-TX-P12"))).Duplicate);

        await provider.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        var returnCalls = harness.Bank.Calls.Where(c => c.Body.ContainsKey("returnId")).ToList();
        var call = Assert.Single(returnCalls);
        Assert.Equal(("RTN-IN-12", "TX-P12", "E2E-TX-P12", "FOCR"), (call.Body["returnId"]!.GetValue<string>(), call.Body["txId"]!.GetValue<string>(), call.Body["endToEndId"]!.GetValue<string>(), call.Body["reason"]!.GetValue<string>()));
        Assert.Equal("RTN-IN-12", call.Headers!["X-Idempotency-Key"]);

        // A return for a payment we do not know is still stored and pushed (the funds come back).
        var unknown = await InboundReturn(provider, harness, PostgresHarness.InboundReturn("CT02-RTN-123", "RTN-IN-13", "TX-UNKNOWN", "E2E-UNKNOWN"));
        Assert.Equal((PapssEventDisposition.Uncorrelated, true), (unknown.Disposition, unknown.Pushed));
    }

    [Fact]
    public async Task Status_returns_the_stored_final_state_without_a_pacs028_and_enquires_only_while_non_final()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        await Pay(harness, provider, Payment("TX-P13"));
        var payment = await Stored(harness, "TX-P13");
        Assert.Equal(1, harness.Gateway_.Count);

        // Non-final: one pacs.028, recorded as a STATUS_ENQUIRY linked to the payment.
        var pending = await StatusQuery(harness, provider, "TX-P13");
        Assert.Equal("PENDING", pending["paymentOutcome"]!.GetValue<string>());
        Assert.Equal("TX-P13", pending["transactionId"]!.GetValue<string>());
        Assert.Equal("RECEIVED_AND_DURABLY_ADMITTED", pending["statusEnquiry"]!["code"]!.GetValue<string>());
        Assert.Equal(2, harness.Gateway_.Count);
        var enquiry = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Operation == PapssOperationType.StatusEnquiry));
        Assert.Equal((payment.Id, PapssGatewayState.Admitted, "TX-P13"), (enquiry.OriginalOperationId!.Value, enquiry.GatewayState, enquiry.OriginalTxId));

        // The answer arrives (referencing the payment): the enquiry is completed too.
        await Status(provider, harness, PostgresHarness.StatusReport("PAPSS-S-131", payment.MsgId!, "pacs.008.001.10", "TX-P13", "E2E-TX-P13", "ACSC"));
        Assert.NotNull((await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == enquiry.Id))).CompletedAt);

        // Final: answered from the store, no pacs.028.
        var final = await StatusQuery(harness, provider, "TX-P13");
        Assert.Equal(("ACSC", "SETTLED", "E2E-TX-P13"), (final["status"]!.GetValue<string>(), final["paymentOutcome"]!.GetValue<string>(), final["localId"]!.GetValue<string>()));
        Assert.Null(final["statusEnquiry"]);
        Assert.Equal("COMPLETED", final["operation"]!["status"]!.GetValue<string>());
        Assert.Equal(2, harness.Gateway_.Count);
    }

    [Fact]
    public async Task Status_minimum_age_holds_back_young_payments_when_configured()
    {
        await using var harness = await PostgresHarness.CreateAsync(o => o.Status.EnquiryMinimumAgeSeconds = 60);
        await using var provider = harness.BuildProvider();
        await Pay(harness, provider, Payment("TX-P14"));
        var young = await StatusQuery(harness, provider, "TX-P14");
        Assert.Null(young["statusEnquiry"]);
        Assert.Equal(1, harness.Gateway_.Count);
        harness.Clock.Advance(TimeSpan.FromSeconds(61));
        var old = await StatusQuery(harness, provider, "TX-P14");
        Assert.NotNull(old["statusEnquiry"]);
        Assert.Equal(2, harness.Gateway_.Count);

        // A TxId the store does not know keeps the previous behaviour (pacs.028 + admission response).
        var unknown = await StatusQuery(harness, provider, "TX-NOT-STORED", "E2E-NOT-STORED");
        Assert.Equal("RECEIVED_AND_DURABLY_ADMITTED", unknown["code"]!.GetValue<string>());
        Assert.Equal(3, harness.Gateway_.Count);
    }

    [Fact]
    public async Task Pending_payment_pushes_complete_exactly_once_after_a_restart()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        string msgId;
        await using (var first = harness.BuildProvider())
        {
            await Pay(harness, first, Payment("TX-P15"));
            msgId = (await Stored(harness, "TX-P15")).MsgId!;
            harness.Bank.Status = System.Net.HttpStatusCode.ServiceUnavailable;
            await Status(first, harness, PostgresHarness.StatusReport("PAPSS-S-151", msgId, "pacs.008.001.10", "TX-P15", "E2E-TX-P15", "ACSC"));
            await first.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, harness.Bank.Count); // failed attempt, retry scheduled
        }

        // "Process restart": a new container resumes from the database after the backoff.
        harness.Bank.Status = System.Net.HttpStatusCode.OK;
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await using var second = harness.BuildProvider();
        await second.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        await second.GetRequiredService<PapssBankPushWorker>().RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, harness.Bank.Count);
        Assert.Equal(PapssDeliveryState.Delivered, (await Stored(harness, "TX-P15")).BankDeliveryState);
        var e = await harness.WithStorageAsync(db => db.PapssOperationEvents.AsNoTracking().SingleAsync(x => x.SourceMessageId == "PAPSS-S-151"));
        Assert.Equal((PapssDeliveryState.Delivered, 2), (e.PushState, e.PushAttempts));
    }

    [Fact]
    public async Task Lookups_by_request_message_id_end_to_end_id_and_404s()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var requestMessageId = (await Pay(harness, provider, Payment("TX-P16")))["requestMessageId"]!.GetValue<string>();

        Assert.Equal("TX-P16", (await Lookup(harness, provider, c => c.GetOperation(requestMessageId, null, CancellationToken.None)))["txId"]!.GetValue<string>());
        Assert.Equal("TX-P16", (await Lookup(harness, provider, c => c.FindOperation("E2E-TX-P16", null, CancellationToken.None)))["txId"]!.GetValue<string>());

        using var scope = provider.CreateScope();
        var controller = Controller(harness, scope.ServiceProvider);
        Assert.IsType<NotFoundObjectResult>(await controller.GetPayment("TX-NOPE", null, CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await controller.GetReturnOperation("RTN-NOPE", null, CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await controller.FindOperation("E2E-NOPE", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.FindOperation(null, null, CancellationToken.None));
        // The verification-only lookup does not expose payments.
        Assert.IsType<NotFoundObjectResult>(await controller.GetVerification(requestMessageId, null, CancellationToken.None));
    }

    // ---------------------------------------------------------------------------------------------

    private static JsonObject Payment(string txId) => new()
    {
        ["rail"] = "PAPSS", ["agent"] = PostgresHarness.ForeignBic, ["lclInstrument"] = "USDP", ["ctgPurp"] = "CASH", ["localId"] = "E2E-" + txId, ["txId"] = txId,
        ["amount"] = 10, ["currency"] = "USD", ["drName"] = "AMINA ALI", ["drAccount"] = "100200", ["drAccountType"] = "BBAN",
        ["crName"] = "FORTRESS GLOBAL", ["crAccount"] = "0012030321735", ["crAccountType"] = "BBAN", ["crAgentBIC"] = PostgresHarness.ForeignBic, ["narration"] = "invoice 7"
    };

    private static JsonObject ReturnBody(string returnId, string txId, string endToEndId) => new()
    {
        ["rail"] = "PAPSS", ["toBIC"] = PostgresHarness.ForeignBic, ["lclInstrument"] = "USDP", ["ctgPurp"] = "CASH", ["originalAmount"] = 10, ["originalCurrency"] = "USD",
        ["txId"] = txId, ["endToEndId"] = endToEndId, ["reason"] = "FOCR", ["additionalInfo"] = "customer request", ["returnId"] = returnId
    };

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
            harness.Options, harness.Clock, services.GetRequiredService<IPapssPaymentService>());
    }

    private static async Task<ActionResult> PayRaw(PostgresHarness harness, ServiceProvider provider, JsonObject body)
    {
        using var scope = provider.CreateScope();
        return await Controller(harness, scope.ServiceProvider).MakePayment(body, CancellationToken.None);
    }

    private static async Task<JsonObject> Pay(PostgresHarness harness, ServiceProvider provider, JsonObject body)
        => (JsonObject)Assert.IsType<OkObjectResult>(await PayRaw(harness, provider, body)).Value!;

    private static async Task<ActionResult> ReturnRaw(PostgresHarness harness, ServiceProvider provider, JsonObject body)
    {
        using var scope = provider.CreateScope();
        return await Controller(harness, scope.ServiceProvider).GetReturn(body, CancellationToken.None);
    }

    private static async Task<JsonObject> Return(PostgresHarness harness, ServiceProvider provider, JsonObject body)
        => (JsonObject)Assert.IsType<OkObjectResult>(await ReturnRaw(harness, provider, body)).Value!;

    private static async Task<JsonObject> StatusQuery(PostgresHarness harness, ServiceProvider provider, string txId, string? endToEndId = null)
    {
        using var scope = provider.CreateScope();
        var body = new JsonObject { ["rail"] = "PAPSS", ["txId"] = txId, ["endToEnd"] = endToEndId ?? "E2E-" + txId, ["toBIC"] = PostgresHarness.ForeignBic };
        return (JsonObject)Assert.IsType<OkObjectResult>(await Controller(harness, scope.ServiceProvider).GetStatus(body, CancellationToken.None)).Value!;
    }

    private static async Task<JsonObject> Lookup(PostgresHarness harness, ServiceProvider provider, Func<GatewayController, Task<ActionResult>> call)
    {
        using var scope = provider.CreateScope();
        return (JsonObject)Assert.IsType<OkObjectResult>(await call(Controller(harness, scope.ServiceProvider))).Value!;
    }

    private static async Task<List<JsonObject>> Unresolved(PostgresHarness harness, ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var value = Assert.IsType<OkObjectResult>(await Controller(harness, scope.ServiceProvider).UnresolvedPapssEvents(50, CancellationToken.None)).Value!;
        return JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(value))!.AsArray().Select(x => x!.AsObject()).ToList();
    }

    private static async Task<PapssIngestResult> Status(ServiceProvider provider, PostgresHarness harness, string pacs002)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPapssPaymentCallbackService>().HandleStatusReportAsync(harness.Binding(), pacs002, CancellationToken.None);
    }

    private static async Task<PapssIngestResult> InboundReturn(ServiceProvider provider, PostgresHarness harness, string pacs004)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPapssPaymentCallbackService>().HandleReturnAsync(harness.Binding(), pacs004, CancellationToken.None);
    }

    /// <summary>Simulates the legacy pacs.008 handler (isomessages row + bank decision) followed by the operation recording.</summary>
    private static async Task<PapssOperation> RecordInbound(ServiceProvider provider, PostgresHarness harness, string sourceId, string txId, string endToEndId, string decision, string? reason = null, bool insertIso = true)
    {
        var pacs008 = PostgresHarness.InboundPayment(sourceId, txId, endToEndId);
        if (insertIso)
            await harness.WithStorageAsync(async db =>
            {
                db.ISOMessages.Add(new ISOMessage
                {
                    MessageType = ISOMessageType.TransactionRequest, Status = TransactionStatus.Success, MsgId = "MSG-" + txId, BizMsgIdr = sourceId, MsgDefIdr = "pacs.008.001.10",
                    BusinessService = harness.Options.SecurityProfile, TxId = txId, EndToEndId = endToEndId, Date = DateTimeOffset.UtcNow, FromBIC = PostgresHarness.Gateway, ToBIC = PostgresHarness.LocalBic,
                    Message = Encoding.UTF8.GetBytes(pacs008), Response = Encoding.UTF8.GetBytes(PostgresHarness.Decision(txId, endToEndId, decision, reason)),
                    PapssDecision = Encoding.UTF8.GetBytes("<signed-decision/>")
                });
                return await db.SaveChangesAsync(CancellationToken.None);
            });
        using var scope = provider.CreateScope();
        var callbacks = scope.ServiceProvider.GetRequiredService<IPapssPaymentCallbackService>();
        await callbacks.RecordInboundPaymentAsync(harness.Binding(), pacs008, CancellationToken.None);
        await callbacks.SyncInboundPaymentAsync(pacs008, CancellationToken.None);
        return await Stored(harness, txId, PapssDirection.Inbound);
    }

    private static Task<PapssOperation> Stored(PostgresHarness harness, string txId, PapssDirection direction = PapssDirection.Outbound)
        => harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Operation == PapssOperationType.Payment && x.Direction == direction && x.TxId == txId));
}
