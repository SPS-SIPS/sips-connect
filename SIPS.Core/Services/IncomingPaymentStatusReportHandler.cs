using System.Net;
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
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Services;
using SIPS.Core.Interfaces;
using SIPS.Core.Services.ISOParsers;

namespace SIPS.Core.Services;

public sealed class IncomingPaymentStatusReportHandler(
    ISO20022Options options,
    ILogger<IncomingPaymentStatusReportHandler> logger,
    INativeSigner signer,
    IJsonAdapter jsonAdapter,
    ISignatureService signature,
    IPaymentStatusReportParser reportParser,
    ICallbackClient callback,
    IResponseFactory responseFactory,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    IInboundMessageService inbound,
    ICallbackOrchestrator callbacks,
    IISOMessageService isoService
) : IIncomingPaymentStatusReportHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly ILogger<IncomingPaymentStatusReportHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly ISignatureService _signature = signature;
    private readonly ICallbackClient _callback = callback;
    private readonly IResponseFactory _responses = responseFactory;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly IInboundMessageService _inbound = inbound;
    private readonly ICallbackOrchestrator _callbacks = callbacks;
    private readonly IISOMessageService _isoService = isoService;
    private readonly IPaymentStatusReportParser _reportParser = reportParser;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Compatibility constructor
    public IncomingPaymentStatusReportHandler(
        ISO20022Options options,
        ILogger<IncomingPaymentStatusReportHandler> logger,
        INativeSigner signer,
        IJsonAdapter jsonAdapter,
        ISignatureService signature,
        IPaymentStatusReportParser reportParser,
        ICallbackClient callback,
        IResponseFactory responseFactory,
        IPersistenceGateway persistence,
        ICorrelationService correlation)
        : this(options, logger, signer, jsonAdapter, signature, reportParser, callback, responseFactory, persistence, correlation,
              new InboundMessageService(signature),
              new CallbackOrchestrator(),
              new ISOMessageService(persistence))
    { }

    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        var cid = _correlation.Create();
        // Step 1: Verify signature and parse via parser
        var (isValid, request) = await _inbound.VerifyAndParseAsync<PaymentStatusReportParser.ReportRequest>(
            message,
            (xml) =>
            {
                if (!_reportParser.TryParse(xml, out var req)) return (false, (PaymentStatusReportParser.ReportRequest?)null);
                return (true, req);
            },
            ct,
            cid);
        if (!isValid || request == null)
            return ErrorResponse("Failed to verify signature or parse TxId.");

        // Step 2: Retrieve ISO message by TxId
        var isoMessage = await _persistence.GetISOMessageByTxIdAsync(request.OrgnlTxId, ct);
        if (isoMessage == null)
        {
            return ErrorResponse("Failed to get the Message.");
        }

        // Step 3: Record the incoming status report under parent ISOMessage
        var record = await _isoService.RecordIncomingStatusAsync(isoMessage, message, ct);

        // Step 4: Prepare response object (reuse PaymentStatus request response builder)
        var statusReq = new PaymentStatusRequestBuilder.Request
        {
            From = isoMessage.ToBIC ?? string.Empty,
            To = isoMessage.FromBIC ?? string.Empty,
            OrgnlTxId = request.OrgnlTxId,
            OriginalEndToEnd = isoMessage.EndToEndId ?? string.Empty,
            BizMsgIdr = isoMessage.BizMsgIdr ?? string.Empty,
            MsgDefIdr = isoMessage.MsgDefIdr ?? string.Empty,
            MsgId = isoMessage.MsgId ?? string.Empty,
        };
        var response = _responses.BuildPaymentStatusInitial(statusReq);

        try
        {
            // Step 5: If DB status is Pending/Unknown, query CoreBank; else map DB to response
            if (isoMessage.Status == TransactionStatus.Pending)
            {
                var headers = new Dictionary<string, string>() {
                    { Constants.API_Key, _callbackLinks.Key! },
                    { Constants.API_Secret, _callbackLinks.Secret! }
                };
                var dto = new CBStatusRequestDto
                {
                    FromBIC = statusReq.From,
                    OriginalEndToEnd = statusReq.OriginalEndToEnd,
                    OrgnlTxId = statusReq.OrgnlTxId
                };
                var responseMessage = await _callbacks.SendJsonAsync(
                    _callbackLinks.Status!,
                    headers,
                    dto,
                    Constants.CB_StatusRequest,
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
                    await _isoService.PersistStatusResponseAsync(record, "RJCT", "Failed to parse the message", "Failed to parse the message", string.Empty, ct);
                    _logger.LogWarning("[{CorrelationId}] Failed to get response from CB. Status: {Status}", cid, responseMessage.StatusCode);
                    response.AdditionalInfo = "Failed to get response from CB.";
                }
            }
            else
            {
                // Map DB status
                response.Status = (isoMessage.Status == TransactionStatus.Success) ? Constants.ACSC : "RJCT";
                response.Reason = isoMessage.Reason ?? string.Empty;
                response.TxId = isoMessage.TxId ?? string.Empty;
                // Populate required original fields from DB entity to satisfy schema
                response.Original.From = isoMessage.ToBIC ?? response.Original.From;
                response.Original.To = isoMessage.FromBIC ?? response.Original.To;
                response.Original.BizMsgIdr = isoMessage.BizMsgIdr ?? response.Original.BizMsgIdr;
                response.Original.MsgDefIdr = isoMessage.MsgDefIdr ?? response.Original.MsgDefIdr;
                response.Original.MsgId = isoMessage.MsgId ?? response.Original.MsgId;
                response.Original.EndToEndId = isoMessage.EndToEndId ?? response.Original.EndToEndId ?? string.Empty;
                var tx = isoMessage.Transactions.FirstOrDefault();
                if (tx != null)
                {
                    response.Original.LocalInstrument = tx.LocalInstrument ?? response.Original.LocalInstrument ?? "N";
                    response.Original.CategoryPurpose = tx.CategoryPurpose ?? response.Original.CategoryPurpose ?? "N";
                    response.Original.Amount = tx.Amount;
                    response.Original.Currency = tx.Currency ?? response.Original.Currency ?? "USD";
                    response.Original.Debtor.Name = string.IsNullOrWhiteSpace(tx.DebtorName) ? "N/A" : tx.DebtorName;
                    response.Original.Debtor.Account = tx.DebtorAccount ?? response.Original.Debtor.Account ?? "NA";
                    response.Original.Debtor.AccountType = tx.DebtorAccountType ?? response.Original.Debtor.AccountType ?? "NA";
                    response.Original.Debtor.AgentBIC = tx.DebtorAgentBIC ?? response.Original.Debtor.AgentBIC ?? "NA";
                    response.Original.Debtor.Issuer = tx.DebtorIssuer ?? response.Original.Debtor.Issuer ?? "C";
                    response.Original.Creditor.Name = string.IsNullOrWhiteSpace(tx.CreditorName) ? "N/A" : tx.CreditorName;
                    response.Original.Creditor.Account = tx.CreditorAccount ?? response.Original.Creditor.Account ?? "NA";
                    response.Original.Creditor.AccountType = tx.CreditorAccountType ?? response.Original.Creditor.AccountType ?? "NA";
                    response.Original.Creditor.AgentBIC = tx.CreditorAgentBIC ?? response.Original.Creditor.AgentBIC ?? "NA";
                    response.Original.Creditor.Issuer = tx.CreditorIssuer ?? response.Original.Creditor.Issuer ?? "C";
                    response.Original.Ustrd = tx.RemittanceInformation ?? response.Original.Ustrd ?? "N";
                }
                else
                {
                    // Ensure required account fields exist even if transactions are missing
                    response.Original.Debtor.Name = string.IsNullOrWhiteSpace(response.Original.Debtor.Name) ? "N/A" : response.Original.Debtor.Name;
                    response.Original.Debtor.Account = string.IsNullOrWhiteSpace(response.Original.Debtor.Account) ? "NA" : response.Original.Debtor.Account;
                    response.Original.Creditor.Name = string.IsNullOrWhiteSpace(response.Original.Creditor.Name) ? "N/A" : response.Original.Creditor.Name;
                    response.Original.Creditor.Account = string.IsNullOrWhiteSpace(response.Original.Creditor.Account) ? "NA" : response.Original.Creditor.Account;
                }
            }

            // Safety defaults to satisfy schema constraints
            response.Original.From = string.IsNullOrWhiteSpace(response.Original.From) ? (isoMessage.ToBIC ?? _callbackLinks.BIC ?? "NA") : response.Original.From;
            response.Original.To = string.IsNullOrWhiteSpace(response.Original.To) ? (isoMessage.FromBIC ?? _callbackLinks.Agent ?? _callbackLinks.BIC ?? "NA") : response.Original.To;
            response.Original.MsgDefIdr = string.IsNullOrWhiteSpace(response.Original.MsgDefIdr) ? (isoMessage.MsgDefIdr ?? "pacs.008.001.10") : response.Original.MsgDefIdr;
            response.Original.BizMsgIdr = string.IsNullOrWhiteSpace(response.Original.BizMsgIdr) ? (isoMessage.BizMsgIdr ?? "BIZ") : response.Original.BizMsgIdr;
            response.Original.MsgId = string.IsNullOrWhiteSpace(response.Original.MsgId) ? (isoMessage.MsgId ?? "MSG") : response.Original.MsgId;
            response.Original.Debtor.Name = string.IsNullOrWhiteSpace(response.Original.Debtor.Name) ? "N/A" : response.Original.Debtor.Name;
            response.Original.Debtor.Account = string.IsNullOrWhiteSpace(response.Original.Debtor.Account) ? "NA" : response.Original.Debtor.Account;
            response.Original.Debtor.AccountType = string.IsNullOrWhiteSpace(response.Original.Debtor.AccountType) ? "NA" : response.Original.Debtor.AccountType;
            response.Original.Debtor.AgentBIC = string.IsNullOrWhiteSpace(response.Original.Debtor.AgentBIC) ? "NA" : response.Original.Debtor.AgentBIC;
            response.Original.Creditor.Name = string.IsNullOrWhiteSpace(response.Original.Creditor.Name) ? "N/A" : response.Original.Creditor.Name;
            response.Original.Creditor.Account = string.IsNullOrWhiteSpace(response.Original.Creditor.Account) ? "NA" : response.Original.Creditor.Account;
            response.Original.Creditor.AccountType = string.IsNullOrWhiteSpace(response.Original.Creditor.AccountType) ? "NA" : response.Original.Creditor.AccountType;
            response.Original.Creditor.AgentBIC = string.IsNullOrWhiteSpace(response.Original.Creditor.AgentBIC) ? "NA" : response.Original.Creditor.AgentBIC;
            response.Original.EndToEndId = string.IsNullOrWhiteSpace(response.Original.EndToEndId) ? (isoMessage.EndToEndId ?? "E2E") : response.Original.EndToEndId;

            // Step 6: Build, persist, and sign response
            var rsp = PaymentStatusRequestResponseBuilder.Build(response);
            await _isoService.PersistStatusResponseAsync(record, response.Status ?? "RJCT", response.Reason ?? string.Empty, response.AdditionalInfo ?? string.Empty, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] INCOMING pacs.002 Handler Exception for TxId {TxId}", cid, request?.OrgnlTxId);
            response.AdditionalInfo = "Failed to process pacs.002";
            var rsp = PaymentStatusRequestResponseBuilder.Build(response);
            await _isoService.PersistStatusResponseAsync(record, response.Status ?? "RJCT", response.Reason ?? string.Empty, response.AdditionalInfo ?? string.Empty, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
    }

    private string ErrorResponse(string message)
    {
        return AdminMessage.Generate(message);
    }

    private void ParseCallbackResult(JsonObject data, PaymentStatusRequestResponseBuilder.Response response)
    {
        _logger.LogDebug("Callback result: {Data}", data.ToString());
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);

        var md = _jsonAdapter.Transform(js!, Constants.CB_PaymentStatusResponse);

        var deserializedContent = _jsonAdapter.ToObject<SIPS.ISO20022.Models.DTOs.CB.CBPaymentStatusResponseDto>(md);

        if (deserializedContent == null)
        {
            response.Status = "RJCT";
            response.Reason = "MISS";
            response.AdditionalInfo = string.Empty;
            return;
        }

        response.Status = deserializedContent.Status ?? "RJCT";
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
}
