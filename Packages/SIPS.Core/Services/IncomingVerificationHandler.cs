using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIPS.Adapter;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Enums;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Services;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Implementations;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
using static SIPS.Core.Constants;

namespace SIPS.Core.Services;

#pragma warning disable CS9113 // Parameter is unread - may be used in future functionality
public sealed class IncomingVerificationHandler(
    ISO20022Options options,
    ILogger<IncomingVerificationHandler> logger,
    INativeSigner signer,
    IJsonAdapter jsonAdapter,
    IPersistenceGateway persistence,
    IPayeeVerificationRequestParser parser,
    ISignatureService signature,
    ICorrelationService correlation,
    ICallbackClient callback,
    IInboundMessageService inbound,
    ICallbackOrchestrator callbacks,
    IISOMessageService isoService,
    IOptions<CoreOptions> coreOptions
) : IIncomingVerificationHandler
#pragma warning restore CS9113
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly ILogger<IncomingVerificationHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IPayeeVerificationRequestParser _parser = parser;
    private readonly ICorrelationService _correlation = correlation;
    private readonly ICallbackClient _callback = callback;
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
    public IncomingVerificationHandler(
        ISO20022Options options,
        ILogger<IncomingVerificationHandler> logger,
        INativeSigner signer,
        IJsonAdapter jsonAdapter,
        IPersistenceGateway persistence,
        IPayeeVerificationRequestParser parser,
        ISignatureService signature,
        ICorrelationService correlation,
        ICallbackClient callback,
        IOptions<CoreOptions> coreOptions)
        : this(options, logger, signer, jsonAdapter, persistence, parser, signature, correlation, callback,
              new InboundMessageService(signature),
              new CallbackOrchestrator(),
              new ISOMessageService(persistence),
              coreOptions)
    {
    }

    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string path = "Normal";
        var cid = _correlation.Create();

        // [CHANGE GUARD]: Internal watchdog budget ensures contractual Compliance with BPC 10s SLA.
        using var globalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        globalCts.CancelAfter(TimeSpan.FromSeconds(_core.CallbackInternalBudgetSeconds > 0 ? _core.CallbackInternalBudgetSeconds : 9));
        var gct = globalCts.Token;

        using var dbCts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
        var dbCt = dbCts.Token;

        // Initialize response object early for emergency responses
        PayeeVerificationResponseBuilder.Request? response = null;
        PayeeVerificationBuilder.Request? request = null;
        ISOMessage? isoMessage = null;

        try
        {
            // Step 1: Verify signature and parse message via helper
            var (isValid, parsedRequest) = await _inbound.VerifyAndParseAsync(
                message,
                (xml) =>
                {
                    if (!_parser.TryParse(xml, out var req)) return (false, (PayeeVerificationBuilder.Request?)null);
                    return (true, req);
                },
                gct,
                cid);
            request = parsedRequest; // Assign to outer scope variable

            if (!isValid || request == null)
            {
                path = "ParseFail";
                // [PROTOCOL COMPLIANCE]: Use admi.002 for protocol-level rejection
                var err = AdminMessageBuilder.Generate(
                    SIPS.ISO20022.Helpers.AdminRejectReasonCodes.InvalidXML, 
                    "Failed to verify the signature or parse the message.", 
                    parsedRequest?.SIPSRequestId);
                return _signer.SignEnvelope(err);
            }

            // Step 2: Validate mandatory MsgId (Critical for verification deduplication - Delta 4)
            if (string.IsNullOrWhiteSpace(request.MsgId))
            {
                path = "MissingMsgId";
                // [PROTOCOL COMPLIANCE]: Mandatory field missing -> admi.002
                var err = AdminMessageBuilder.Generate(
                    SIPS.ISO20022.Helpers.AdminRejectReasonCodes.MandatoryElementMissing,
                    "MsgId is mandatory for VerificationRequest sovereignty.", 
                    request.SIPSRequestId);
                return _signer.SignEnvelope(err);
            }
            
            // Step 3: Record incoming message (INSERT-First)
            var recordResult = await _isoService.TryRecordIncomingVerificationAsync(
                new ISOMessage
                {
                    MessageType = ISOMessageType.VerificationRequest,
                    Date = System.DateTimeOffset.Now.ToUniversalTime(),
                    FromBIC = request.From,
                    ToBIC = request.To,
                    Message = Encoding.UTF8.GetBytes(message),
                    Status = TransactionStatus.Pending,
                    BizMsgIdr = request.BizMsgIdr,
                    MsgDefIdr = request.MsgDefIdr,
                    MsgId = request.MsgId,
                    TxId = request.MsgId,
                    UETR = request.MsgId // Proxy UETR
                }, gct);

            isoMessage = recordResult.record;
            var outcome = recordResult.outcome;
            var duplicateBy = recordResult.duplicateBy;

            // [GOLD PATTERN]: Audit Owner Claim
            if (outcome == SIPS.PostgreSQL.Enums.DedupOutcome.Owner)
            {
                await _isoService.AppendAuditLedgerEventAsync(isoMessage.Id, new {
                    @event = "VerificationOwnerClaimed",
                    timestampUtc = DateTimeOffset.UtcNow,
                    correlationId = cid
                }, gct);
            }
            else
            {
                // [LOOPBACK DETECTION]: Fix for self-addressed messages (e.g. simulation/loopback) failing with DuplicatePending
                if (request.From == request.To && isoMessage.Status == TransactionStatus.Pending && (isoMessage.Response == null || isoMessage.Response.Length == 0))
                {
                     _logger.LogInformation("[{CorrelationId}] [LOOPBACK_DETECTED] MsgId={MsgId} is self-addressed. Taking ownership of pending Outgoing record.", cid, request.MsgId);
                     await _isoService.AppendAuditLedgerEventAsync(isoMessage.Id, new { 
                         @event = "VerificationLoopbackDetected", 
                         status = "OwnershipTaken",
                         timestampUtc = DateTimeOffset.UtcNow 
                     }, gct);
                     goto ProcessAsOwner;
                }

                // [GOLD PATTERN]: Follower Logic
                path = "DuplicateFollower";
                
                await _isoService.AppendAuditLedgerEventAsync(isoMessage.Id, new {
                    @event = "VerificationFollowerDuplicate",
                    duplicateBy = duplicateBy,
                    timestampUtc = DateTimeOffset.UtcNow,
                    correlationId = cid
                }, gct);

                // REPLAY: If response exists, return immediately
                if (isoMessage.Response != null && isoMessage.Response.Length > 0)
                {
                    _logger.LogInformation("[{CorrelationId}] [REPLAY] Found existing response for MsgId={MsgId}. Replaying.", cid, request.MsgId);
                    await _isoService.AppendAuditLedgerEventAsync(isoMessage.Id, new { @event = "VerificationReplayStored", timestampUtc = DateTimeOffset.UtcNow }, gct);
                    // Ensure the response is signed if it wasn't stored signed (defensive)
                    // But assuming stored response is the final signed XML
                    return Encoding.UTF8.GetString(isoMessage.Response);
                }

                // WAIT: If Pending, wait 500ms and retry
                if (isoMessage.Status == TransactionStatus.Pending || isoMessage.Status == TransactionStatus.CheckStatus)
                {
                    _logger.LogInformation("[{CorrelationId}] [WAIT] MsgId={MsgId} is Pending. Waiting 500ms.", cid, request.MsgId);
                    await Task.Delay(500, gct);
                    
                    // Re-fetch
                    // Use persistence directly or service method if available. 
                    // Since TryRecordIncomingVerificationAsync doesn't support re-fetch by ID easily without leaking persistence logic, 
                    // we re-run TryRecord... which will act as a Get effectively or use GetInboundMessageByTxIdAsync if TxId matched.
                    // But here we matched by MsgId usually. 
                    // Let's use the object we have if EF tracks it, but we need a fresh fetch to see DB updates from Owner.
                    // We can't access GetByMsgIdAsync directly. 
                    // We will rely on re-calling TryRecordIncomingVerificationAsync which does a fresh fetch on conflict.
                    
                     var retryResult = await _isoService.TryRecordIncomingVerificationAsync(
                        new ISOMessage
                        {
                            MessageType = ISOMessageType.VerificationRequest,
                            MsgId = request.MsgId,
                            TxId = request.MsgId
                        }, gct);
                    
                    var freshRecord = retryResult.record;
                    if (freshRecord.Response != null && freshRecord.Response.Length > 0)
                    {
                         _logger.LogInformation("[{CorrelationId}] [REPLAY-AFTER-WAIT] Found response for MsgId={MsgId}.", cid, request.MsgId);
                        return Encoding.UTF8.GetString(freshRecord.Response);
                    }
                    
                    // Still pending -> Reject
                     path = "DuplicatePending";
                     _logger.LogWarning("[{CorrelationId}] [DUPLICATE-PENDING] MsgId={MsgId} still pending after wait.", cid, request.MsgId);
                     
                     await _isoService.AppendAuditLedgerEventAsync(isoMessage.Id, new { 
                         @event = "VerificationDuplicateInProcessRejected", 
                         reason = "DuplicateMessageInProcess",
                         timestampUtc = DateTimeOffset.UtcNow 
                     }, gct);

                     // [PROTOCOL COMPLIANCE]: admi.002 for Duplicate Pending (not acmt.024)
                     var pendingErr = AdminMessageBuilder.Generate(
                        SIPS.ISO20022.Helpers.AdminRejectReasonCodes.DuplicateMessageInProcess,
                        "Duplicate message processing in progress. Please retry later.", 
                        request.SIPSRequestId);
                     return _signer.SignEnvelope(pendingErr);
                }
            }

            // --- OWNER PATH ---
            ProcessAsOwner:

            // Step 4: Prepare internal response state
            response = new PayeeVerificationResponseBuilder.Request
            {
                Original = request,
                VerificationId = request.SIPSRequestId ?? string.Empty,
                From = request.From,
                To = request.To,
                Type = request.Type
            };

            // Step 5: Send callback and parse result via orchestrator
            var headers = new Dictionary<string, string>() {
                { API_Key, _callbackLinks.Key! },
                { API_Secret, _callbackLinks.Secret! }
            };
            if (!string.IsNullOrWhiteSpace(request.SIPSRequestId))
                headers["X-Idempotency-Key"] = request.SIPSRequestId!;
            // Normalize alias and type prior to CoreBank matching
            var normalizedAlias = request.Alias ?? string.Empty;
            var normalizedType = request.Type ?? string.Empty;

            // [BUSINESS COMPLIANCE]: Strip legacy 'USD:' prefix and auto-detect IBAN for Somalia ISO standards.
            if (normalizedAlias.StartsWith("USD:", StringComparison.OrdinalIgnoreCase))
            {
                normalizedAlias = normalizedAlias.Substring(4);
            }
            if (normalizedAlias.StartsWith("SO", StringComparison.OrdinalIgnoreCase))
            {
                normalizedType = "IBAN";
            }

            var dto = new CBVerificationRequestDto
            {
                Alias = normalizedAlias,
                Type = normalizedType,
                FromBIC = request.From,
                VerificationId = request.SIPSRequestId!
            };

            // Create a bounded cancellation token for CoreBank callback
            using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(gct);
            coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));

            var responseMessage = await _callbacks.SendJsonAsync(
                _callbackLinks.Verification!,
                headers,
                dto,
                CB_VerificationRequest,
                _jsonAdapter,
                _correlation,
                _jsonSerializerOptions,
                _callback,
                coreBankCts.Token, // Use bounded token
                cid);

            _logger.LogInformation("[IncomingVerificationHandler] Callback for ReqId={ReqId} returned StatusCode={StatusCode}", request.SIPSRequestId, responseMessage?.StatusCode);
            if (responseMessage != null && responseMessage.StatusCode == HttpStatusCode.OK && responseMessage.Data != null)
            {
                ParseCallbackResult(responseMessage.Data, response);
            }
            else
            {
                path = responseMessage?.StatusCode == HttpStatusCode.RequestTimeout ? "CoreBankTimeout" : "CoreBankError";
                _logger.LogWarning("[IncomingVerificationHandler] Callback failed or returned non-200. Marking verified=false. StatusCode={StatusCode}", responseMessage?.StatusCode);
                response.Verified = false;
                response.Reason = MISS;
                response.AdditionalInfo = responseMessage?.Message ?? "CoreBank verification failed or timed out.";
            }

            // Step 6: Build, persist, and sign response via helper
            // [PROTOCOL COMPLIANCE]: Business response is always acmt.024
            var rsp = PayeeVerificationResponseBuilder.Build(response);
            
            // Sign BEFORE persist to ensure we store the exact bytes we send? 
            // Better to persist the XML, then sign right before return, OR persist signed XML.
            // The existing pattern helper `PersistResponseAsync` takes `string responseXml`.
            // Let's sign it first to be safe and consistent with "Store what you send".
            var signedRsp = _signer.SignEnvelope(rsp);

            var finalStatus = path == "CoreBankTimeout" ? TransactionStatus.CheckStatus : (response.Verified ? TransactionStatus.Success : TransactionStatus.Failed);
            
            // [GOLD PATTERN]: Persist-Before-Return
            await _isoService.PersistResponseAsync(isoMessage!, finalStatus, response.Reason, response.AdditionalInfo, signedRsp, dbCt);
            
            await _isoService.AppendAuditLedgerEventAsync(isoMessage!.Id, new { 
                @event = "VerificationBusinessResponsePersisted", 
                status = finalStatus.ToString(),
                timestampUtc = DateTimeOffset.UtcNow 
            }, gct);

            return FinalizeResponse(signedRsp, cid, sw, path);
        }
        catch (TaskCanceledException ex) when (!gct.IsCancellationRequested)
        {
            path = "CoreBankTimeout";
            _logger.LogWarning(ex, "[{CorrelationId}] [MANUAL_RECONCILIATION_REQUIRED:POTENTIAL_PHANTOM_CREDIT] CoreBank callback timed out for Verification ReqId={ReqId}. Raising CheckStatus for audit trail.", cid, request?.SIPSRequestId);
            
            response ??= new PayeeVerificationResponseBuilder.Request
            {
                Original = request,
                VerificationId = request?.SIPSRequestId ?? string.Empty,
                From = request?.From ?? string.Empty,
                To = request?.To ?? string.Empty,
                Type = request?.Type ?? string.Empty
            };
            response.Verified = false;
            response.Reason = MISS;
            response.AdditionalInfo = "CoreBank response exceeded internal SLA.";
            
            // [AUDIT-GRADE INTEGRITY]: Record "in-doubt" state even for verification
            var ev = new
            {
                schemaVersion = 1,
                eventId = Guid.NewGuid(),
                actor = "System",
                @event = "CheckStatusRaised",
                reason = "CoreBankTimeout",
                timestampUtc = DateTimeOffset.UtcNow,
                slaContext = new { elapsedMs = sw.ElapsedMilliseconds, isoPath = path },
                correlation = new { msgId = request?.SIPSRequestId, alias = request?.Alias },
                coreBank = new { idempotencyKey = request?.SIPSRequestId, timeoutSeconds = _core.CoreBankTimeoutSeconds },
                reconciliationState = "Open"
            };

            if (isoMessage != null)
            {
                await _isoService.AppendAuditLedgerEventAsync(isoMessage.Id, ev, gct);
            }

            var rsp = PayeeVerificationResponseBuilder.Build(response);
            var signedRsp = _signer.SignEnvelope(rsp);
            
            try {
                using var dbCts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                if (isoMessage != null)
                {
                    await _isoService.PersistResponseAsync(isoMessage, TransactionStatus.CheckStatus, response.Reason, response.AdditionalInfo, signedRsp, dbCts2.Token);
                }
            } catch { }

            return FinalizeResponse(signedRsp, cid, sw, path);
        }
        catch (Exception ex)
        {
            path = path == "Normal" ? (request == null ? "EmergencyCatch" : "DbFail") : path;
            _logger.LogError(ex, "[{CorrelationId}] Failed to verify payee for ReqId={ReqId}. Path={Path}. Returning signed ISO response for SLA compliance.", cid, request?.SIPSRequestId, path);
            
            // [PROTOCOL COMPLIANCE]: Technical/Emergency error -> admi.002
            var err = AdminMessageBuilder.Generate(
                SIPS.ISO20022.Helpers.AdminRejectReasonCodes.TechnicalError,
                "Internal Error during verification processing.", 
                request?.SIPSRequestId);
            return _signer.SignEnvelope(err);
        }
    }

    private string FinalizeResponse(string isoBody, string cid, System.Diagnostics.Stopwatch sw, string path)
    {
        long elapsedMs = sw.ElapsedMilliseconds;
        long remainingSlaMs = (_core.CallbackSlaSeconds * 1000) - elapsedMs;

        _logger.LogInformation("[{CorrelationId}] [METRIC:ISO_PATH={Path}] Processing complete. Elapsed={ElapsedMs}ms, RemainingSLA={RemainingSlaMs}ms", cid, path, elapsedMs, remainingSlaMs);

        try
        {
            return _signer.SignEnvelope(isoBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] [METRIC:ISO_SIGNING_FAILED] Failed to sign ISO response. Returning unsigned body for SLA compliance.", cid);
            return isoBody; // Signing-safe fallback
        }
    }

    private static PayeeVerificationResponseBuilder.Request BuildInitialResponse(PayeeVerificationBuilder.Request request)
    {
        return new PayeeVerificationResponseBuilder.Request
        {
            From = request.To,
            To = request.From,
            MsgDefIdr = request.MsgDefIdr,
            BizMsgIdr = request.BizMsgIdr,
            MsgId = request.MsgId,
            CreDt = request.CreDt,
            Original = request,
            Reason = MISS,
            Verified = false
        };
    }


    private void ParseCallbackResult(JsonObject data, PayeeVerificationResponseBuilder.Request response)
    {
        _logger.LogInformation("Callback Response: {Response}", data.ToJsonString(_jsonSerializerOptions));

        // First, transform the raw callback payload using our configured mapping
        // so fields like accountNo/accountType map to Id/Type regardless of casing.
        JsonObject mapped;
        try
        {
            mapped = _jsonAdapter.Transform(data, CB_VerificationResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to transform callback payload using mapping '{MappingKey}'. Falling back to raw payload.", CB_VerificationResponse);
            mapped = data;
        }

        // Deserialize into our DTO with case-insensitive property matching
        var deserializedContent = _jsonAdapter.ToObject<VerificationResponseDto>(mapped);
        _logger.LogInformation("[ParseCallbackResult] Verification result for {Alias}: Verified={Verified}", response.Original?.Alias, deserializedContent?.IsVerified);

        response.Verified = deserializedContent?.IsVerified ?? false;
        response.Reason = response.Verified ? SUCC : MISS;
        response.Id = deserializedContent?.Id ?? string.Empty;
        response.Type = IBAN;
        response.Name = deserializedContent?.Name ?? string.Empty;
        response.Address = deserializedContent?.Address ?? string.Empty;
        response.Currency = deserializedContent?.Currency ?? string.Empty;
    }


}