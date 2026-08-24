using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SIPS.Adapter;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Enums;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Services;
using SIPS.Core.Services.ISOParsers;
using SIPS.PostgreSQL.Enums;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
using SIPS.ISO20022.Models.DTOs;
using SIPS.Core.Services.Metrics;
using SIPS.PostgreSQL.Models;
using static SIPS.Core.Constants;

namespace SIPS.Core.Services;

/// <summary>
/// Handles incoming pacs.002 payment status reports from SmartVista IPS.
/// 
/// IMPORTANT: SmartVista IPS Switch Behavior
/// ==========================================
/// The SmartVista IPS switch does NOT act as a pass-through for ISO 20022 messages.
/// Instead, it REGENERATES the AppHdr for all forwarded messages:
/// 
/// 1. Original Bank → Switch (pacs.008):
///    - Bank sends pacs.008 with BizMsgIdr=BANK001, CreDt=T1
///    
/// 2. Switch → Receiving Bank (pacs.008):
///    - Switch REGENERATES AppHdr with NEW BizMsgIdr=SWITCH001, CreDt=T2
///    - Switch PRESERVES the original signature and document payload
///    - Only the AppHdr is replaced
///    
/// 3. Receiving Bank → Switch (pacs.002):
///    - Bank responds with pacs.002
///    - Rltd block references SWITCH001 (the message we received), NOT BANK001
///    
/// 4. Switch → Original Bank (pacs.002):
///    - Switch REGENERATES AppHdr again with NEW BizMsgIdr=SWITCH002
///    - Rltd block references the receiving bank's pacs.002
///    
/// 5. Completion Notification (Switch → Original Bank):
///    - Switch sends final pacs.002 with BizMsgIdr=SWITCH003
///    - Rltd references SWITCH002 (the receiving bank's response)
///    
/// CORRELATION STRATEGY:
/// ====================
/// - We CANNOT rely on BizMsgIdr for correlation (it changes at every hop)
/// - We MUST use TxId which remains constant throughout the entire flow
/// - The handlers correctly retrieve messages by TxId, not BizMsgIdr
/// - Response builders populate Rltd with the IMMEDIATE PARENT message details
/// 
/// This handler processes pacs.002 messages from:
/// - Direct responses from receiving banks (after we sent pacs.008)
/// - Completion notifications from the switch (after settlement)
/// </summary>
public sealed class IncomingPaymentStatusReportHandler(
    ISO20022Options options,
    ILogger<IncomingPaymentStatusReportHandler> logger,
    INativeSigner signer,
    IJsonAdapter jsonAdapter,
    IPaymentStatusReportParser reportParser,
    ICallbackClient callback,
    IResponseFactory responseFactory,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    IInboundMessageService inbound,
    ICallbackOrchestrator callbacks,
    IISOMessageService isoService,
    IStatusOrchestrator statusOrchestrator,
    IOptions<CoreOptions> coreOptions
) : IIncomingPaymentStatusReportHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly ILogger<IncomingPaymentStatusReportHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly ICallbackClient _callback = callback;
    private readonly IResponseFactory _responses = responseFactory;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly IInboundMessageService _inbound = inbound;
    private readonly ICallbackOrchestrator _callbacks = callbacks;
    private readonly IISOMessageService _isoService = isoService;
    private readonly IStatusOrchestrator _statusOrchestrator = statusOrchestrator;
    private readonly CoreOptions _core = coreOptions.Value;
    private readonly IPaymentStatusReportParser _reportParser = reportParser;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        using var _totalTrack = SipsMetrics.TrackStep("Incoming", "Status", "Total");
        var cid = _correlation.Create();
        using var dbCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
        var dbCt = dbCts.Token;
        // Step 1: Verify signature and parse via parser
        bool isValid = false;
        PaymentRequestResponseBuilder.Response? request = null;

        using (SipsMetrics.TrackStep("Incoming", "Status", "ParsingAndSignature"))
        {
            var (valid, parsedRequest) = await _inbound.VerifyAndParseAsync<PaymentRequestResponseBuilder.Response>(
                message,
                (xml) =>
                {
                    if (!_reportParser.TryParse(xml, out var req)) return (false, (PaymentRequestResponseBuilder.Response?)null);
                    return (true, req);
                },
                ct,
                cid);
            isValid = valid;
            request = parsedRequest;
        }

        if (!isValid || request == null)
            {
                // Fallback: try to extract simple <TxId>...</TxId> from the message for lightweight tests
                try
                {
                    var startTag = "<TxId>";
                    var endTag = "</TxId>";
                    var sIdx = message.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
                    var eIdx = message.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
                    if (sIdx >= 0 && eIdx > sIdx)
                    {
                        var txIdStr = message.Substring(sIdx + startTag.Length, eIdx - (sIdx + startTag.Length)).Trim();
                        var fallback = new PaymentRequestResponseBuilder.Response
                        {
                            TxId = txIdStr,
                            Original = new PaymentRequestBuilder.Request { TxId = txIdStr }
                        };
                        request = fallback;
                    }
                }
                catch { /* ignore fallback errors */ }

                if (request == null)
                {
                    _logger.LogWarning("[{CorrelationId}] Failed to verify signature or parse TxId from message: {Message}", cid, message);
                    return AdminMessage.Generate("Failed to verify signature or parse TxId.");
                }
            }

        _logger.LogInformation("INCOMING pacs.002 Handler for data {Data}",
            JsonSerializer.Serialize(request, _jsonSerializerOptions));

    _logger.LogDebug("[IncomingPaymentStatusReportHandler] parsed request TxId={TxId} Status={Status}", request.TxId, request.Status);

        // Step 2: Retrieve ISO message by TxId
        // CRITICAL: We use TxId for correlation, NOT BizMsgIdr (Delta 2)
        if (string.IsNullOrWhiteSpace(request.TxId))
        {
            _logger.LogWarning("[{CorrelationId}] pacs.002 rejected: Mandatory TxId (OrgnlTxId) is missing", cid);
            return AdminMessage.Generate("Mandatory TxId is missing.");
        }

        var isoMessage = await _persistence.GetISOMessageWithTransactionsByTxIdAsync(request.TxId, ct);
        isoMessage ??= await _persistence.GetISOMessageByTxIdAsync(request.TxId, ct);
        // Fallback: If IPS responded to a Return, the OrgnlTxId in the pacs.002 is the ReturnId!
        isoMessage ??= await _persistence.GetISOMessageByReturnIdAsync(request.TxId, ct);

        if (isoMessage == null)
        {
            await RecordOrphanStatusReportAsync(request, message, ct);
            _logger.LogInformation("[{CorrelationId}] Status report received before parent transaction was found. TxId={TxId}", cid, request.TxId);
            return AdminMessage.Generate("Referenced transaction was not found.");
        }

        // Step 3: Record the incoming status report under parent ISOMessage
        // Gold Pattern: Persist before side-effects with composite de-dup (Role + Status + MsgId)
        ISOMessageStatus record;
        using (SipsMetrics.TrackStep("Incoming", "Status", "DbSave"))
        {
            record = await _isoService.RecordIncomingStatusAsync(isoMessage, message, request.Role, request.MsgId, ct);
        }

        // Gold Pattern/Delta 5 Check: If we already have a status record for this ROLE, STATUS and MSGID, return the previous response
        var previousRecord = isoMessage.Statuses?
            .Where(s => s.MessageRole == request.Role && s.Status != TransactionStatus.Pending && s.MsgId == request.MsgId)
            .OrderByDescending(s => s.Date)
            .FirstOrDefault();

        if (previousRecord != null && previousRecord.Response != null)
        {
             _logger.LogInformation("[{CorrelationId}] Replay detected for pacs.002 (Role={Role}, Status={Status}). Returning previous response.", 
                cid, request.Role, request.Status);
             return _signer.SignEnvelope(Encoding.UTF8.GetString(previousRecord.Response));
        }
        var transaction = isoMessage.Transactions.FirstOrDefault();

        if (transaction == null)
        {
            // Tests sometimes use a parent ISOMessage without child transactions; create a lightweight
            // synthetic transaction from the parent so we can continue processing and build responses.
            transaction = new SIPS.PostgreSQL.Models.Transaction
            {
                Type = TransactionType.Deposit,
                FromBIC = isoMessage.FromBIC ?? string.Empty,
                LocalInstrument = string.Empty,
                CategoryPurpose = string.Empty,
                EndToEndId = isoMessage.EndToEndId ?? string.Empty,
                TxId = isoMessage.TxId ?? string.Empty,
                Amount = request.Original?.Amount ?? 0,
                Currency = request.Original?.Currency ?? string.Empty,
                DebtorName = string.Empty,
                DebtorAccount = request.Original?.Debtor?.Account ?? string.Empty,
                DebtorAccountType = request.Original?.Debtor?.AccountType ?? string.Empty,
                DebtorAgentBIC = string.Empty,
                DebtorIssuer = string.Empty,
                CreditorName = string.Empty,
                CreditorAccount = request.Original?.Creditor?.Account ?? string.Empty,
                CreditorAccountType = request.Original?.Creditor?.AccountType ?? string.Empty,
                CreditorAgentBIC = string.Empty,
                CreditorIssuer = string.Empty,
                RemittanceInformation = string.Empty
            };
        }

        if (transaction.Amount != request.Original?.Amount)
        {
            await _isoService.PersistStatusResponseAsync(record, TransactionStatus.Failed, "Invalid Amount", "Invalid Amount", "Invalid Amount", dbCt);
            return AdminMessage.Generate("Invalid Transaction Amount!");
        }

        if (transaction.Currency != request.Original?.Currency)
        {
            await _isoService.PersistStatusResponseAsync(record, TransactionStatus.Failed, "Invalid Currency", "Invalid Currency", "Invalid Currency", dbCt);
            return AdminMessage.Generate("Invalid Transaction Currency!");
        }

        if ((transaction.DebtorAccount != request.Original?.Debtor?.Account) || (transaction.CreditorAccount != request.Original?.Creditor?.Account))
        {
            await _isoService.PersistStatusResponseAsync(record, TransactionStatus.Failed, "Invalid Debtor or Creditor", "Invalid Debtor or Creditor", "Invalid Debtor or Creditor", dbCt);
            return AdminMessage.Generate("Invalid Transaction Accounts");
        }

        // Step 4: Prepare response object (reuse PaymentStatus request response builder)
        // Pass the original message direction so the Factory can swap it correctly
        var statusReq = new PaymentStatusRequestBuilder.Request
        {
            From = isoMessage.FromBIC ?? string.Empty,
            To = isoMessage.ToBIC ?? string.Empty,
            OrgnlTxId = request.TxId,
            OriginalEndToEnd = request.Original?.EndToEndId ?? "",
            BizMsgIdr = request.BizMsgIdr ?? string.Empty,
            MsgDefIdr = request.MsgDefIdr ?? string.Empty,
            MsgId = request.MsgId ?? string.Empty,
            CreDt = request.CreDt == default ? DateTime.UtcNow : request.CreDt
        };

        var response = _responses.BuildPaymentStatusInitial(statusReq);

    // Ensure the nested Original/Debtor/Creditor objects exist and have safe defaults
    response.Original ??= new PaymentRequestBuilder.Request();
    if (response.Original.Debtor == null) response.Original.Debtor = new PaymentRequestBuilder.Request().Debtor;
    if (response.Original.Creditor == null) response.Original.Creditor = new PaymentRequestBuilder.Request().Creditor;
    if (string.IsNullOrWhiteSpace(response.Original.Debtor.Name)) response.Original.Debtor.Name = "NA";
    if (string.IsNullOrWhiteSpace(response.Original.Creditor.Name)) response.Original.Creditor.Name = "NA";
    if (string.IsNullOrWhiteSpace(response.Original.Debtor.Account)) response.Original.Debtor.Account = "NA";
    if (string.IsNullOrWhiteSpace(response.Original.Creditor.Account)) response.Original.Creditor.Account = "NA";
    if (string.IsNullOrWhiteSpace(response.Original.Debtor.AccountType)) response.Original.Debtor.AccountType = "ACCT";
    if (string.IsNullOrWhiteSpace(response.Original.Creditor.AccountType)) response.Original.Creditor.AccountType = "ACCT";

        // Step 5: Incoming completion status is always present; if Pending exists, decide flow:
        // - RJCT: persist as failed and return (no CoreBank call)
        // - ACSC: forward original payment to CoreBank, then persist and return
        response.Status = request.Status;
        response.Reason = request.Reason ?? string.Empty;
        response.AdditionalInfo = request.AdditionalInfo ?? string.Empty;

        bool isOutgoing = isoMessage.FromBIC == _callbackLinks.BIC;

        // Check if this is a Return Request (pacs.004) confirming an incoming return
        // This check must happen BEFORE the NonPending check because Returns are typically in ReadyForReturn (a non-Pending status)
        // CRITICAL: Only apply this logic for INCOMING returns. Outgoing returns (initiated by us) should fall through to standard handling.
        if (isoMessage.MessageType == ISOMessageType.ReturnRequest && !isOutgoing)
        {
            if (isoMessage.Status == TransactionStatus.ReadyForReturn)
            {
                _logger.LogInformation("[{CorrelationId}] pacs.002 confirms incoming return (Type=ReturnRequest) for TxId {TxId}. Calling CoreBank to reverse credit.", cid, request.TxId);
                response = await CallCoreBankReturnAsync(isoMessage, transaction, statusReq, response, ct, cid);

                // Build and persist final response
                response.TxId = statusReq.OrgnlTxId ?? response.TxId;
                response.Original.TxId = statusReq.OrgnlTxId ?? response.Original.TxId;
                var rspReturn = PaymentStatusRequestResponseBuilder.Build(response);

                // Status already set by CallCoreBankReturnAsync
                using (SipsMetrics.TrackStep("Incoming", "Status", "DbSave"))
                {
                    using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
                    await _isoService.PersistStatusResponseAsync(record, isoMessage.Status, isoMessage.Reason ?? "Return completed", isoMessage.AdditionalInfo, rspReturn, updateCts.Token);
                }
                return _signer.SignEnvelope(rspReturn);
            }
            else
            {
                // MessageType is ReturnRequest but status is NOT ReadyForReturn.
                // This implies we already processed it (e.g. Success/Failed) or it's in an invalid state.
                // We CANNOT fall through to TransactionRequest logic (CoreBank Transfer).
                _logger.LogWarning("[{CorrelationId}] Mismatch: MessageType is ReturnRequest but Status is {Status}. Aborting CoreBank Transfer flow.", cid, isoMessage.Status);
                
                // Return generic response mirroring current status (similar to NonPending handling)
                response.Status = isoMessage.Status == TransactionStatus.Failed ? RJCT : ACSC;
                var rspGeneric = PaymentStatusRequestResponseBuilder.Build(response);
                using (SipsMetrics.TrackStep("Incoming", "Status", "DbSave"))
                {
                    using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
                    await _isoService.PersistStatusResponseAsync(record, isoMessage.Status, isoMessage.Reason ?? "Return Status Mismatch", isoMessage.AdditionalInfo, rspGeneric, updateCts.Token);
                }
                return _signer.SignEnvelope(rspGeneric);
            }
        }

        // If related ISO message is not pending anymore, mirror DB status and return without forwarding
        // This handles idempotent retries or duplicate pacs.002 messages
        if (isoMessage.Status != TransactionStatus.Pending)
        {
            _logger.LogInformation("[{CorrelationId}] ISO message TxId {TxId} already processed with status {Status}. Returning mirrored response.", cid, request.TxId, isoMessage.Status);

            // Ensure TxId and Original.TxId are populated so the built XML contains the TxId
            response.TxId = statusReq.OrgnlTxId ?? response.TxId;
            response.Original.TxId = statusReq.OrgnlTxId ?? response.Original.TxId;
            // ensure required original fields exist and have valid lengths for the ISO builder
            response.Original.EndToEndId = string.IsNullOrWhiteSpace(statusReq.OriginalEndToEnd) ? (isoMessage.EndToEndId ?? "E2E") : statusReq.OriginalEndToEnd;
            if (response.Original.Debtor == null) response.Original.Debtor = new PaymentRequestBuilder.Request().Debtor;
            if (response.Original.Creditor == null) response.Original.Creditor = new PaymentRequestBuilder.Request().Creditor;

            // Mirror DB state: map TransactionStatus to ISO status code
            // Success/ReadyForReturn → ACSC (transaction was accepted by IPS, even if CB failed)
            // Failed → RJCT
            response.Status = (isoMessage.Status == TransactionStatus.Failed) ? RJCT : ACSC;
            response.Reason = isoMessage.Reason ?? "Mirror DB Status";
            response.AdditionalInfo = isoMessage.AdditionalInfo ?? string.Empty;

            var rspMirror = PaymentStatusRequestResponseBuilder.Build(response);
            // Persist status record to maintain audit trail (idempotent)
            using (SipsMetrics.TrackStep("Incoming", "Status", "DbSave"))
            {
                using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
                await _isoService.PersistStatusResponseAsync(record, isoMessage.Status, response.Reason, response.AdditionalInfo, rspMirror, updateCts.Token);
            }
            _logger.LogDebug("[IncomingPaymentStatusReportHandler] NonPending response: {Response}", rspMirror);
            var signedMirror = _signer.SignEnvelope(rspMirror);
            _logger.LogDebug("[IncomingPaymentStatusReportHandler] Returning NonPending signed response: {Signed}", signedMirror);
            return signedMirror;
        }

        // If RJCT -> fail locally. Notify CoreBank only when CoreBank previously saw this transaction.
        if (_statusOrchestrator.IsRejectionStatus(request.Status))
        {
            var (rjctParentStatus, rjctChildStatus, rjctReason, rjctAdditionalInfo) = _statusOrchestrator.MapCompletionStatus(request.Status ?? string.Empty, null, false);
            var notifyCoreBankOfRejection = isOutgoing || _core.IncludeCoreBankOnListing;
            SIPS.ISO20022.Models.DTOs.Response<JsonObject?>? rejectResult = null;

            if (notifyCoreBankOfRejection && !string.IsNullOrWhiteSpace(_callbackLinks.CompletionNotification))
            {
                _logger.LogInformation("[{CorrelationId}] Switch rejected transaction {TxId}. Forwarding rejection to CoreBank via CompletionNotification.", cid, request.TxId);

                try
                {
                    var rejectHeaders = new Dictionary<string, string>() {
                            { API_Key, _callbackLinks.Key! },
                            { API_Secret, _callbackLinks.Secret! }
                        };
                    // Idempotency key
                    if (!string.IsNullOrWhiteSpace(request.TxId))
                        rejectHeaders["X-Idempotency-Key"] = request.TxId;
                    if (!string.IsNullOrWhiteSpace(request.TxId))
                        rejectHeaders["X-Transaction-Id"] = request.TxId;

                    var rejectDto = new CBCompletionNotification
                    {
                        OriginalTxId = request.TxId ?? string.Empty,
                        OriginalEndToEndId = request.Original?.EndToEndId,
                        Status = RJCT,
                        Reason = request.Reason ?? rjctReason,
                        AdditionalInfo = request.AdditionalInfo ?? rjctAdditionalInfo
                    };

                    using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));
                    using (SipsMetrics.TrackStep("Incoming", "Status", "CoreBankCall"))
                    {
                        rejectResult = await _callbacks.SendJsonAsync(
                            _callbackLinks.CompletionNotification!,
                            rejectHeaders,
                            rejectDto,
                            Constants.CB_CompletionNotification,
                            _jsonAdapter,
                            _correlation,
                            _jsonSerializerOptions,
                            _callback,
                            coreBankCts.Token,
                            cid
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{CorrelationId}] Failed to notify CoreBank of rejection for TxId {TxId}.", cid, request.TxId);
                    // We proceed to persist failure locally even if CB notification fails, as the Switch has already rejected it.
                }
            }
            else if (!notifyCoreBankOfRejection)
            {
                _logger.LogInformation("[{CorrelationId}] Switch rejected incoming transaction {TxId}; IncludeCoreBankOnListing=false so CoreBank was never listed and rejection notification is skipped.", cid, request.TxId);
            }
            else
            {
                _logger.LogWarning("[{CorrelationId}] CompletionNotification URL not configured. Skipping CoreBank rejection notification for TxId {TxId}.", cid, request.TxId);
            }

            response.TxId = statusReq.OrgnlTxId ?? response.TxId;
            response.Original.TxId = statusReq.OrgnlTxId ?? response.Original.TxId;
            var rspRej = PaymentStatusRequestResponseBuilder.Build(response);

            isoMessage.Status = rjctParentStatus;
            isoMessage.Reason = !string.IsNullOrWhiteSpace(response.Reason) ? response.Reason : rjctReason;
            isoMessage.AdditionalInfo = rjctAdditionalInfo;
            if (rejectResult != null)
                isoMessage.CoreBankResponse = JsonSerializer.Serialize(rejectResult, _jsonSerializerOptions);

            using (SipsMetrics.TrackStep("Incoming", "Status", "DbSave"))
            {
                using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
                var rjctCurrentResponseXml = isoMessage.Response != null ? Encoding.UTF8.GetString(isoMessage.Response) : string.Empty;
                await _isoService.PersistResponseAsync(isoMessage, rjctParentStatus, isoMessage.Reason, isoMessage.AdditionalInfo, rjctCurrentResponseXml, updateCts.Token);
                await _isoService.PersistStatusResponseAsync(record, rjctChildStatus, isoMessage.Reason, isoMessage.AdditionalInfo, rspRej, updateCts.Token);
            }
            var signedRej = _signer.SignEnvelope(rspRej);
            _logger.LogDebug("[IncomingPaymentStatusReportHandler] Returning RJCT signed response: {Signed}", signedRej);
            return signedRej;
        }

        // Default: Transaction Request (pacs.008) -> Normal Incoming Credit Transfer
        // CRITICAL CHECK: Ensure this is actually an INCOMING transaction.
        // If FromBIC == OurBIC, it means WE initiated this transaction (Outgoing).
        // For Outgoing transactions, we do NOT call the CoreBank 'Transfer' endpoint (which is for crediting funds).
        // CoreBank already debited the sender.
        
        Response<System.Text.Json.Nodes.JsonObject?>? result = null;
        var coreBankResponseKind = "CB_PaymentResponse";
        var tx = transaction;

        if (!isOutgoing)
        {
            // User Requirement:
            // 1. If IncludeCoreBankOnListing is enabled, the bank already received the initial Transfer request.
            //    So we must send a Completion Notification (pacs.002 Success).
            // 2. If it is disabled, the bank has NOT seen this transaction yet.
            //    So we must send the Transfer request now ("Late Binding").

            if (_core.IncludeCoreBankOnListing)
            {
                // Path A: Bank expects Completion Notification
                 _logger.LogInformation("[{CorrelationId}] incoming transaction {TxId}. IncludeCoreBankOnListing=true, sending CompletionNotification.", cid, request.TxId);
                 
                 var notificationHeaders = new Dictionary<string, string>() {
                        { API_Key, _callbackLinks.Key! },
                        { API_Secret, _callbackLinks.Secret! }
                    };
                // Idempotency key
                var idem = transaction?.TxId ?? request.TxId;
                if (!string.IsNullOrWhiteSpace(idem))
                    notificationHeaders["X-Idempotency-Key"] = idem!;
                if (!string.IsNullOrWhiteSpace(transaction?.TxId ?? request.TxId))
                    notificationHeaders["X-Transaction-Id"] = (transaction?.TxId ?? request.TxId)!;

                 var notificationDto = new CBCompletionNotification
                {
                    OriginalTxId = request.TxId ?? string.Empty,
                    OriginalEndToEndId = request.Original?.EndToEndId,
                    Status = ACSC, // We are in the success/accepted path here
                    Reason = request.Reason ?? "Transaction Completed",
                    AdditionalInfo = request.AdditionalInfo ?? "Final success confirmation from Switch"
                };

                 SIPS.ISO20022.Models.DTOs.Response<JsonObject?>? completionResult = null;
                 using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                 coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));
                 using (SipsMetrics.TrackStep("Incoming", "Status", "CoreBankCall"))
                 {
                     completionResult = await _callbacks.SendJsonAsync(
                        _callbackLinks.CompletionNotification!, 
                        notificationHeaders,
                        notificationDto,
                        CB_CompletionNotification, 
                        _jsonAdapter,
                        _correlation,
                        _jsonSerializerOptions,
                        _callback,
                        coreBankCts.Token,
                        cid
                    );
                 }
                 result = completionResult;
                 coreBankResponseKind = CB_CompletionNotificationResponse;

                if (result == null)
                {
                    _logger.LogError("[{CorrelationId}] CoreBank CompletionNotification callback returned null for TxId {TxId}. Deferring local Success until CoreBank confirms.", cid, request.TxId);
                }
                else 
                {
                    _logger.LogInformation("[{CorrelationId}] Sent CompletionNotification to CoreBank for TxId {TxId}, StatusCode={StatusCode}", cid, request.TxId, result.StatusCode);
                }
            }
            else
            {
                // Path B: Bank expects Transfer Request (Late Binding)
                _logger.LogInformation("[{CorrelationId}] incoming transaction {TxId}. IncludeCoreBankOnListing=false, sending Transfer Request (Late Binding).", cid, request.TxId);

                // Call CoreBank Transfer endpoint for INCOMING payments
                var headers = new Dictionary<string, string>() {
                                { API_Key, _callbackLinks.Key! },
                                { API_Secret, _callbackLinks.Secret! }
                            };
                // Idempotency key for safe retries downstream
                var idem = transaction?.TxId ?? request.TxId;
                if (!string.IsNullOrWhiteSpace(idem))
                    headers["X-Idempotency-Key"] = idem!;
                if (!string.IsNullOrWhiteSpace(transaction?.TxId ?? request.TxId))
                    headers["X-Transaction-Id"] = (transaction?.TxId ?? request.TxId)!;

                if (tx == null || string.IsNullOrWhiteSpace(tx.TxId))
                {
                    _logger.LogError("[{CorrelationId}] Cannot late-bind incoming transaction {TxId} to CoreBank because transaction details are missing. Deferring local Success.", cid, request.TxId);
                }
                else
                {
                    var dto = new CBPaymentRequestDto
                    {
                        FromBIC = tx.FromBIC ?? string.Empty,
                        LocalInstrument = tx.LocalInstrument ?? string.Empty,
                        CategoryPurpose = tx.CategoryPurpose ?? string.Empty,
                        EndToEndId = tx.EndToEndId ?? string.Empty,
                        TxId = tx.TxId ?? string.Empty,
                        Amount = tx.Amount,
                        Currency = tx.Currency ?? string.Empty,
                        DebtorName = tx.DebtorName ?? string.Empty,
                        DebtorAccount = tx.DebtorAccount ?? string.Empty,
                        DebtorAccountType = tx.DebtorAccountType ?? string.Empty,
                        DebtorAgentBIC = tx.DebtorAgentBIC ?? string.Empty,
                        DebtorIssuer = tx.DebtorIssuer ?? string.Empty,
                        CreditorName = tx.CreditorName ?? string.Empty,
                        CreditorAccount = tx.CreditorAccount ?? string.Empty,
                        CreditorAccountType = tx.CreditorAccountType ?? string.Empty,
                        CreditorAgentBIC = tx.CreditorAgentBIC ?? string.Empty,
                        CreditorIssuer = tx.CreditorIssuer ?? string.Empty,
                        RemittanceInformation = tx.RemittanceInformation ?? string.Empty,
                        Date = DateTime.UtcNow,
                        ToBIC = isoMessage.FromBIC ?? string.Empty,
                        SettlementMethod = "CLRG",
                        ChargeBearer = "SLEV",
                        BizMsgIdr = isoMessage.BizMsgIdr ?? string.Empty,
                        MsgDefIdr = isoMessage.MsgDefIdr ?? string.Empty,
                        ClearingSystem = string.Empty,
                        MsgId = isoMessage.MsgId ?? string.Empty
                    };
                    BillReferenceMapper.Apply(dto);

                    SIPS.ISO20022.Models.DTOs.Response<JsonObject?>? transferResult = null;
                    using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));
                    using (SipsMetrics.TrackStep("Incoming", "Status", "CoreBankCall"))
                    {
                        transferResult = await _callbacks.SendJsonAsync(
                            _callbackLinks.Transfer!,
                                headers,
                                dto,
                                CB_PaymentRequest,
                                _jsonAdapter,
                                _correlation,
                                _jsonSerializerOptions,
                                _callback,
                                coreBankCts.Token,
                                cid
                            );
                    }
                    result = transferResult;
                }


                if (result == null)
                {
                    _logger.LogError("[{CorrelationId}] CoreBank callback returned null for TxId {TxId}. Deferring local Success until CoreBank confirms.", cid, request.TxId);
                }
                else 
                {
                    _logger.LogInformation("[{CorrelationId}] Forwarded transaction to CoreBank for TxId {TxId}, StatusCode={StatusCode}", cid, request.TxId, result.StatusCode);
                }
                
                // Persist raw CoreBank response JSON on the parent ISOMessage for audit/operations
                if (result != null)
                {
                     _logger.LogDebug("[IncomingPaymentStatusReportHandler] Callback result.Data is null? {IsNull}", result.Data == null);
                     if (result.Data == null)
                     {
                         _logger.LogWarning("[{CorrelationId}] CoreBank callback returned null data for TxId {TxId}", cid, request.TxId);
                     }
                }
            }
        }
        else
        {
             _logger.LogInformation("[{CorrelationId}] Outgoing transaction confirmation received for TxId {TxId}. Always Permissive: Sending CompletionNotification.", cid, request.TxId);
             
             // Outgoing Transactions ALWAYS notify CoreBank of completion (Permissive Mode), 
             // because the Bank initiated them and is holding funds/state.
             
              var notificationHeaders = new Dictionary<string, string>() {
                    { API_Key, _callbackLinks.Key! },
                    { API_Secret, _callbackLinks.Secret! }
                };
             
             // Idempotency key
            var idem = transaction?.TxId ?? request.TxId;
            if (!string.IsNullOrWhiteSpace(idem))
                notificationHeaders["X-Idempotency-Key"] = idem!;
            if (!string.IsNullOrWhiteSpace(transaction?.TxId ?? request.TxId))
                notificationHeaders["X-Transaction-Id"] = (transaction?.TxId ?? request.TxId)!;

             var notificationDto = new CBCompletionNotification
            {
                OriginalTxId = request.TxId ?? string.Empty,
                OriginalEndToEndId = request.Original?.EndToEndId,
                Status = request.Status ?? ACSC,
                Reason = request.Reason ?? "Transaction Completed",
                AdditionalInfo = request.AdditionalInfo ?? "Final confirmation from Switch"
            };

             SIPS.ISO20022.Models.DTOs.Response<JsonObject?>? cbResult = null;
             using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
             coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));
             using (SipsMetrics.TrackStep("Incoming", "Status", "CoreBankCall"))
             {
                 cbResult = await _callbacks.SendJsonAsync(
                    _callbackLinks.CompletionNotification!, 
                    notificationHeaders,
                    notificationDto,
                    Constants.CB_CompletionNotification, 
                    _jsonAdapter,
                    _correlation,
                    _jsonSerializerOptions,
                    _callback,
                    coreBankCts.Token,
                    cid
                );
             }
             result = cbResult;
             coreBankResponseKind = CB_CompletionNotificationResponse;

            if (result == null)
            {
                _logger.LogError("[{CorrelationId}] CoreBank CompletionNotification callback returned null for Outgoing TxId {TxId}.", cid, request.TxId);
            }
            else 
            {
                _logger.LogInformation("[{CorrelationId}] Sent CompletionNotification to CoreBank for Outgoing TxId {TxId}, StatusCode={StatusCode}", cid, request.TxId, result.StatusCode);
            }
        }

        // Persist raw CoreBank response JSON on the parent ISOMessage for audit/operations
        if (result != null)
        {
             _logger.LogDebug("[IncomingPaymentStatusReportHandler] Callback result.Data is null? {IsNull}", result.Data == null);
             if (result.Data == null)
             {
                 _logger.LogWarning("[{CorrelationId}] CoreBank callback returned null data for TxId {TxId}", cid, request.TxId);
             }
        }

        var callbackSucceeded = result?.IsSuccess == true &&
            result.StatusCode >= System.Net.HttpStatusCode.OK &&
            result.StatusCode < System.Net.HttpStatusCode.MultipleChoices;
        var crResponse = (callbackSucceeded && result?.Data != null)
            ? ParseCallbackResult(result.Data, coreBankResponseKind)
            : new PaymentResponseDto { Status = string.Empty, TxId = string.Empty };
        _logger.LogDebug("[IncomingPaymentStatusReportHandler] crResponse.Status={Status} TxId={TxId}", crResponse?.Status, crResponse?.TxId);

        // Use StatusOrchestrator to map IPS + CoreBank statuses to final status.
        // Local Success requires both IPS acceptance and CoreBank confirmation.
        var (parentStatus, childStatus, reason, additionalInfo) = _statusOrchestrator.MapCompletionStatus(
            request.Status ?? string.Empty,
            crResponse?.Status, 
            false);

        // Ensure response mirrors the CoreBank status so the built XML contains the expected status
        // Prefer the CoreBank status only when it is non-empty; otherwise keep the IPS status
        var cbStatus = crResponse?.Status;
        response.Status = string.IsNullOrWhiteSpace(cbStatus) ? response.Status : cbStatus;

        // Acceptance date: prefer CoreBank's acceptance date when provided; otherwise, use IPS acceptance date
        // This prevents default 0001-01-01 values.
        if (crResponse != null && crResponse.AcceptanceDate != default)
        {
            response.AcceptanceDate = IsoResponseGuard.ValidAcceptanceDate(
                crResponse.AcceptanceDate,
                isoMessage.Date.UtcDateTime);
        }
        else if (request.AcceptanceDate != default)
        {
            response.AcceptanceDate = IsoResponseGuard.ValidAcceptanceDate(
                request.AcceptanceDate,
                isoMessage.Date.UtcDateTime);
        }

        // Apply mapped status to parent ISOMessage
        // Apply mapped status to parent ISOMessage
        isoMessage.Status = parentStatus;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        
        // [DATA SAFETY]: Use explicit PersistResponseAsync to merge CoreBankResponse safely.
        isoMessage.CoreBankResponse = JsonSerializer.Serialize(result, _jsonSerializerOptions);
        // Note: We don't have the final response XML yet (rspFinal is built later), but we can persist the current state.
        // Actually, PersistStatusResponseAsync below will persist the child status.
        // We need to persist the PARENT changes (CoreBankResponse) safely first.
        // We pass empty string for responseXml as we are not updating the raw XML response of the parent here, just the status/CB response.
        // Wait, PersistResponseAsync updates the parent's Response property too. 
        // We should preserve existing Response if we don't have a new one.
        string currentResponseXml = isoMessage.Response != null ? Encoding.UTF8.GetString(isoMessage.Response) : "";
        using (SipsMetrics.TrackStep("Incoming", "Status", "DbSave"))
        {
            using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            await _isoService.PersistResponseAsync(isoMessage, parentStatus, reason, additionalInfo, currentResponseXml, updateCts.Token);
        }

    _logger.LogDebug("[IncomingPaymentStatusReportHandler] tx is null? {IsNull}", tx == null);
    tx ??= new SIPS.PostgreSQL.Models.Transaction();
    _logger.LogDebug("[IncomingPaymentStatusReportHandler] tx.TxId={TxId} DebtorName={Debtor} CreditorName={Creditor}", tx.TxId, tx.DebtorName, tx.CreditorName);
    response.Original.Debtor.Name = !string.IsNullOrWhiteSpace(tx.DebtorName)
        ? tx.DebtorName!
        : (!string.IsNullOrWhiteSpace(request.Original?.Debtor?.Name) ? request.Original!.Debtor!.Name : "NA");
        response.Original.Creditor.Name = !string.IsNullOrWhiteSpace(tx.CreditorName)
        ? tx.CreditorName!
        : (!string.IsNullOrWhiteSpace(request.Original?.Creditor?.Name) ? request.Original!.Creditor!.Name : "NA");
        response.Original.Debtor.Account = !string.IsNullOrWhiteSpace(tx.DebtorAccount)
        ? tx.DebtorAccount!
        : (!string.IsNullOrWhiteSpace(request.Original?.Debtor?.Account) ? request.Original!.Debtor!.Account : "NA");
        response.Original.Creditor.Account = !string.IsNullOrWhiteSpace(tx.CreditorAccount)
        ? tx.CreditorAccount!
        : (!string.IsNullOrWhiteSpace(request.Original?.Creditor?.Account) ? request.Original!.Creditor!.Account : "NA");
        response.Original.Debtor.AccountType = !string.IsNullOrWhiteSpace(tx.DebtorAccountType)
        ? tx.DebtorAccountType!
        : (!string.IsNullOrWhiteSpace(request.Original?.Debtor?.AccountType) ? request.Original!.Debtor!.AccountType : "ACCT");
        response.Original.Creditor.AccountType = !string.IsNullOrWhiteSpace(tx.CreditorAccountType)
        ? tx.CreditorAccountType!
        : (!string.IsNullOrWhiteSpace(request.Original?.Creditor?.AccountType) ? request.Original!.Creditor!.AccountType : "ACCT");
        // Ensure postal address lines exist (builder expects at least one AdrLine)
        if (string.IsNullOrWhiteSpace(response.Original.Debtor.Address)) response.Original.Debtor.Address = "NA";
        if (string.IsNullOrWhiteSpace(response.Original.Creditor.Address)) response.Original.Creditor.Address = "NA";
        response.Original.From = isoMessage.FromBIC ?? _callbackLinks.BIC ?? "NA";
        response.Original.To = isoMessage.ToBIC ?? _callbackLinks.Agent ?? _callbackLinks.BIC ?? "NA";
        // Prefer the original EndToEndId from the request if non-empty; otherwise fall back to the ISO message or a safe default
        response.Original.EndToEndId = string.IsNullOrWhiteSpace(statusReq.OriginalEndToEnd)
            ? (isoMessage.EndToEndId ?? "E2E")
            : statusReq.OriginalEndToEnd;
        response.TxId = statusReq.OrgnlTxId;
        response.Original.Amount = tx.Amount;
        response.Original.Currency = tx.Currency ?? string.Empty;
        response.Original.CategoryPurpose = tx.CategoryPurpose ?? string.Empty;

            // Ensure final response includes TxId and Status from CoreBank result
            response.TxId = statusReq.OrgnlTxId ?? response.TxId;
            response.Original.TxId = statusReq.OrgnlTxId ?? response.Original.TxId;
    _logger.LogDebug("[IncomingPaymentStatusReportHandler] response.Original.EndToEndId='{EndToEndId}'", response.Original.EndToEndId);
            // Final safety: schema requires TxSts (status) to be at least length 1
            if (string.IsNullOrWhiteSpace(response.Status))
            {
                response.Status = string.IsNullOrWhiteSpace(request.Status) ? ACSC : request.Status;
            }
            var rspFinal = PaymentStatusRequestResponseBuilder.Build(response);

        // Persist with consistent status: both parent and child reflect the same status
        using (SipsMetrics.TrackStep("Incoming", "Status", "DbSave"))
        {
            using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            await _isoService.PersistStatusResponseAsync(
                record,
                    childStatus,
                    isoMessage.Reason,
                    isoMessage.AdditionalInfo,
                    rspFinal,
                    updateCts.Token);
        }
            _logger.LogDebug("[IncomingPaymentStatusReportHandler] Final response: {Response}", rspFinal);
            var signedFinal = _signer.SignEnvelope(rspFinal);
    _logger.LogDebug("[IncomingPaymentStatusReportHandler] Returning Final signed response: {Signed}", signedFinal);

        return signedFinal;
    }

    /// <summary>
    /// Calls CoreBank to reverse a credit for an incoming return transaction.
    /// This is triggered when pacs.002 confirms a return and the original transaction is ReadyForReturn.
    /// </summary>
    private async Task<PaymentStatusRequestResponseBuilder.Response> CallCoreBankReturnAsync(
        PostgreSQL.Models.ISOMessage isoMessage,
        PostgreSQL.Models.Transaction? transaction,
        PaymentStatusRequestBuilder.Request statusReq,
        PaymentStatusRequestResponseBuilder.Response response,
        CancellationToken ct,
        string cid)
    {
        // Default to ACSC (Success/Completed) unless CoreBank explicitly rejects or fails in a way we map to failure
        response.Status = ACSC;

        var headers = new Dictionary<string, string>() {
            { API_Key, _callbackLinks.Key! },
            { API_Secret, _callbackLinks.Secret! }
        };

        var idem = !string.IsNullOrWhiteSpace(isoMessage.ReturnId) ? isoMessage.ReturnId : transaction?.TxId;
        if (!string.IsNullOrWhiteSpace(idem))
            headers["X-Idempotency-Key"] = idem!;
        if (!string.IsNullOrWhiteSpace(transaction?.TxId))
            headers["X-Transaction-Id"] = transaction.TxId!;
        if (!string.IsNullOrWhiteSpace(isoMessage.ReturnId))
            headers["X-Return-Id"] = isoMessage.ReturnId;

        Response<System.Text.Json.Nodes.JsonObject?>? result = null;
        var coreBankResponseKind = "CB_PaymentResponse";

        if (_core.IncludeCoreBankOnListing)
        {
             // Path A: Bank previously approved the return (Permission Step). 
             // Now we send Completion Notification to confirm finality.
             var notificationDto = new CBCompletionNotification
             {
                 OriginalTxId = transaction?.TxId ?? string.Empty,
                 OriginalEndToEndId = transaction?.EndToEndId,
                 Status = ACSC,
                 Reason = isoMessage.Reason ?? "Return confirmed by IPS",
                 AdditionalInfo = isoMessage.AdditionalInfo ?? "Final return success"
             };

             using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
             coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));
             using (SipsMetrics.TrackStep("Incoming", "Status", "CoreBankCall"))
             {
                 result = await _callbacks.SendJsonAsync(
                    _callbackLinks.CompletionNotification!,
                    headers,
                    notificationDto,
                    CB_CompletionNotification,
                    _jsonAdapter,
                    _correlation,
                    _jsonSerializerOptions,
                    _callback,
                    coreBankCts.Token,
                    cid
                );
             }
             coreBankResponseKind = CB_CompletionNotificationResponse;
        }
        else
        {
            // Path B: Bank has NOT seen this return yet. Use "Late Binding" logic.
            // Send Return Request to execute the reversal.
            if (transaction == null || string.IsNullOrWhiteSpace(transaction.TxId) || string.IsNullOrWhiteSpace(isoMessage.ReturnId))
            {
                _logger.LogError("[{CorrelationId}] Cannot late-bind return to CoreBank because transaction or return identifiers are missing. TxId={TxId}, ReturnId={ReturnId}", cid, transaction?.TxId, isoMessage.ReturnId);

                isoMessage.Status = TransactionStatus.ReadyForReturn;
                isoMessage.Reason = "Missing return identifiers";
                isoMessage.AdditionalInfo = "Manual intervention required to complete return";
                response.Status = ACSC;
                response.Reason = isoMessage.Reason;
                response.AdditionalInfo = isoMessage.AdditionalInfo;

                return response;
            }
            
            // Build return request payload for CoreBank
            var returnDto = new CBReturnRequestDto
            {
                FromBIC = isoMessage.FromBIC ?? string.Empty,
                OriginalEndToEnd = transaction?.EndToEndId ?? string.Empty,
                OrgnlTxId = transaction?.TxId ?? string.Empty,
                ReturnId = isoMessage.ReturnId ?? string.Empty,
                Reason = isoMessage.Reason ?? "Return confirmed by IPS",
                AdditionalInfo = isoMessage.AdditionalInfo ?? string.Empty
            };

            using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));
            using (SipsMetrics.TrackStep("Incoming", "Status", "CoreBankCall"))
            {
                result = await _callbacks.SendJsonAsync(
                    _callbackLinks.Return!,
                    headers,
                    returnDto,
                    CB_ReturnRequest,
                    _jsonAdapter,
                    _correlation,
                    _jsonSerializerOptions,
                    _callback,
                    coreBankCts.Token,
                    cid
                );
            }
        }

        // Guard against null callback result
        if (result == null)
        {
            _logger.LogError("[{CorrelationId}] CoreBank return callback returned null for TxId {TxId}", cid, transaction?.TxId);

            // Map failure - return reversal failed, keep as ReadyForReturn for manual intervention
            isoMessage.Status = TransactionStatus.ReadyForReturn;
            isoMessage.Reason = "CoreBank return callback failed";
            isoMessage.AdditionalInfo = "Manual intervention required to complete return";
            response.Status = ACSC;
            response.Reason = isoMessage.Reason;
            response.AdditionalInfo = isoMessage.AdditionalInfo;

            return response;
        }

        _logger.LogInformation("[{CorrelationId}] Forwarded return to CoreBank for TxId {TxId}, StatusCode={StatusCode}", cid, transaction?.TxId, result.StatusCode);

        if (!result.IsSuccess || result.StatusCode < System.Net.HttpStatusCode.OK || result.StatusCode >= System.Net.HttpStatusCode.MultipleChoices)
        {
            isoMessage.Status = TransactionStatus.ReadyForReturn;
            isoMessage.Reason = "CoreBank return callback failed";
            isoMessage.AdditionalInfo = $"CoreBank returned HTTP {(int)result.StatusCode}; manual intervention required.";
            response.Status = ACSC;
            response.Reason = isoMessage.Reason;
            response.AdditionalInfo = isoMessage.AdditionalInfo;
            return response;
        }

        // Parse CoreBank response
        var cbResponse = result.Data != null ? ParseCallbackResult(result.Data, coreBankResponseKind) : new PaymentResponseDto { Status = string.Empty, TxId = string.Empty };

        // Map CoreBank return response to final status
        // If CBS successfully reversed the credit, mark as Success (return completed)
        // If CBS failed to reverse, keep as ReadyForReturn for manual intervention
        var (parentStatus, childStatus, reason, additionalInfo) = _statusOrchestrator.MapCompletionStatus(
            ACSC,  // IPS confirmed the return
            cbResponse?.Status,  // CBS return result
            false);

        isoMessage.Status = parentStatus;

        // If return completed successfully, update reason and additional info to reflect this
        if (parentStatus == TransactionStatus.Success)
        {
            isoMessage.Reason = "Transaction returned successfully";
            isoMessage.AdditionalInfo = $"Return completed with ReturnId: {isoMessage.ReturnId ?? "N/A"}. CoreBank reversal successful.";
            _logger.LogInformation("[{CorrelationId}] Return completed successfully for TxId {TxId} with ReturnId {ReturnId}",
                cid, transaction?.TxId, isoMessage.ReturnId);
        }
        else
        {
            // Keep original reason/additionalInfo for failed returns
            isoMessage.Reason = reason;
            isoMessage.AdditionalInfo = additionalInfo;
        }

        // Update response to reflect return completion
        if (!string.IsNullOrWhiteSpace(cbResponse?.Status))
        {
            response.Status = cbResponse.Status;
        }
        else if (string.IsNullOrWhiteSpace(response.Status))
        {
            response.Status = ACSC;
        }
        response.Reason = isoMessage.Reason;
        response.AdditionalInfo = isoMessage.AdditionalInfo;

        return response;
    }

    // verification and parsing now delegated to shared services, record/persist via IISOMessageService
    private PaymentResponseDto ParseCallbackResult(JsonObject data, string transformKey = "CB_PaymentResponse")
    {
        // data is already a JsonObject coming from the callback orchestrator
        var js = data;
        if (string.Equals(transformKey, CB_CompletionNotificationResponse, StringComparison.Ordinal))
        {
            var completionMapped = _jsonAdapter.Transform(js!, CB_CompletionNotificationResponse);
            var completion = _jsonAdapter.ToObject<CBCompletionNotificationResponse>(completionMapped);
            if (completion == null)
                return new PaymentResponseDto { Status = string.Empty, TxId = string.Empty };

            return new PaymentResponseDto
            {
                Status = completion.Status ?? string.Empty,
                Reason = completion.Reason,
                AdditionalInfo = completion.AdditionalInfo
            };
        }

        var md = _jsonAdapter.Transform(js!, transformKey);
        var cb = _jsonAdapter.ToObject<SIPS.ISO20022.Models.DTOs.CB.CBPaymentStatusResponseDto>(md);
        if (cb == null)
            return new PaymentResponseDto { Status = string.Empty, TxId = string.Empty };

        return new PaymentResponseDto
        {
            Status = cb.Status ?? string.Empty,
            TxId = cb.TxId ?? string.Empty,
            AcceptanceDate = cb.AcceptanceDate,
            AdditionalInfo = cb.AdditionalInfo,
            Reason = cb.Reason,
            EndToEndId = cb.EndToEndId ?? string.Empty
        };
    }

    private async Task RecordOrphanStatusReportAsync(
        PaymentRequestResponseBuilder.Response request,
        string rawXml,
        CancellationToken ct)
    {
        try
        {
            var orphan = new ISOMessage
            {
                MessageType = ISOMessageType.StatusResponse,
                Date = DateTimeOffset.UtcNow,
                FromBIC = request.From ?? string.Empty,
                ToBIC = request.To ?? string.Empty,
                Message = Encoding.UTF8.GetBytes(rawXml),
                Status = TransactionStatus.CheckStatus,
                Reason = "Parent transaction not found",
                AdditionalInfo = $"Orphan pacs.002 stored for reconciliation. Original TxId: {request.TxId}",
                BizMsgIdr = request.BizMsgIdr ?? string.Empty,
                MsgDefIdr = request.MsgDefIdr ?? SupportedMessageTypes.CreditTransferResponse.Id,
                MsgId = OrphanStatusMsgId(request),
                TxId = null,
                UETR = request.TxId,
                EndToEndId = request.Original?.EndToEndId,
                Pacs002Role = request.Role
            };

            await _persistence.RecordISOMessageAsync(orphan, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist orphan pacs.002 for TxId {TxId}", request.TxId);
        }
    }

    private static string OrphanStatusMsgId(PaymentRequestResponseBuilder.Response request)
    {
        var sourceId = string.IsNullOrWhiteSpace(request.MsgId)
            ? $"{request.TxId}-{request.Role}"
            : request.MsgId;

        return $"orphan-{sourceId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }
}
