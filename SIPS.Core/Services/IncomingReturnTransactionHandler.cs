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
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Implementations;
using SIPS.PostgreSQL.Enums;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
namespace SIPS.Core.Services;
public sealed class IncomingReturnTransactionHandler(
    ISO20022Options options,
    ILogger<IncomingReturnTransactionHandler> logger,
    INativeSigner signer,
    IJsonAdapter jsonAdapter,
    IIncomingRecorder record,
    ISignatureService signature,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    ICallbackClient callback,
    IReturnPaymentRequestParser parser,
    IInboundMessageService inbound,
    ICallbackOrchestrator callbacks,
    IISOMessageService isoService,
    IStatusOrchestrator statusOrchestrator,
    IOptions<CoreOptions> coreOptions
    ) : IIncomingReturnTransactionHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly ILogger<IncomingReturnTransactionHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IIncomingRecorder _record = record;
    private readonly ISignatureService _signature = signature;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly ICallbackClient _callback = callback;
    private readonly IReturnPaymentRequestParser _parser = parser;
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
    public IncomingReturnTransactionHandler(
        ISO20022Options options,
        ILogger<IncomingReturnTransactionHandler> logger,
        INativeSigner signer,
        IJsonAdapter jsonAdapter,
        IIncomingRecorder record,
        ISignatureService signature,
        IPersistenceGateway persistence,
        ICorrelationService correlation,
        ICallbackClient callback,
        IReturnPaymentRequestParser parser,
        IOptions<CoreOptions> coreOptions)
        : this(options, logger, signer, jsonAdapter, record, signature, persistence, correlation, callback, parser,
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
        using var dbCts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
        var dbCt = dbCts.Token;
        // Step 1: Verify signature and parse message via helper
        var (isValid, request) = await _inbound.VerifyAndParseAsync(
            message,
            (xml) =>
            {
                if (!_parser.TryParse(xml, out var req)) return (false, (ReturnPaymentRequestBuilder.Request?)null);
                return (true, req);
            },
            ct,
            cid);
        if (!isValid || request == null)
            return ErrorResponse("Failed to verify the signature or parse the message.");

        // Step 2: Save the message as you received it via service
        var record = await _isoService.RecordIncomingReturnAsync(request, message, ct);

        // Step 3: Get the original message
        var originalMessage = await _persistence.GetISOMessageWithTransactionsByTxIdAsync(request.OrgnlTxId, ct);
        var response = BuildInitialResponse(request);
        _logger.LogInformation("[{CorrelationId}] Retrieved original message response (IRTH): {response}", cid, JsonSerializer.Serialize(response, _jsonSerializerOptions));

        // Step 4: Validate original message exists and is a transaction request
        if (originalMessage == null)
        {
            _logger.LogWarning("[{CorrelationId}] Return rejected: Original transaction {TxId} not found", cid, request.OrgnlTxId);
            response.AdditionalInfo = "Original transaction not found.";
            response.Reason = MISS;
            response.Status = RJCT;
            var rsp = ReturnPaymentResponseBuilder.Build(response);
            await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, dbCt);
            return _signer.SignEnvelope(rsp);
        }

        if (originalMessage.MessageType != PostgreSQL.Enums.ISOMessageType.TransactionRequest)
        {
            _logger.LogWarning("[{CorrelationId}] Return rejected: Original message {TxId} is not a TransactionRequest (type={Type})",
                cid, request.OrgnlTxId, originalMessage.MessageType);
            response.AdditionalInfo = "Original message is not a transaction request.";
            response.Reason = MISS;
            response.Status = RJCT;
            var rsp = ReturnPaymentResponseBuilder.Build(response);
            await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, dbCt);
            return _signer.SignEnvelope(rsp);
        }

        // Step 5: Validate original transaction was successfully completed (ACSC)
        // Only allow returns for transactions that were accepted by IPS
        if (originalMessage.Status != TransactionStatus.Success && originalMessage.Status != TransactionStatus.ReadyForReturn)
        {
            _logger.LogWarning("[{CorrelationId}] Return rejected: Original transaction {TxId} status is {Status}, not Success or ReadyForReturn",
                cid, request.OrgnlTxId, originalMessage.Status);
            response.AdditionalInfo = $"Cannot return transaction with status {originalMessage.Status}. Only successful transactions can be returned.";
            response.Reason = "NOAS"; // No Original Transaction
            response.Status = RJCT;
            var rsp = ReturnPaymentResponseBuilder.Build(response);
            await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? "NOAS", response.AdditionalInfo ?? string.Empty, rsp, dbCt);
            return _signer.SignEnvelope(rsp);
        }

        // Step 6: Validate return request fields against original transaction
        var originalTransaction = originalMessage.Transactions.FirstOrDefault();
        if (originalTransaction != null)
        {
            var validationErrors = new List<string>();

            // Validate amount (if provided in return request)
            if (request.OriginalAmount > 0 && request.OriginalAmount != originalTransaction.Amount)
            {
                validationErrors.Add($"Amount mismatch: return={request.OriginalAmount}, original={originalTransaction.Amount}");
            }

            // Validate currency
            if (!string.IsNullOrWhiteSpace(request.OriginalCurrency) &&
                !string.Equals(request.OriginalCurrency, originalTransaction.Currency, StringComparison.OrdinalIgnoreCase))
            {
                validationErrors.Add($"Currency mismatch: return={request.OriginalCurrency}, original={originalTransaction.Currency}");
            }

            // Validate EndToEndId
            if (!string.IsNullOrWhiteSpace(request.OriginalEndToEnd) &&
                !string.Equals(request.OriginalEndToEnd, originalTransaction.EndToEndId, StringComparison.OrdinalIgnoreCase))
            {
                validationErrors.Add($"EndToEndId mismatch: return={request.OriginalEndToEnd}, original={originalTransaction.EndToEndId}");
            }

            if (validationErrors.Any())
            {
                var errorMessage = string.Join("; ", validationErrors);
                _logger.LogWarning("[{CorrelationId}] Return rejected: Validation failed for {TxId}: {Errors}",
                    cid, request.OrgnlTxId, errorMessage);
                response.AdditionalInfo = $"Return validation failed: {errorMessage}";
                response.Reason = "NARR"; // Narrative Reason
                response.Status = RJCT;
                var rsp = ReturnPaymentResponseBuilder.Build(response);
                await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? "NARR", response.AdditionalInfo ?? string.Empty, rsp, dbCt);
                return _signer.SignEnvelope(rsp);
            }
        }

        try
        {
            // Step 7: Send callback and parse result via orchestrator
            var headers = new Dictionary<string, string>() {
                { API_Key, _callbackLinks.Key! },
                { API_Secret, _callbackLinks.Secret! }
            };
            var idem = string.IsNullOrWhiteSpace(request.ReturnId) ? request.OrgnlTxId : $"{request.OrgnlTxId}-{request.ReturnId}";
            headers["X-Idempotency-Key"] = idem;
            headers["X-Transaction-Id"] = request.OrgnlTxId;
            if (!string.IsNullOrWhiteSpace(request.ReturnId))
                headers["X-Return-Id"] = request.ReturnId;
            if (!string.IsNullOrWhiteSpace(request.OriginalEndToEnd))
                headers["X-EndToEnd-Id"] = request.OriginalEndToEnd;
            var dto = new CBReturnRequestDto
            {
                FromBIC = request.From,
                OriginalEndToEnd = request.OriginalEndToEnd,
                OrgnlTxId = request.OrgnlTxId,
                ReturnId = request.ReturnId,
                Reason = request.ReturnReason,
                AdditionalInfo = request.AdditionalInfo,
            };
            var responseMessage = await _callbacks.SendJsonAsync(
                _callbackLinks.Return!,
                headers,
                dto,
                CB_ReturnRequest,
                _jsonAdapter,
                _correlation,
                _jsonSerializerOptions,
                _callback,
                ct,
                cid);
            if (responseMessage.StatusCode == HttpStatusCode.OK && responseMessage.Data != null)
            {
                ParseCallbackResult(responseMessage.Data, response, originalMessage);
            }
            else
            {
                response.AdditionalInfo = "Failed to get response from CB.";
            }
            // Step 6: Build, persist, and sign response
            var rsp = ReturnPaymentResponseBuilder.Build(response);
            _logger.LogInformation("[{CorrelationId}] Built response (IRTH): {Response}", cid, rsp);
            await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, dbCt);
            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] INCOMING PS Handler Exception for TxId {TxId}", cid, request.OrgnlTxId);
            response.AdditionalInfo = "Failed to transfer: " + ex.Message;
            var rsp = ReturnPaymentResponseBuilder.Build(response);
            await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, dbCt);
            return _signer.SignEnvelope(rsp);
        }
    }

    private ReturnPaymentResponseBuilder.Response BuildInitialResponse(ReturnPaymentRequestBuilder.Request request)
    {
        return new ReturnPaymentResponseBuilder.Response
        {
            From = request.To,
            To = request.From,
            MsgDefIdr = request.MsgDefIdr,
            BizMsgIdr = request.BizMsgIdr,
            MsgId = request.MsgId,
            CreDt = request.CreDt,
            Status = RJCT,
            Reason = MISS,
            AdditionalInfo = "Failed to get the Message.",
            Original = new ReturnPaymentRequestBuilder.Request
            {
                From = request.From,
                To = request.To,
                MsgDefIdr = request.MsgDefIdr,
                BizMsgIdr = request.BizMsgIdr,
                MsgId = request.MsgId,
                CreDt = request.CreDt,
                OrgnlTxId = request.OrgnlTxId,
                OriginalEndToEnd = request.OriginalEndToEnd,
                ReturnId = request.ReturnId,
                ClearingSystem = request.ClearingSystem,
                LocalInstrument = request.LocalInstrument,
                CategoryPurpose = request.CategoryPurpose,
                OriginalAmount = request.OriginalAmount,
                OriginalCurrency = request.OriginalCurrency,
                ReturnReason = request.ReturnReason,
                AdditionalInfo = request.AdditionalInfo,
            }
        };
    }

    private string ErrorResponse(string message)
    {
        return AdminMessage.Generate(message);
    }
    private void ParseCallbackResult(JsonObject data, ReturnPaymentResponseBuilder.Response response, PostgreSQL.Models.ISOMessage originalMessage)
    {
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);

        var md = _jsonAdapter.Transform(js!, CB_ReturnResponse);

        var deserializedContent = _jsonAdapter.ToObject<CBReturnResponseDto>(md);

        if (deserializedContent == null)
        {
            response.Status = RJCT;
            response.Reason = MISS;
            response.AdditionalInfo = "Failed to parse the message.";
            return;
        }

        var originalTransaction = originalMessage.Transactions.FirstOrDefault();

        response.Status = deserializedContent.Status ?? RJCT;
        response.Reason = deserializedContent.Reason ?? string.Empty;
        response.AdditionalInfo = deserializedContent.AdditionalInfo ?? string.Empty;
        response.TxId = deserializedContent.OrgnlTxId ?? string.Empty;

        response.Original.From = originalMessage.FromBIC ?? string.Empty;
        response.Original.To = originalMessage.ToBIC ?? string.Empty;
        response.Original.BizMsgIdr = originalMessage.BizMsgIdr ?? string.Empty;
        response.Original.MsgId = originalMessage.MsgId ?? string.Empty;
        response.Original.ClearingSystem = "FP";
        response.Original.CreDt = originalMessage.Date.UtcDateTime;
        if (originalTransaction != null)
        {
            response.Original.LocalInstrument = originalTransaction.LocalInstrument ?? string.Empty;
            response.Original.CategoryPurpose = originalTransaction.CategoryPurpose ?? string.Empty;
            response.Original.OriginalEndToEnd = originalTransaction.EndToEndId ?? string.Empty;
            response.Original.OrgnlTxId = originalTransaction.TxId ?? string.Empty;
            response.Original.OriginalAmount = originalTransaction.Amount;
            response.Original.OriginalCurrency = originalTransaction.Currency ?? string.Empty;
        }
    }
}