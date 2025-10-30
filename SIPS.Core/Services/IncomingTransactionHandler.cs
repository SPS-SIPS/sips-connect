using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIPS.Adapter;
using SIPS.Core.Interfaces;
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
            return ErrorResponse("Failed to verify the signature or parse the message.");

        // Step 2: Record the incoming ISO message via service
        var record = await _isoService.RecordIncomingTransactionAsync(request, message, ct);

        // Step 3: Prepare response object
        var response = _responses.BuildPaymentInitial(request);

        try
        {
            // Step 4: Send callback and parse result via orchestrator
            var headers = new Dictionary<string, string>() {
                { API_Key, _callbackLinks.Key! },
                { API_Secret, _callbackLinks.Secret! }
            };
            var dto = new CBPaymentRequestDto
            {
                FromBIC = request.From,
                LocalInstrument = request.LocalInstrument,
                CategoryPurpose = request.CategoryPurpose,
                EndToEndId = request.EndToEndId,
                TxId = request.TxId,
                Amount = request.Amount,
                Currency = request.Currency,
                DebtorName = request.Debtor.Name,
                DebtorAccount = request.Debtor.Account,
                DebtorAccountType = request.Debtor.AccountType,
                DebtorAgentBIC = request.Debtor.AgentBIC,
                DebtorIssuer = request.Debtor.Issuer ?? "C",
                CreditorName = request.Creditor.Name,
                CreditorAccount = request.Creditor.Account,
                CreditorAccountType = request.Creditor.AccountType,
                CreditorAgentBIC = request.Creditor.AgentBIC,
                CreditorIssuer = request.Creditor.Issuer ?? "C",
                RemittanceInformation = request.Ustrd ?? "",
                Date = request.CreDt,
                ToBIC = request.To,
                SettlementMethod = request.SettlementMethod.ToString(),
                ChargeBearer = request.ChargeBearer.ToString(),
                BizMsgIdr = request.BizMsgIdr,
                MsgDefIdr = request.MsgDefIdr,
                ClearingSystem = request.ClearingSystem,
                MsgId = request.MsgId
            };
            var responseMessage = await _callbacks.SendJsonAsync(
                _callbackLinks.Transfer!,
                headers,
                dto,
                CB_PaymentRequest,
                _jsonAdapter,
                _correlation,
                _jsonSerializerOptions,
                _callback,
                ct,
                cid);
            if (responseMessage.StatusCode == HttpStatusCode.OK && responseMessage.Data != null)
            {
                ParseCallbackResult(responseMessage.Data, response);
            }
            else
            {
                _logger.LogWarning("[{CorrelationId}] Failed to get response from CB. Status: {Status}", cid, responseMessage.StatusCode);
                response.AdditionalInfo = "Failed to get response from CB.";
            }

            // Step 5: Build, persist, and sign response
            var rsp = PaymentRequestResponseBuilder.Build(response);
            await _isoService.PersistTransactionResponseAsync(record,
                response.Status ?? RJCT,
                response.Reason ?? "XYZ",
                response.AdditionalInfo,
                rsp,
                response.TxId ?? string.Empty,
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
                response.Status ?? RJCT,
                response.Reason ?? MISS,
                response.AdditionalInfo,
                rsp,
                response.TxId ?? string.Empty,
                request?.EndToEndId ?? string.Empty,
                ct);
            return _signer.SignEnvelope(rsp);
        }
    }


    private string ErrorResponse(string message)
    {
        return AdminMessage.Generate(message);
    }

    // verification and parsing now delegated to shared services, record/persist via IISOMessageService
    private void ParseCallbackResult(JsonObject data, PaymentRequestResponseBuilder.Response response)
    {
        // convert the responseContent to a JsonObject
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);
        var md = _jsonAdapter.Transform(js!, "CB_PaymentResponse");
        var deserializedContent = _jsonAdapter.ToObject<PaymentResponseDto>(md);

        response.Status = deserializedContent?.Status ?? RJCT;
        response.Reason = deserializedContent?.Reason ?? string.Empty;
        response.AdditionalInfo = deserializedContent?.AdditionalInfo ?? string.Empty;
        response.AcceptanceDate = deserializedContent?.AcceptanceDate ?? DateTime.UtcNow;
        response.TxId = deserializedContent?.TxId ?? string.Empty;
    }

}