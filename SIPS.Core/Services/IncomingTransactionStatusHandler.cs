using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIPS.Adapter;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Services;
using Microsoft.Extensions.Logging;
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Enums;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Implementations;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;

namespace SIPS.Core.Services;

public sealed class IncomingTransactionStatusHandler(
    ISO20022Options options,
    ILogger<IncomingTransactionStatusHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IJsonAdapter jsonAdapter,
    IIncomingRecorder record,
    ISignatureService signature,
    IPaymentStatusRequestParser parser,
    ICallbackClient callback,
    IResponseFactory responseFactory,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    IInboundMessageService inbound,
    ICallbackOrchestrator callbacks,
    IISOMessageService isoService,
    IStatusOrchestrator statusOrchestrator,
    IOptions<CoreOptions> coreOptions
) : IIncomingTransactionStatusHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ILogger<IncomingTransactionStatusHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly INativeVerifier _verifier = verifier;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IIncomingRecorder _record = record;
    private readonly ISignatureService _signature = signature;
    private readonly IPaymentStatusRequestParser _parser = parser;
    private readonly ICallbackClient _callback = callback;
    private readonly IResponseFactory _responses = responseFactory;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly IInboundMessageService _inbound = inbound;
    private readonly ICallbackOrchestrator _callbacks = callbacks;
    private readonly IISOMessageService _isoService = isoService;
    private readonly IStatusOrchestrator _statusOrchestrator = statusOrchestrator;
    private readonly CoreOptions _core = coreOptions.Value;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Compatibility constructor for tests and existing code paths
    public IncomingTransactionStatusHandler(
        ISO20022Options options,
        ILogger<IncomingTransactionStatusHandler> logger,
        IInterfaceHttpClient httpClient,
        INativeSigner signer,
        INativeVerifier verifier,
        IJsonAdapter jsonAdapter,
        IIncomingRecorder record,
        ISignatureService signature,
        IPaymentStatusRequestParser parser,
        ICallbackClient callback,
        IResponseFactory responseFactory,
        IPersistenceGateway persistence,
        ICorrelationService correlation,
        IOptions<CoreOptions> coreOptions)
        : this(options, logger, httpClient, signer, verifier, jsonAdapter, record, signature, parser, callback, responseFactory, persistence, correlation,
              new InboundMessageService(signature),
              new CallbackOrchestrator(),
              new ISOMessageService(persistence),
              new StatusOrchestrator(logger as ILogger<StatusOrchestrator> ?? throw new ArgumentNullException("StatusOrchestrator logger")),
              coreOptions)
    {
    }

    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        var cid = _correlation.Create();
        // Step 1: Verify signature and parse message
        var (isValid, request) = await _inbound.VerifyAndParseAsync(
            message,
            (xml) =>
            {
                if (!_parser.TryParse(xml, out var req)) return (false, (PaymentStatusRequestBuilder.Request?)null);
                return (true, req);
            },
            ct,
            cid);
        if (!isValid || request == null)
            return AdminMessage.Generate("Failed to verify the signature or parse the message.");

        // Step 2: Retrieve ISO message by TxId (with transactions for richer context)
        var isoMessage = await _persistence.GetISOMessageWithTransactionsByTxIdAsync(request.OrgnlTxId, ct);
        if (isoMessage == null)
        {
            _logger.LogWarning("[{CorrelationId}] Status request for non-existent transaction {TxId}", cid, request.OrgnlTxId);
            using var dbCts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(10));
            await CreateISOMessage(request, message, dbCts.Token);
            return AdminMessage.Generate("Failed to get the Message.");
        }

        // Step 3: Record the incoming status message
        var record = await _isoService.RecordIncomingStatusAsync(isoMessage, message, ct);

        // Step 4: Prepare response object
        var response = _responses.BuildPaymentStatusInitial(request);

        try
        {
            using var dbCts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            var dbCt = dbCts.Token;
            // Step 5: Send callback and parse result
            var headers = new Dictionary<string, string>() {
                { API_Key, _callbackLinks.Key! },
                { API_Secret, _callbackLinks.Secret! }
            };
            // Idempotency key for safe retries downstream
            var idem = string.IsNullOrWhiteSpace(request.OriginalEndToEnd)
                ? request.OrgnlTxId
                : $"{request.OrgnlTxId}-{request.OriginalEndToEnd}";
            headers["X-Idempotency-Key"] = idem;
            headers["X-Transaction-Id"] = request.OrgnlTxId;
            if (!string.IsNullOrWhiteSpace(request.OriginalEndToEnd))
                headers["X-EndToEnd-Id"] = request.OriginalEndToEnd;
            var dto = new CBStatusRequestDto
            {
                FromBIC = request.From,
                OriginalEndToEnd = request.OriginalEndToEnd,
                OrgnlTxId = request.OrgnlTxId
            };
            var responseMessage = await _callbacks.SendJsonAsync(
                _callbackLinks.Status!,
                headers,
                dto,
                CB_StatusRequest,
                _jsonAdapter,
                _correlation,
                _jsonSerializerOptions,
                _callback,
                ct,
                cid);

            // Guard against null callback result
            if (responseMessage == null)
            {
                _logger.LogError("[{CorrelationId}] CoreBank status callback returned null for TxId {TxId}", cid, request.OrgnlTxId);
                response.Status = RJCT;
                response.Reason = "CoreBank callback failed";
                response.AdditionalInfo = "Null response from CoreBank";
            }
            else if (responseMessage.StatusCode == HttpStatusCode.OK && responseMessage.Data != null)
            {
                ParseCallbackResult(responseMessage.Data, response);
            }
            else
            {
                _logger.LogWarning("[{CorrelationId}] Failed to get response from CB. Status: {Status}", cid, responseMessage.StatusCode);
                response.Status = RJCT;
                response.Reason = "CoreBank callback failed";
                response.AdditionalInfo = $"Failed to get response from CB. Status: {responseMessage.StatusCode}";
            }

            // Step 6: Build, persist, and sign response (single persist)
            // Use StatusOrchestrator to map status consistently
            var finalStatus = _statusOrchestrator.MapSingleStatus(response.Status ?? RJCT, "CoreBank");
            var rsp = PaymentStatusRequestResponseBuilder.Build(response);
            await _isoService.PersistStatusResponseAsync(record, finalStatus, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, dbCt);
            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] INCOMING PS Handler Exception for TxId {TxId}", cid, request.OrgnlTxId);
            response.AdditionalInfo = "Failed to transfer: " + ex.Message;
            var rsp = PaymentStatusRequestResponseBuilder.Build(response);
            using var dbCts2 = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            await _isoService.PersistStatusResponseAsync(record, TransactionStatus.Failed, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, dbCts2.Token);
            return _signer.SignEnvelope(rsp);
        }
    }

    private void ParseCallbackResult(JsonObject data, PaymentStatusRequestResponseBuilder.Response response)
    {
        _logger.LogDebug("Callback result: {Data}", data.ToString());
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);

        var md = _jsonAdapter.Transform(js!, CB_PaymentStatusResponse);

        var deserializedContent = _jsonAdapter.ToObject<CBPaymentStatusResponseDto>(md);

        if (deserializedContent == null)
        {
            response.Status = RJCT;
            response.Reason = MISS;
            response.AdditionalInfo = string.Empty;
            return;
        }

        response.Status = deserializedContent.Status ?? RJCT;
        response.Reason = deserializedContent.Reason ?? string.Empty;
        response.AdditionalInfo = deserializedContent.AdditionalInfo ?? string.Empty;
        response.AcceptanceDate = deserializedContent.AcceptanceDate;
        response.TxId = deserializedContent.TxId ?? string.Empty;
        response.Original.From = deserializedContent.FromBIC ?? string.Empty;
        response.Original.To = deserializedContent.ToBIC ?? string.Empty;
        response.Original.BizMsgIdr = deserializedContent.BizMsgIdr ?? string.Empty;
        response.Original.MsgId = deserializedContent.MsgId ?? string.Empty;
        response.Original.ClearingSystem = deserializedContent.ClearingSystem ?? string.Empty;
        response.Original.MsgDefIdr = deserializedContent.MsgDefIdr ?? string.Empty;
        response.Original.CreDt = deserializedContent.Date;
        response.Original.LocalInstrument = deserializedContent.LocalInstrument ?? string.Empty;
        response.Original.CategoryPurpose = deserializedContent.CategoryPurpose ?? string.Empty;
        response.Original.EndToEndId = deserializedContent.EndToEndId ?? string.Empty;
        response.Original.TxId = deserializedContent.TxId ?? string.Empty;
        response.Original.Amount = deserializedContent.Amount;
        response.Original.Currency = deserializedContent.Currency ?? string.Empty;
        response.Original.Debtor.Name = deserializedContent.DebtorName ?? string.Empty;
        response.Original.Debtor.Account = deserializedContent.DebtorAccount ?? string.Empty;
        response.Original.Debtor.AccountType = deserializedContent.DebtorAccountType ?? string.Empty;
        response.Original.Debtor.AgentBIC = deserializedContent.DebtorAgentBIC ?? string.Empty;
        response.Original.Debtor.Issuer = deserializedContent.DebtorIssuer ?? string.Empty;
        response.Original.Creditor.Name = deserializedContent.CreditorName ?? string.Empty;
        response.Original.Creditor.Account = deserializedContent.CreditorAccount ?? string.Empty;
        response.Original.Creditor.AccountType = deserializedContent.CreditorAccountType ?? string.Empty;
        response.Original.Creditor.AgentBIC = deserializedContent.CreditorAgentBIC ?? string.Empty;
        response.Original.Creditor.Issuer = deserializedContent.CreditorIssuer ?? string.Empty;
        response.Original.Ustrd = deserializedContent.RemittanceInformation ?? string.Empty;
    }
    // status recording/persisting now handled by IISOMessageService

    private async Task<ISOMessage> CreateISOMessage(PaymentStatusRequestBuilder.Request request, string message, CancellationToken ct)
    {
        // record the incoming message
        return await _persistence.RecordISOMessageAsync(
                   new ISOMessage
                   {
                       MessageType = ISOMessageType.StatusRequest,
                       Date = DateTimeOffset.Now.ToUniversalTime(),
                       FromBIC = request.From,
                       ToBIC = request.To,
                       Message = Encoding.UTF8.GetBytes(message),
                       Status = TransactionStatus.Failed,
                       Reason = "Failed to get message from DB",
                       BizMsgIdr = request.BizMsgIdr,
                       MsgDefIdr = request.MsgDefIdr,
                       MsgId = request.MsgId
                   }
               , ct);
    }
}