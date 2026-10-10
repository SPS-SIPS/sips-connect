using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SIPS.Adapter.Models;
using SIPS.Connect.Controllers;
using SIPS.Connect.Services;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Helpers;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using SIPS.XMLDsig.Xades.Options;
using SIPS.XMLDsig.Xades.Services;
using Xunit;
using Moq;

namespace SIPS.Connect.PostgresTests;

/// <summary>
/// R2 inbound recall (camt.056 recalling a payment THIS institution received) against a real PostgreSQL: ingest/idempotency,
/// the accept/reject decision's mutual exclusivity, and the one-open-recall-per-payment index reused from R1.
/// </summary>
[Trait("Category", "Postgres")]
public sealed class PapssInboundRecallOperationTests
{
    [Fact]
    public async Task Migration_widens_the_open_recall_index_to_cover_inbound_outcomes()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var connection = new NpgsqlConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await using var history = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE migrationid LIKE '%_AddPapssInboundRecall'", connection);
        Assert.Equal(1L, (long)(await history.ExecuteScalarAsync())!);
        await using var index = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_papss_op_open_recall'", connection);
        var definition = (string)(await index.ExecuteScalarAsync())!;
        Assert.Contains("INBOUND_RECALL_AWAITING_DECISION", definition);
        Assert.Contains("INBOUND_RECALL_ACCEPTED_BY_BANK", definition);
        Assert.Contains("INBOUND_RECALL_REJECTED_BY_BANK", definition);
        Assert.Contains("INBOUND_RECALL_UNRESOLVED", definition);
        Assert.DoesNotContain("INBOUND_RECALL_REPLY_SUBMITTED", definition);
    }

    /// <summary>
    /// Mandatory qualification for the R2 release-blocking fix: drives the FULL real SIPS Connect callback stack -- a genuinely
    /// signed camt.056.001.09, the real PapssCallbackGuard, the real IncomingController dispatch, the real
    /// PapssPaymentCallbackService backed by this real PostgreSQL -- instead of a fake endpoint standing in for SIPS Connect.
    /// Before the PapssCallbackGuard allowed-set fix, this exact request was rejected with 400 Bad Request before
    /// HandleInboundRecallAsync (and therefore CreateInboundRecallAsync) ever ran; this proves the whole chain the gateway
    /// depends on for R2 actually works end to end against a real database, not just that the guard's allow-list contains the
    /// right string.
    /// </summary>
    [Fact]
    public async Task Signed_camt056_001_09_reaches_the_real_guard_controller_and_handler_and_is_durably_recorded()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-QUAL-1", "E2E-RX-QUAL-1");

        using var pki = new CallbackPki();
        var signed = pki.Signer.SignEnvelope(
            InboundRecallNotification("CT02-QUAL-NOTIFY-1", "CXL-QUAL-1", "RX-QUAL-1", "E2E-RX-QUAL-1",
                from: PostgresHarness.Gateway, to: PostgresHarness.LocalBic, businessService: harness.Options.SecurityProfile),
            XadesProfile.WpSipsPapss);
        var verified = await pki.Verifier.VerifyWithProvenance(signed, XadesProfile.WpSipsPapss, CancellationToken.None);
        Assert.True(verified.Result, $"certificate={verified.Verbose.CertificateStatus}; signature={verified.Verbose.SignatureStatus}; references={verified.Verbose.ReferencesStatus}; ownership={verified.Verbose.OwnershSIPStatus}");

        using var scope = provider.CreateScope();
        var guard = new PapssCallbackGuard(harness.Options, scope.ServiceProvider.GetRequiredService<XadesOptions>(), scope.ServiceProvider.GetRequiredService<JsonAdapterOptions>(), pki.Verifier);
        var paymentCallbacks = new PapssPaymentCallbackService(scope.ServiceProvider.GetRequiredService<PapssOperationStore>(), scope.ServiceProvider.GetRequiredService<IPapssOutboxSignal>());
        var controller = new IncomingController(Mock.Of<IIncoming>(), guard, Mock.Of<IPapssPaymentDecisionPublisher>(), Mock.Of<IPapssInboundVerificationService>(), scope.ServiceProvider.GetRequiredService<IParticipantCallbackContext>(), NullLogger<IncomingController>.Instance, paymentCallbacks)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(signed));

        var result = await controller.Post(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        var recall = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.RequestMessageId == "CT02-QUAL-NOTIFY-1"));
        Assert.Equal(PapssDirection.Inbound, recall.Direction);
        Assert.Equal(PapssOperationType.Recall, recall.Operation);
        Assert.Equal(PapssOutcome.InboundRecallAwaitingDecision, recall.PapssOutcome);
        Assert.Equal(payment.Id, recall.OriginalOperationId);
        Assert.Equal("DUPL", recall.Reason);
    }

    [Fact]
    public async Task Ingest_is_durable_linked_to_the_received_payment_and_idempotent_on_redelivery()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-1", "E2E-RX-1");
        var message = InboundRecall("CT02-RX-1-RECALL", "RX-1", "E2E-RX-1", "DUPL");

        using (var scope = provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
            var (operation, created) = await store.CreateInboundRecallAsync("<raw/>", message, payment, CancellationToken.None);
            Assert.True(created);
            Assert.Equal(PapssOutcome.InboundRecallAwaitingDecision, operation.PapssOutcome);
            Assert.Equal(payment.Id, operation.OriginalOperationId);
            Assert.Equal("DUPL", operation.Reason);

            // Redelivery of the identical gateway notification (same PAPSS source message id) is idempotent: the same row, not a new one.
            var (replay, createdAgain) = await store.CreateInboundRecallAsync("<raw/>", message, payment, CancellationToken.None);
            Assert.False(createdAgain);
            Assert.Equal(operation.Id, replay.Id);
        }
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperations.CountAsync(x => x.Direction == PapssDirection.Inbound && x.Operation == PapssOperationType.Recall)));
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.EventType == PapssEventTypes.InboundRecallReceived)));
    }

    [Fact]
    public async Task A_recall_naming_a_payment_not_in_the_store_is_still_recorded_unlinked()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var message = InboundRecall("CT02-RX-UNLINKED", "UNKNOWN-TX", "UNKNOWN-E2E", "DUPL");
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
        var (operation, created) = await store.CreateInboundRecallAsync("<raw/>", message, originalPayment: null, CancellationToken.None);
        Assert.True(created);
        Assert.Null(operation.OriginalOperationId);
        Assert.Equal(PapssOutcome.InboundRecallAwaitingDecision, operation.PapssOutcome);
    }

    [Fact]
    public async Task Accept_and_reject_are_mutually_exclusive_a_second_contradictory_decision_is_recorded_but_not_applied()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-2", "E2E-RX-2");
        Guid recallId;
        using (var scope = provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
            var (operation, _) = await store.CreateInboundRecallAsync("<raw/>", InboundRecall("CT02-RX-2-RECALL", "RX-2", "E2E-RX-2", "DUPL"), payment, CancellationToken.None);
            recallId = operation.Id;

            var accepted = await store.RecordInboundRecallDecisionAsync(recallId, accept: true, null, null, CancellationToken.None);
            Assert.Equal(PapssInboundRecallDecisionOutcome.Applied, accepted);

            // A later, contradictory REJECT must not flip an already-decided ACCEPT.
            var contradiction = await store.RecordInboundRecallDecisionAsync(recallId, accept: false, "CUST", "too late", CancellationToken.None);
            Assert.Equal(PapssInboundRecallDecisionOutcome.Conflict, contradiction);
        }
        var stored = await harness.WithStorageAsync(db => db.PapssOperations.AsNoTracking().SingleAsync(x => x.Id == recallId));
        Assert.Equal(PapssOutcome.InboundRecallAcceptedByBank, stored.PapssOutcome);
        Assert.Null(stored.StatusReasonCode); // the conflicting REJECT's reason must never have been applied
        Assert.Equal(2, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.OperationId == recallId && x.EventType == PapssEventTypes.InboundRecallDecision)));
        Assert.Equal(1, await harness.WithStorageAsync(db => db.PapssOperationEvents.CountAsync(x => x.OperationId == recallId && x.EventType == PapssEventTypes.InboundRecallDecision && x.Disposition == PapssEventDisposition.Conflict)));
    }

    [Fact]
    public async Task Repeating_the_same_decision_while_still_open_applies_harmlessly()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-3", "E2E-RX-3");
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
        var (operation, _) = await store.CreateInboundRecallAsync("<raw/>", InboundRecall("CT02-RX-3-RECALL", "RX-3", "E2E-RX-3", "DUPL"), payment, CancellationToken.None);

        Assert.Equal(PapssInboundRecallDecisionOutcome.Applied, await store.RecordInboundRecallDecisionAsync(operation.Id, accept: false, "CUST", null, CancellationToken.None));
        Assert.Equal(PapssInboundRecallDecisionOutcome.Applied, await store.RecordInboundRecallDecisionAsync(operation.Id, accept: false, "CUST", null, CancellationToken.None));
    }

    [Fact]
    public async Task Response_submitted_is_terminal_and_releases_the_open_recall_lock()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-4", "E2E-RX-4");
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
        var (operation, _) = await store.CreateInboundRecallAsync("<raw/>", InboundRecall("CT02-RX-4-RECALL", "RX-4", "E2E-RX-4", "DUPL"), payment, CancellationToken.None);
        Assert.NotNull(await store.FindOpenInboundRecallAsync(payment.Id, CancellationToken.None));

        await store.RecordInboundRecallDecisionAsync(operation.Id, accept: true, null, null, CancellationToken.None);
        await store.RecordInboundRecallReplySubmittedAsync(operation.Id, CancellationToken.None);

        var stored = await store.FindInboundRecallAsync("CT02-RX-4-RECALL", CancellationToken.None);
        Assert.Equal(PapssOutcome.InboundRecallReplySubmitted, stored!.PapssOutcome);
        Assert.NotNull(stored.CompletedAt);
        // The lock is released: a second recall of the SAME payment may now be admitted.
        Assert.Null(await store.FindOpenInboundRecallAsync(payment.Id, CancellationToken.None));
        var (second, created) = await store.CreateInboundRecallAsync("<raw/>", InboundRecall("CT02-RX-4-SECOND", "RX-4", "E2E-RX-4", "FRAD"), payment, CancellationToken.None);
        Assert.True(created);
        Assert.NotEqual(operation.Id, second.Id);
    }

    [Fact]
    public async Task At_most_one_open_inbound_recall_per_payment_the_index_refuses_a_concurrent_second_one()
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        var payment = await SeedReceivedPayment(harness, "RX-5", "E2E-RX-5");
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
        await store.CreateInboundRecallAsync("<raw/>", InboundRecall("CT02-RX-5-A", "RX-5", "E2E-RX-5", "DUPL"), payment, CancellationToken.None);

        // A second, distinct PAPSS source message recalling the SAME payment while the first is still open: the partial
        // unique index on originaloperationid refuses it directly at the database level (defence in depth).
        await Assert.ThrowsAsync<DbUpdateException>(() => harness.WithStorageAsync(async db =>
        {
            db.PapssOperations.Add(new PapssOperation
            {
                Direction = PapssDirection.Inbound,
                Operation = PapssOperationType.Recall,
                RequestMessageId = "CT02-RX-5-B",
                OriginalOperationId = payment.Id,
                OriginalTxId = "RX-5",
                OriginalEndToEndId = "E2E-RX-5",
                GatewayState = PapssGatewayState.NotSubmitted,
                PapssOutcome = PapssOutcome.InboundRecallAwaitingDecision,
                BankDeliveryState = PapssDeliveryState.NotRequired,
                SignedRequest = "<raw/>"u8.ToArray(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(CancellationToken.None);
            return true;
        }));
    }

    private static PapssInboundRecallMessage InboundRecall(string sourceMessageId, string originalTxId, string originalEndToEndId, string reasonCode)
        => new(sourceMessageId, DateTimeOffset.UtcNow, "CXL-" + sourceMessageId, originalTxId, originalEndToEndId, reasonCode, null);

    private static async Task<PapssOperation> SeedReceivedPayment(PostgresHarness harness, string txId, string endToEndId)
    {
        var now = DateTimeOffset.UtcNow;
        return await harness.WithStorageAsync(async db =>
        {
            var payment = new PapssOperation
            {
                Direction = PapssDirection.Inbound,
                Operation = PapssOperationType.Payment,
                RequestMessageId = "CT02-" + txId,
                MsgId = "CT02-" + txId,
                TxId = txId,
                EndToEndId = endToEndId,
                CounterpartyBic = "EGAFEGCX",
                Amount = 500.00m,
                Currency = "USD",
                LocalInstrument = "USDP",
                GatewayState = PapssGatewayState.NotSubmitted,
                PapssOutcome = PapssOutcome.Settled,
                BankDeliveryState = PapssDeliveryState.Delivered,
                SignedRequest = "<raw/>"u8.ToArray(),
                SourceCreatedAt = now,
                ReceivedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.PapssOperations.Add(payment);
            await db.SaveChangesAsync(CancellationToken.None);
            return payment;
        });
    }

    /// <summary>camt.056.001.09 FIToFIPmtCxlReq exactly as the gateway's SipsInboundRecallMessage.Build delivers it (mirrors
    /// SIPS.Connect.Tests.GatewayRecallXml.InboundRecallNotification -- duplicated here rather than shared across test
    /// projects): AppHdr Fr = the gateway's own WP-SIPS identity (the signer), To = this institution's BIC; Assgnr = the
    /// original recalling bank's PAPSS participant id (business data, never the signer); Assgne = this institution's BIC.</summary>
    private static string InboundRecallNotification(string sourceMessageId, string cancellationId, string originalTxId, string originalEndToEndId,
        decimal amount = 500m, string currency = "USD", string reason = "DUPL", string from = "WPSIPSGW", string to = "ZKBASOS0",
        string recallingParticipantId = "PAPSS-FOREIGN", string? businessService = null)
    {
        XNamespace h = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03", d = "urn:iso:std:iso:20022:tech:xsd:camt.056.001.09", e = "urn:iso:std:iso:20022:tech:xsd:inboundRecall_notification";
        XElement Party(string name, string id) => new(h + name, new XElement(h + "FIId", new XElement(h + "FinInstnId", new XElement(h + "Othr", new XElement(h + "Id", id)))));
        var created = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var envelope = new XElement(e + "FPEnvelope",
            new XAttribute(XNamespace.Xmlns + "header", h.NamespaceName), new XAttribute(XNamespace.Xmlns + "document", d.NamespaceName), new XAttribute("Id", "BL-" + sourceMessageId),
            new XElement(h + "AppHdr", Party("Fr", from), Party("To", to), new XElement(h + "BizMsgIdr", sourceMessageId), new XElement(h + "MsgDefIdr", "camt.056.001.09"),
                businessService is null ? null : new XElement(h + "BizSvc", businessService), new XElement(h + "CreDt", created)),
            new XElement(d + "Document", new XElement(d + "FIToFIPmtCxlReq",
                new XElement(d + "Assgnmt", new XElement(d + "Id", sourceMessageId),
                    new XElement(d + "Assgnr", new XElement(d + "Agt", new XElement(d + "FinInstnId", new XElement(d + "Othr", new XElement(d + "Id", recallingParticipantId))))),
                    new XElement(d + "Assgne", new XElement(d + "Agt", new XElement(d + "FinInstnId", new XElement(d + "BICFI", to)))),
                    new XElement(d + "CreDtTm", created)),
                new XElement(d + "Undrlyg", new XElement(d + "TxInf",
                    new XElement(d + "CxlId", cancellationId),
                    new XElement(d + "OrgnlGrpInf", new XElement(d + "OrgnlMsgId", "CT02-" + originalTxId), new XElement(d + "OrgnlMsgNmId", "pacs.008.001.07")),
                    new XElement(d + "OrgnlEndToEndId", originalEndToEndId),
                    new XElement(d + "OrgnlTxId", originalTxId),
                    new XElement(d + "OrgnlIntrBkSttlmAmt", new XAttribute("Ccy", currency), amount.ToString("0.00")),
                    new XElement(d + "CxlRsnInf", new XElement(d + "Rsn", new XElement(d + "Cd", reason))))))));
        return envelope.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>A real signer/verifier pair (self-signed test CA) for the one qualification test that needs a genuinely signed
    /// callback instead of a mocked INativeVerifier -- mirrors SIPS.Connect.Tests.PapssDisabledTests.CallbackPki.</summary>
    private sealed class CallbackPki : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "papss-callback-pki-pg-" + Guid.NewGuid().ToString("N"));
        public NativeSigner Signer { get; }
        public NativeVerifier Verifier { get; }

        public CallbackPki()
        {
            Directory.CreateDirectory(directory);
            using var rootKey = RSA.Create(2048); var rootRequest = new CertificateRequest("CN=PAPSS Test Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true)); rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true)); using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            using var leafKey = RSA.Create(2048); var leafRequest = new CertificateRequest("CN=papss", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true)); leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true)); leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true)); using var unsigned = leafRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10), RandomNumberGenerator.GetBytes(16)); using var leaf = unsigned.CopyWithPrivateKey(leafKey);
            var certPem = new string(PemEncoding.Write("CERTIFICATE", leaf.Export(X509ContentType.Cert))); var rootPem = new string(PemEncoding.Write("CERTIFICATE", root.Export(X509ContentType.Cert))); File.WriteAllText(Path.Combine(directory, "leaf.pem"), certPem); File.WriteAllText(Path.Combine(directory, "root.pem"), rootPem); File.WriteAllText(Path.Combine(directory, "key.pem"), leafKey.ExportPkcs8PrivateKeyPem());
            var xades = new XadesOptions { CertificatePath = Path.Combine(directory, "leaf.pem"), ChainPath = Path.Combine(directory, "root.pem"), PrivateKeyPath = Path.Combine(directory, "key.pem"), BaseDN = leaf.Issuer, Algorithms = ["SHA256withRSA"], DefaultSignatureMethod = "SHA256withRSA", VerificationWindowMinutes = 100 }; var certificates = new CertificateService(xades); Signer = new(xades, NullLogger<NativeSigner>.Instance, certificates);
            var record = new CertificateDownloadResponse(certPem, "papss", false, "SPS", "UAT", PostgresHarness.Gateway, Convert.ToHexString(SHA256.HashData(leaf.Export(X509ContentType.Cert))).ToLowerInvariant(), "SPS.XADES.BES.001@1.0.0", "1.3.6.1.5.5.7.3.2"); Verifier = new(xades, NullLogger<NativeVerifier>.Instance, certificates, new Download(record));
        }

        public void Dispose() => Directory.Delete(directory, true);
        private sealed class Download(CertificateDownloadResponse value) : ICertificateDownloadService { public Task<(CertificateDownloadResponse? Certificates, string? Error)> GetCertificatesAsync(string serialNumber, string issuerDN, CancellationToken cancellationToken = default) => Task.FromResult<(CertificateDownloadResponse?, string?)>((value, null)); }
    }
}
