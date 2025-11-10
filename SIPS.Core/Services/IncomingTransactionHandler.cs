using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIPS.Adapter;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Services;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Implementations;
using SIPS.PostgreSQL.Enums;
namespace SIPS.Core.Services;
public sealed class IncomingTransactionHandler(
    ISO20022Options options,
    ILogger<IncomingTransactionHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IJsonAdapter jsonAdapter,
    IIncomingRecorder record,
    ISignatureService signature,
    IPaymentRequestParser parser,
    ICallbackClient callback,
    IResponseFactory responseFactory,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    IInboundMessageService inbound,
    ICallbackOrchestrator callbacks,
    IISOMessageService isoService
    ) : IIncomingTransactionHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ILogger<IncomingTransactionHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly INativeVerifier _verifier = verifier;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IIncomingRecorder _record = record;
    private readonly ISignatureService _signature = signature;
    private readonly IPaymentRequestParser _parser = parser;
    private readonly ICallbackClient _callback = callback;
    private readonly IResponseFactory _responses = responseFactory;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly IInboundMessageService _inbound = inbound;
    private readonly ICallbackOrchestrator _callbacks = callbacks;
    private readonly IISOMessageService _isoService = isoService;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Compatibility constructor for tests and existing code paths
    public IncomingTransactionHandler(
        ISO20022Options options,
        ILogger<IncomingTransactionHandler> logger,
        IInterfaceHttpClient httpClient,
        INativeSigner signer,
        INativeVerifier verifier,
        IJsonAdapter jsonAdapter,
        IIncomingRecorder record,
        ISignatureService signature,
        IPaymentRequestParser parser,
        ICallbackClient callback,
        IResponseFactory responseFactory,
        IPersistenceGateway persistence,
        ICorrelationService correlation)
        : this(options, logger, httpClient, signer, verifier, jsonAdapter, record, signature, parser, callback, responseFactory, persistence, correlation,
              new InboundMessageService(signature),
              new CallbackOrchestrator(),
              new ISOMessageService(persistence))
    {
    }
    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        // correlation id
        string cid = _correlation.Create();

        // Step 1: Verify signature and parse message via helper
        var (isValid, request) = await _inbound.VerifyAndParseAsync(
            message,
            (xml) =>
            {
                if (!_parser.TryParse(xml, out var req)) return (false, (PaymentRequestBuilder.Request?)null);
                return (true, req);
            },
            ct,
            cid);
        if (!isValid || request == null)
            return AdminMessage.Generate("Failed to verify the signature or parse the message.");
        // Step 2: Record the incoming ISO message via service
        var record = await _isoService.RecordIncomingTransactionAsync(request, message, ct);

        // Step 3: Prepare response object
        var response = _responses.BuildPaymentInitial(request);

        try
        {
            // Step 4: Immediately acknowledge with ACSC to the sender; CoreBank processing will occur upon status report
            response.Status = ACSC;
            response.Reason = null;
            response.AdditionalInfo = null;

            // Step 5: Build, persist, and sign response
            var rsp = PaymentRequestResponseBuilder.Build(response);
            await _isoService.PersistTransactionResponseAsync(record,
                TransactionStatus.Pending,
                "Transaction Is Pending For Approval",
                null,
                rsp,
                request.TxId ?? string.Empty,
                request.EndToEndId ?? string.Empty,
                ct);
            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] INCOMING PS Handler Exception for TxId {TxId}", cid, request?.TxId);
            response.AdditionalInfo = "Failed to process Transaction";
            var rsp = PaymentRequestResponseBuilder.Build(response);
            await _isoService.PersistTransactionResponseAsync(record,
                TransactionStatus.Failed,
                "Failed to process Transaction",
                response.AdditionalInfo,
                rsp,
                request?.TxId ?? string.Empty,
                request?.EndToEndId ?? string.Empty,
                ct);
            return _signer.SignEnvelope(rsp);
        }
    }
}