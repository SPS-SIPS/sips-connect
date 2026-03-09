using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIPS.Adapter;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models;
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
using SIPS.Core.Services.Metrics;
using static SIPS.Core.Constants;
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
        using var _totalTrack = SipsMetrics.TrackStep("Incoming", "Status", "Total");
        var cid = _correlation.Create();
        // Step 1: Verify signature and parse message
        bool isValid = false;
        PaymentStatusRequestBuilder.Request? request = null;

        using (SipsMetrics.TrackStep("Incoming", "Status", "ParsingAndSignature"))
        {
            var (valid, parsedRequest) = await _inbound.VerifyAndParseAsync(
                message,
                (xml) =>
                {
                    if (!_parser.TryParse(xml, out var req)) return (false, (PaymentStatusRequestBuilder.Request?)null);
                    return (true, req);
                },
                ct,
                cid);
            isValid = valid;
            request = parsedRequest;
        }

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
        ISOMessageStatus record;
        using (SipsMetrics.TrackStep("Incoming", "Status", "DbSave"))
        {
            record = await _isoService.RecordIncomingStatusAsync(isoMessage, message, SIPS.ISO20022.Enums.Pacs002Role.StatusUpdate, request.MsgId, ct);
        }

        // Step 4: Prepare response object
        var response = _responses.BuildPaymentStatusInitial(request);

        // Delta 4: Ledger-as-Truth (Protocol Aligned)
        // Inbound pacs.028 probes are resolved strictly against the local DB state.
        // We do not proxy to CoreBank as an "oracle" during the switch investigation.
        _logger.LogInformation("[{CorrelationId}] pacs.028 inquiry for TxId {TxId} resolved via local ledger (Status={Status}).", 
            cid, request.OrgnlTxId, isoMessage.Status);
        
        if (isoMessage.Status == TransactionStatus.Success)
        {
            response.Status = ACSC;
            response.Reason = isoMessage.Reason ?? "G001"; // Successful
        }
        else if (isoMessage.Status == TransactionStatus.Failed)
        {
            response.Status = RJCT;
            response.Reason = isoMessage.Reason ?? "MS03"; // Rejected
        }
        else
        {
            // If still Pending or CheckStatus, return ACSP (AcceptedSettlementInProcess)
            // indicating the participant is still processing or in-doubt.
            response.Status = SIPS.Core.Constants.ACSP;
            response.Reason = "PDNG"; 
        }

        response.AdditionalInfo = isoMessage.AdditionalInfo;
        
        // Propagate addresses from ledger to response for full disclosure in pacs.002 Rltd block
        var tx = isoMessage.Transactions.FirstOrDefault();
        if (tx != null)
        {
            response.Original.Debtor ??= new Person();
            response.Original.Debtor.Name = tx.DebtorName ?? string.Empty;
            response.Original.Debtor.Address = tx.DebtorAddress ?? string.Empty;
            response.Original.Creditor ??= new Person();
            response.Original.Creditor.Name = tx.CreditorName ?? string.Empty;
            response.Original.Creditor.Address = tx.CreditorAddress ?? string.Empty;
        }
        
            
        var localRsp = PaymentStatusRequestResponseBuilder.Build(response);
        await _isoService.PersistStatusResponseAsync(record, isoMessage.Status, response.Reason, response.AdditionalInfo ?? string.Empty, localRsp, ct);
        return _signer.SignEnvelope(localRsp);
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
        response.Original.Debtor.Address = deserializedContent.DebtorAddress ?? string.Empty;
        response.Original.Debtor.AccountType = deserializedContent.DebtorAccountType ?? string.Empty;
        response.Original.Debtor.AgentBIC = deserializedContent.DebtorAgentBIC ?? string.Empty;
        response.Original.Debtor.Issuer = deserializedContent.DebtorIssuer ?? string.Empty;
        response.Original.Creditor.Name = deserializedContent.CreditorName ?? string.Empty;
        response.Original.Creditor.Account = deserializedContent.CreditorAccount ?? string.Empty;
        response.Original.Creditor.Address = deserializedContent.CreditorAddress ?? string.Empty;
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