using Xunit;

using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Implementations;
using Moq;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Verification;
using SIPS.Adapter;
using SIPS.Core.Services.Callback;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;

namespace SIPS.Core.Tests.Tests;


public sealed class IncomingTransactionStatusHandler_Token_Tests
{
    [Fact]
    public void Placeholder_Passes()
    {
        Assert.True(true);
    }
    private sealed class FakeParser : IPaymentStatusRequestParser
    {
        public bool TryParse(string xml, out PaymentStatusRequestBuilder.Request request)
        {
            request = new PaymentStatusRequestBuilder.Request
            {
                From = "FROM",
                To = "TO",
                OrgnlTxId = "TX123",
                OriginalEndToEnd = "E2E"
            };
            return true;
        }
    }

    private sealed class FakeInbound : IInboundMessageService
    {
        public Task<(bool ok, TRequest? request)> VerifyAndParseAsync<TRequest>(
            string xml,
            Func<string, (bool ok, TRequest? request)> tryParse,
            CancellationToken ct,
            string correlationId)
        {
            var (ok, req) = tryParse("<xml/>");
            return Task.FromResult((ok, req));
        }
    }
    private sealed class FakePersistence : IPersistenceGateway
    {
        public CancellationToken? LastToken { get; private set; }
        public Task<ISOMessage> RecordISOMessageAsync(ISOMessage message, CancellationToken ct)
        {
            LastToken = ct;
            return Task.FromResult(message);
        }
        public Task<ISOMessageStatus> RecordISOMessageStatusAsync(ISOMessageStatus status, CancellationToken ct) => Task.FromResult(status);
        public Task<ISOMessage> ISOMessageResponseAsync(ISOMessage message, CancellationToken ct) => Task.FromResult(message);
        public Task<ISOMessageStatus> ISOMessageStatusResponseAsync(ISOMessageStatus status, CancellationToken ct) => Task.FromResult(status);
        public Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct) => Task.FromResult<ISOMessage?>(null);
        public Task<ISOMessage?> GetISOMessageByIdAsync(int id, CancellationToken ct) => Task.FromResult<ISOMessage?>(null);
        public Task<ISOMessage?> GetISOMessageWithTransactionsByTxIdAsync(string txId, CancellationToken ct) => Task.FromResult<ISOMessage?>(null);
        public Task<System.Collections.Generic.List<ISOMessage>> GetISOMessagesByStatusAsync(TransactionStatus status, CancellationToken ct) => Task.FromResult(new System.Collections.Generic.List<ISOMessage>());
        public Task<int> AppendAuditLedgerEventAsync(int isoMessageId, object entry, uint expectedXmin, CancellationToken ct) => Task.FromResult(1);
        public Task<ISOMessage?> GetISOMessageByTxIdAndTypeAsync(string txId, ISOMessageType type, CancellationToken ct) => Task.FromResult<ISOMessage?>(null);
        public Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingTransactionAsync(ISOMessage entity, CancellationToken ct) => Task.FromResult((default(ISOMessage?), true));
        public Task<(ISOMessage record, SIPS.PostgreSQL.Enums.DedupOutcome outcome, string? duplicateBy)> TryRecordIncomingVerificationAsync(ISOMessage entity, CancellationToken ct) => Task.FromResult((entity, SIPS.PostgreSQL.Enums.DedupOutcome.Owner, default(string?)));
    }
    private sealed class Noop : INativeSigner, INativeVerifier, IJsonAdapter, ICallbackClient, ISignatureService
    {
        public string SignEnvelope(string xml) => xml;
        public string SignEnvelope(string xml, string referenceId) => xml;
        public Task<(bool ok, string? verbose)> VerifyAsync(string xml, CancellationToken ct) => Task.FromResult<(bool, string?)>((true, null));
        public Task<(bool result, VerboseResult verbose)> VerifySignature(string xml, bool isDetached, CancellationToken ct) => Task.FromResult((true, new VerboseResult()));
        public JsonObject Transform(JsonObject json, string mappingName) => json;
        public JsonObject Transform<T>(T localObject, string mappingName) => new JsonObject();
        public T ToObject<T>(JsonObject json) => default!;
        public Task<Response<JsonObject?>> SendAsync(string url, System.Collections.Generic.Dictionary<string, string> headers, StringContent content, CancellationToken ct, string? correlationId = null) => Task.FromResult(Response<JsonObject?>.Success(new JsonObject()));
    }

    [Fact]
    public async Task When_ISOMessage_Missing_CreateISOMessage_Uses_DbToken()
    {
        var options = new ISO20022Options();
        var logger = new NullLogger<IncomingTransactionStatusHandler>();
    var httpClient = new InterfaceHttpClient(new NullLogger<InterfaceHttpClient>(), new HttpClient(), Microsoft.Extensions.Options.Options.Create(new CoreOptions()));
        var signer = new Noop();
        var verifier = new Noop();
        var jsonAdapter = new Noop();
    var recorder = Mock.Of<SIPS.PostgreSQL.Interfaces.IIncomingRecorder>();
        var signature = new Noop();
        var parser = new FakeParser();
        var callback = new Noop();
        var responseFactory = new ResponseFactory();
        var persistence = new FakePersistence();
        var correlation = new CorrelationService();
        var inbound = new FakeInbound();
    var callbacks = new CallbackOrchestrator();
    var isoService = new ISOMessageService(persistence);
        var statusOrchestrator = new Mock<IStatusOrchestrator>().Object;
        var core = Microsoft.Extensions.Options.Options.Create(new CoreOptions { DbPersistTimeoutSeconds = 1 });

        var handler = new IncomingTransactionStatusHandler(options, logger, httpClient, signer, verifier, jsonAdapter, recorder, signature, parser, callback, responseFactory, persistence, correlation, inbound, callbacks, isoService, statusOrchestrator, core);

        using var userCts = new CancellationTokenSource();
        userCts.Cancel();

        var _ = await handler.HandleAsync("<xml/>", userCts.Token);

        Assert.NotNull(persistence.LastToken);
        Assert.False(persistence.LastToken!.Value.IsCancellationRequested);
        Assert.NotEqual(userCts.Token, persistence.LastToken!.Value);
    }
}
