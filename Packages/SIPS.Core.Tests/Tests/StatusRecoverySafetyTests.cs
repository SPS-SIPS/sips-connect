using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Adapter;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;

using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public sealed class StatusRecoverySafetyTests
{
    [Fact]
    public async Task Recovery_exhaustion_is_not_cached_as_network_rejection()
    {
        var h = new Harness("PDNG");
        await h.Iso.FinalizeAfterMaxRetriesAsync(h.Row, "No answer after retry budget", CancellationToken.None);
        var response = await h.Query();
        Assert.True(response.IsSuccess);
        Assert.Equal("PDNG", response.Data?.Status);
        Assert.Contains("reconcil", response.Data?.AdditionalInfo ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.Callbacks.Invocations);
    }

    [Theory]
    [InlineData("PDNG")]
    [InlineData("ACSP")]
    [InlineData("ACCP")]
    [InlineData("ZZZZ")]
    [InlineData("")]
    public async Task Nonfinal_or_unknown_status_never_becomes_cached_rejection(string status)
    {
        var h = new Harness(status);
        for (var i = 0; i < 2; i++)
        {
            var result = await h.Query();
            Assert.True(result.IsSuccess);
            Assert.Equal("PDNG", result.Data?.Status);
            Assert.Equal(TransactionStatus.CheckStatus, h.Row.Status);
        }
        Assert.Equal(2, h.Sends);
        Assert.Empty(h.Callbacks.Invocations);
    }

    [Theory]
    [InlineData("ACSC")]
    [InlineData("RJCT")]
    public async Task Invalid_signature_cannot_publish_or_persist_a_final_outcome(string status)
    {
        var h = new Harness(status) { SignatureValid = false };
        var response = await h.Query();
        Assert.Equal("PDNG", response.Data?.Status);
        Assert.Equal(TransactionStatus.CheckStatus, h.Row.Status);
        Assert.Null(h.Row.Response);
        Assert.Empty(h.Callbacks.Invocations);
        Assert.DoesNotContain(h.SavedStates, s => s is TransactionStatus.Success or TransactionStatus.Failed);
    }

    [Theory]
    [InlineData("ACSC", "OTHER-TX", "LOCAL-123")]
    [InlineData("RJCT", "OTHER-TX", "LOCAL-123")]
    [InlineData("ACSC", "BANK-123", "OTHER-LOCAL")]
    [InlineData("RJCT", "BANK-123", "OTHER-LOCAL")]
    public async Task Mismatched_signed_report_cannot_finalize_the_queried_payment(string status, string txId, string localId)
    {
        var h = new Harness(status, txId, localId);
        var response = await h.Query();
        Assert.Equal("PDNG", response.Data?.Status);
        Assert.Equal("BANK-123", response.Data?.TxId);
        Assert.Equal("LOCAL-123", response.Data?.EndToEndId);
        Assert.Equal(TransactionStatus.CheckStatus, h.Row.Status);
        Assert.Null(h.Row.Response);
        Assert.Empty(h.Callbacks.Invocations);
    }

    [Theory]
    [InlineData("ACSC", TransactionStatus.Success)]
    [InlineData("RJCT", TransactionStatus.Failed)]
    public async Task Verified_correlated_final_report_notifies_once_and_is_replayed_from_cache(string status, TransactionStatus expected)
    {
        var h = new Harness(status);
        var first = await h.Query();
        Assert.Equal(status, first.Data?.Status);
        Assert.Equal(expected, h.Row.Status);
        Assert.NotNull(h.Row.Response); // Cached terminal state needs actual network evidence.
        Assert.Single(h.Callbacks.Invocations);
        var second = await h.Query();
        Assert.Equal(status, second.Data?.Status);
        Assert.Equal(1, h.Sends);
        Assert.Single(h.Callbacks.Invocations);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Transport_failure_with_even_a_final_looking_body_remains_pending(HttpStatusCode code)
    {
        var h = new Harness("ACSC") { HttpStatus = code };
        var response = await h.Query();
        Assert.True(response.IsSuccess);
        Assert.Equal("PDNG", response.Data?.Status);
        Assert.Equal(TransactionStatus.CheckStatus, h.Row.Status);
        Assert.Empty(h.Callbacks.Invocations);
    }

    [Theory]
    [InlineData("PDNG")]
    [InlineData("ACSP")]
    [InlineData("ACCP")]
    [InlineData("ZZZZ")]
    [InlineData("")]
    public void Single_IPS_status_mapping_does_not_make_nonfinal_or_unknown_results_failed(string status)
    {
        var mapper = new StatusOrchestrator(NullLogger<StatusOrchestrator>.Instance);
        Assert.Equal(TransactionStatus.CheckStatus, mapper.MapSingleStatus(status, "IPS"));
    }

    private sealed class Harness
    {
        public readonly ISOMessage Row = new()
        {
            Id = 1, TxId = "BANK-123", EndToEndId = "LOCAL-123", FromBIC = "BICX", ToBIC = "TOBIC",
            MessageType = ISOMessageType.TransactionRequest, Status = TransactionStatus.CheckStatus,
            Date = DateTimeOffset.UtcNow, Message = new byte[] { 1 }, Transactions = new List<Transaction>()
        };
        public readonly Mock<ICallbackOrchestrator> Callbacks = new();
        public readonly List<TransactionStatus> SavedStates = new();
        public readonly ISOMessageService Iso;
        public bool SignatureValid = true;
        public HttpStatusCode HttpStatus = HttpStatusCode.OK;
        public int Sends;
        private readonly OutgoingTransactionStatusHandler handler;

        public Harness(string status, string txId = "BANK-123", string localId = "LOCAL-123")
        {
            var xml = OutgoingTransactionIdempotencyTests.NetworkReport(status, txId, localId);
            var persistence = new Mock<IPersistenceGateway>();
            persistence.Setup(x => x.GetISOMessageWithTransactionsByTxIdAsync("BANK-123", It.IsAny<CancellationToken>())).ReturnsAsync(Row);
            persistence.Setup(x => x.RecordISOMessageStatusAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessageStatus row, CancellationToken _) => row);
            persistence.Setup(x => x.ISOMessageStatusResponseAsync(It.IsAny<ISOMessageStatus>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessageStatus row, CancellationToken _) => row);
            persistence.Setup(x => x.ISOMessageResponseAsync(It.IsAny<ISOMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ISOMessage row, CancellationToken _) => { SavedStates.Add(row.Status); return row; });
            Iso = new ISOMessageService(persistence.Object);
            var signer = new Mock<INativeSigner>();
            signer.Setup(x => x.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns((string text, string _) => text);
            var signature = new Mock<ISignatureService>();
            signature.Setup(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => (SignatureValid, (string?)null));
            var sender = new Mock<ISipsRequestSender>();
            sender.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
                .Returns(() => { Sends++; return Task.FromResult(new Response<string>(xml) { IsSuccess = HttpStatus == HttpStatusCode.OK, StatusCode = HttpStatus }); });
            Callbacks.Setup(x => x.SendJsonAsync(It.IsAny<string>(), It.IsAny<IDictionary<string,string>>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<IJsonAdapter>(), It.IsAny<ICorrelationService>(), It.IsAny<JsonSerializerOptions>(),
                It.IsAny<ICallbackClient>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(Response<JsonObject?>.Success(new JsonObject { ["status"] = "ACSC" }));
            var adapter = new Mock<IJsonAdapter>();
            adapter.Setup(x => x.Transform(It.IsAny<JsonObject>(), It.IsAny<string>()))
                .Returns((JsonObject json, string _) => json);
            adapter.Setup(x => x.ToObject<CBCompletionNotificationResponse>(It.IsAny<JsonObject>()))
                .Returns(new CBCompletionNotificationResponse { Status = "ACSC" });
            handler = new OutgoingTransactionStatusHandler(
                new ISO20022Options { BIC = "BICX", SIPS = "https://network.test", Key = "test", Secret = "test", CompletionNotification = "https://bank.test/completion" },
                NullLogger<OutgoingTransactionStatusHandler>.Instance, signer.Object, signature.Object, persistence.Object,
                new CorrelationService(), sender.Object, Iso, new StatusOrchestrator(NullLogger<StatusOrchestrator>.Instance),
                Callbacks.Object, adapter.Object, Mock.Of<ICallbackClient>(),
                Microsoft.Extensions.Options.Options.Create(new CoreOptions()));
        }
        public Task<Response<PaymentResponseDto>> Query() => handler.HandleAsync(
            new StatusRequestDto { TxId = "BANK-123", EndToEnd = "LOCAL-123" }, CancellationToken.None);
    }
}
