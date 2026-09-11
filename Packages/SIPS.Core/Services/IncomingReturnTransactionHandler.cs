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
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
using SIPS.Core.Services.Metrics;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using static SIPS.Core.Constants;
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
              coreOptions)
    {
    }

    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        using var _totalTrack = SipsMetrics.TrackStep("Incoming", "Return", "Total");
        var cid = _correlation.Create();
        using var dbCts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
        var dbCt = dbCts.Token;

        bool isValid = false;
        ReturnPaymentRequestBuilder.Request? request = null;

        // Step 1: Verify signature and parse message via helper
        using (SipsMetrics.TrackStep("Incoming", "Return", "ParsingAndSignature"))
        {
            var (valid, parsedRequest) = await _inbound.VerifyAndParseAsync(
                message,
                (xml) =>
                {
                    if (!_parser.TryParse(xml, out var req)) return (false, (ReturnPaymentRequestBuilder.Request?)null);
                    return (true, req);
                },
                ct,
                cid);
            isValid = valid;
            request = parsedRequest;
        }

        if (!isValid || request == null)
            return _signer.SignEnvelope(SipsReject.Create(message, AdminRejectReasonCodes.SignatureInvalid, "Failed to verify the signature or parse the message."));

        // Step 1.1: Enforce OrgnlTxId as the mandatory anchor (Delta 2)
        if (string.IsNullOrWhiteSpace(request.OrgnlTxId))
        {
            _logger.LogWarning("[{CorrelationId}] Return rejected: Mandatory OrgnlTxId is missing", cid);
            return _signer.SignEnvelope(SipsReject.Create(message, AdminRejectReasonCodes.MandatoryElementMissing, "Mandatory OrgnlTxId is missing."));
        }

        // Step 2: Save the message using Gold Pattern (INSERT-First with de-dup)
        ISOMessage? record;
        bool isNew;
        using (SipsMetrics.TrackStep("Incoming", "Return", "DbSave"))
        {
            var result = await _isoService.TryRecordIncomingReturnAsync(request, message, null, ct);
            record = result.Message;
            isNew = result.IsNew;
        }

        // Step 3: Get the original message
        var originalMessage = await _persistence.GetISOMessageWithTransactionsByTxIdAsync(request.OrgnlTxId, ct);
        var response = BuildInitialResponse(request);

        // Gold Pattern Check: If NOT new, and we have a record, this is a replay.
        if (!isNew && record != null)
        {
            _logger.LogInformation("[{CorrelationId}] Replay detected for Return Request {MsgId}. Returning previous response.", cid, request.MsgId);
            return _signer.SignEnvelope(record.Response != null ? Encoding.UTF8.GetString(record.Response) : SipsReject.Create(message, AdminRejectReasonCodes.DuplicateMessageConflict, "Duplicate return request, but no previous response found."));
        }
        _logger.LogInformation("[{CorrelationId}] Retrieved original message response (IRTH): {response}", cid, JsonSerializer.Serialize(response, _jsonSerializerOptions));

        // Step 4: Validate original message exists and is a transaction request
        if (originalMessage == null)
        {
            _logger.LogInformation("[{CorrelationId}] Return rejected: Original transaction {TxId} not found", cid, request.OrgnlTxId);
            response.AdditionalInfo = "Original transaction not found.";
            response.Reason = MISS;
            response.Status = RJCT;
            var rsp = ReturnPaymentResponseBuilder.Build(response);
            if (record != null)
            {
                await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, dbCt);
            }
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
            if (record != null)
            {
                await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, dbCt);
            }
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
            if (record != null)
            {
                await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? "NOAS", response.AdditionalInfo ?? string.Empty, rsp, dbCt);
            }
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
                if (record != null)
                {
                    await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? "NARR", response.AdditionalInfo ?? string.Empty, rsp, dbCt);
                }
                return _signer.SignEnvelope(rsp);
            }
        }


        // CoreBank Integration (Optional)
        // If configured, we notify CoreBank about the return request.
        // We propagate rejection (RJCT) if CoreBank rejects it (Active Decision).
        // Otherwise, we accept as ACSC (locally Pending/ReadyForReturn) and await pacs.002.
        if (_core.IncludeCoreBankOnListing)
        {
            try
            {
                var cbRequest = new CBReturnRequestDto
                {
                    OrgnlTxId = request.OrgnlTxId,
                    ReturnId = request.ReturnId,
                    Reason = request.ReturnReason,
                    AdditionalInfo = request.AdditionalInfo,
                    OriginalEndToEnd = request.OriginalEndToEnd,
                    FromBIC = request.From
                };

                var headers = new Dictionary<string, string>() {
                        { API_Key, _callbackLinks.Key! },
                        { API_Secret, _callbackLinks.Secret! }
                    };
                headers["X-Idempotency-Key"] = request.ReturnId;
                headers["X-Return-Id"] = request.ReturnId;
                headers["X-Transaction-Id"] = request.OrgnlTxId;

                // [FIX 2]: Scoped timeout matching pacs.008 handler pattern
                using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));

                SIPS.ISO20022.Models.DTOs.Response<JsonObject?>? result = null;
                using (SipsMetrics.TrackStep("Incoming", "Return", "CoreBankCall"))
                {
                    result = await _callbacks.SendJsonAsync(
                        _callbackLinks.Return!,
                        headers,
                        cbRequest,
                        CB_ReturnRequest,
                        _jsonAdapter,
                        _correlation,
                        _jsonSerializerOptions,
                        _callback,
                        coreBankCts.Token,
                        cid
                    );
                }

                if (result?.IsSuccess == true &&
                    result.StatusCode >= System.Net.HttpStatusCode.OK &&
                    result.StatusCode < System.Net.HttpStatusCode.MultipleChoices &&
                    result.Data != null)
                {
                    var cbResult = ParseCallbackResult(result.Data, originalMessage);
                    if (cbResult != null && IsCoreBankSuccess(cbResult.Status))
                    {
                        response.Status = ACSC;
                        response.Reason = "Return accepted by CoreBank - awaiting confirm";
                        response.AdditionalInfo = cbResult.AdditionalInfo ?? "Return pre-approved by CoreBank.";
                    }
                    else
                    {
                        var returnedStatus = string.IsNullOrWhiteSpace(cbResult?.Status) ? "<empty>" : cbResult.Status;
                        _logger.LogWarning("[{CorrelationId}] Return rejected by CoreBank for TxId {TxId}. Status={Status}, Reason={Reason}", cid, request.OrgnlTxId, returnedStatus, cbResult?.Reason);
                        response.Status = RJCT;
                        response.Reason = "MS03";
                        response.AdditionalInfo = IsoText.StatusAdditionalInfo(
                            cbResult?.AdditionalInfo,
                            cbResult?.Reason,
                            $"CoreBank returned non-success status: {returnedStatus}.");

                        var rspReject = ReturnPaymentResponseBuilder.Build(response);
                        if (record != null)
                        {
                            using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
                            await _isoService.PersistReturnResponseAsync(record, RJCT, response.Reason, response.AdditionalInfo, rspReject, updateCts.Token);

                            var ev = new
                            {
                                schemaVersion = 1,
                                eventId = Guid.NewGuid(),
                                actor = "System",
                                @event = "ReturnRejectedByCoreBank",
                                timestampUtc = DateTimeOffset.UtcNow,
                                correlation = new { transactionId = request.OrgnlTxId, returnId = request.ReturnId, msgId = request.MsgId },
                                coreBankDecision = new { status = cbResult?.Status, reason = cbResult?.Reason },
                                finalState = "Rejected"
                            };
                            await _isoService.AppendAuditLedgerEventAsync(record.Id, ev, updateCts.Token);
                        }
                        return _signer.SignEnvelope(rspReject);
                    }
                }
                else
                {
                    _logger.LogWarning("[{CorrelationId}] CoreBank returned null or empty return response for TxId {TxId}. Rejecting for safety.", cid, request.OrgnlTxId);
                    response.Status = RJCT;
                    response.Reason = "MS03";
                    response.AdditionalInfo = "CoreBank returned empty response.";
                    var rspEmpty = ReturnPaymentResponseBuilder.Build(response);
                    if (record != null)
                    {
                        using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
                        await _isoService.PersistReturnResponseAsync(record, RJCT, response.Reason, response.AdditionalInfo, rspEmpty, updateCts.Token);
                    }
                    return _signer.SignEnvelope(rspEmpty);
                }
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // [FIX 2]: CoreBank timeout — matching pacs.008 handler pattern
                _logger.LogWarning(ex, "[{CorrelationId}] CoreBank callback timed out for Return TxId {TxId} (>{Timeout}s). Rejecting for safety.", cid, request.OrgnlTxId, _core.CoreBankTimeoutSeconds);
                response.Status = RJCT;
                response.Reason = "MS03";
                response.AdditionalInfo = "CoreBank response exceeded internal SLA.";
                var rspTimeout = ReturnPaymentResponseBuilder.Build(response);
                if (record != null)
                {
                    using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
                    await _isoService.PersistReturnResponseAsync(record, RJCT, response.Reason, response.AdditionalInfo, rspTimeout, updateCts.Token);

                    var ev = new
                    {
                        schemaVersion = 1,
                        eventId = Guid.NewGuid(),
                        actor = "System",
                        @event = "ReturnCoreBankTimeout",
                        timestampUtc = DateTimeOffset.UtcNow,
                        correlation = new { transactionId = request.OrgnlTxId, returnId = request.ReturnId, msgId = request.MsgId },
                        coreBank = new { timeoutSeconds = _core.CoreBankTimeoutSeconds },
                        finalState = "Rejected"
                    };
                    await _isoService.AppendAuditLedgerEventAsync(record.Id, ev, updateCts.Token);
                }
                return _signer.SignEnvelope(rspTimeout);
            }
            catch (Exception ex)
            {
                // [FIX 1]: Unified failure policy — RJCT on CoreBank connectivity failure (matching pacs.008 handler)
                _logger.LogError(ex, "[{CorrelationId}] Failed to call CoreBank for Return {TxId} (Connectivity issue). Rejecting for safety.", cid, request.OrgnlTxId);
                response.Status = RJCT;
                response.Reason = "MS03";
                response.AdditionalInfo = "Failed to reach CoreBank for authorization.";
                var rspErr = ReturnPaymentResponseBuilder.Build(response);
                if (record != null)
                {
                    using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
                    await _isoService.PersistReturnResponseAsync(record, RJCT, response.Reason, response.AdditionalInfo, rspErr, updateCts.Token);
                }
                return _signer.SignEnvelope(rspErr);
            }
        }

        try
        {
            // Step 7: Mark original transaction as ReadyForReturn and return ACSC
            // DO NOT call CoreBank yet - wait for pacs.002 confirmation first
            _logger.LogInformation("[{CorrelationId}] Marking original transaction {TxId} as ReadyForReturn with ReturnId {ReturnId}",
                cid, request.OrgnlTxId, request.ReturnId);

            using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            // Update original message status to ReadyForReturn and store ReturnId
            originalMessage.Status = TransactionStatus.ReadyForReturn;
            originalMessage.ReturnId = request.ReturnId; // Store ReturnId for audit trail
            originalMessage.Reason = "Return received - awaiting confirm";
            originalMessage.AdditionalInfo = $"Return requested with ReturnId: {request.ReturnId}";
            await _persistence.ISOMessageResponseAsync(originalMessage, updateCts.Token);

            // Build ACSC acknowledgment response
            response.Status = ACSC;
            response.Reason = "Return accepted - awaiting confirm";
            response.AdditionalInfo = "Transaction marked as ReadyForReturn. Awaiting pacs.002 confirmation to complete return.";

            var rsp = ReturnPaymentResponseBuilder.Build(response);
            _logger.LogInformation("[{CorrelationId}] Built ACSC response (IRTH): {Response}", cid, rsp);

            // Persist return message as ReadyForReturn (not final status yet)
            if (record != null)
            {
                await _isoService.PersistReturnResponseAsync(record, ACSC, response.Reason ?? ACSC, response.AdditionalInfo ?? string.Empty, rsp, updateCts.Token, TransactionStatus.ReadyForReturn);
            }

            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Failed to process return request", cid);
            response.Status = RJCT;
            response.Reason = MISS;
            response.AdditionalInfo = "Failed to process return request.";
            var rsp = ReturnPaymentResponseBuilder.Build(response);
            if (record != null)
            {
                using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
                await _isoService.PersistReturnResponseAsync(record, response.Status ?? RJCT, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, updateCts.Token);
            }
            return _signer.SignEnvelope(rsp);
        }
    }

    // Note: CoreBank callback for return completion will be triggered by IncomingPaymentStatusReportHandler
    // when pacs.002 confirmation is received. The handler will:
    // 1. Check if original transaction status is ReadyForReturn
    // 2. Call CoreBank to complete the return
    // 3. Finalize the transaction status based on CoreBank response

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
            Status = ACSC,
            Reason = string.Empty,
            AdditionalInfo = string.Empty,
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

    private CBReturnResponseDto? ParseCallbackResult(JsonObject data, PostgreSQL.Models.ISOMessage originalMessage)
    {
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);

        var md = _jsonAdapter.Transform(js!, CB_ReturnResponse);

        return _jsonAdapter.ToObject<CBReturnResponseDto>(md);
    }

    private static bool IsCoreBankSuccess(string? status)
    {
        var normalizedStatus = status?.Trim();
        return string.Equals(normalizedStatus, ACSC, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedStatus, "SUCC", StringComparison.OrdinalIgnoreCase);
    }
}
