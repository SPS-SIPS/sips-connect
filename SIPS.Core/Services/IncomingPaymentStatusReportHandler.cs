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

        _logger.LogInformation("INCOMING pacs.002 Handler for data {Data}",
            JsonSerializer.Serialize(request, _jsonSerializerOptions));

        // Step 2: Retrieve ISO message by TxId
        var isoMessage = await _persistence.GetISOMessageWithTransactionsByTxIdAsync(request.OriginalTxId, ct);
        if (isoMessage == null)
        {
            return ErrorResponse("Failed to get the Message.");
        }

        var transaction = isoMessage.Transactions.FirstOrDefault();

        if (transaction == null)
        {
            return ErrorResponse("Failed to get the Transaction.");
        }

        // Step 3: Record the incoming status report under parent ISOMessage
        var record = await _isoService.RecordIncomingStatusAsync(isoMessage, message, ct);
        // Step 4: Prepare response object (reuse PaymentStatus request response builder)
        var statusReq = new PaymentStatusRequestBuilder.Request
        {
            From = isoMessage.ToBIC ?? string.Empty,
            To = isoMessage.FromBIC ?? string.Empty,
            OrgnlTxId = request.OriginalTxId,
            OriginalEndToEnd = request.OriginalEndToEndId ?? "",
            BizMsgIdr = isoMessage.BizMsgIdr ?? string.Empty,
            MsgDefIdr = isoMessage.MsgDefIdr ?? string.Empty,
            MsgId = isoMessage.MsgId ?? string.Empty,
        };

        var response = _responses.BuildPaymentStatusInitial(statusReq);

        try
        {
            // Step 5: If incoming completion status exists, update DB, notify CoreBank, and respond mirroring status
            var incomingStatus = request.Status?.ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(incomingStatus))
            {
                response.Status = incomingStatus;
                response.Reason = request.Reason ?? string.Empty;
                response.AdditionalInfo = request.AdditionalInfo ?? string.Empty;

                // Notify CoreBank about completion
                if (!string.IsNullOrWhiteSpace(_callbackLinks.CompletionNotification))
                {
                    var headers = new Dictionary<string, string>() {
                        { Constants.API_Key, _callbackLinks.Key! },
                        { Constants.API_Secret, _callbackLinks.Secret! }
                    };

                    var completionDto = new CBCompletionNotification
                    {
                        OriginalTxId = request.OriginalTxId,
                        OriginalEndToEndId = request.OriginalEndToEndId ?? string.Empty,
                        Status = incomingStatus,
                        Reason = request.Reason ?? string.Empty,
                        AdditionalInfo = request.AdditionalInfo ?? string.Empty
                    };
                    Console.WriteLine($"Completion notification: {JsonSerializer.Serialize(completionDto, _jsonSerializerOptions)}");
                    try
                    {
                        await _callbacks.SendJsonAsync(
                            _callbackLinks.CompletionNotification!,
                            headers,
                            completionDto,
                            Constants.CB_CompletionNotification,
                            _jsonAdapter,
                            _correlation,
                            _jsonSerializerOptions,
                            _callback,
                            ct,
                            cid);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[{CorrelationId}] Failed to notify CoreBank completion status for TxId {TxId}", cid, request.OriginalTxId);
                    }
                }

                // Minimal safeguards for schema-required fields
                response.Original.Debtor.Name = transaction.DebtorName;
                response.Original.Creditor.Name = transaction.CreditorName;
                response.Original.Debtor.Account = transaction.DebtorAccount;
                response.Original.Creditor.Account = transaction.CreditorAccount;
                response.Original.Debtor.AccountType = transaction.DebtorAccountType;
                response.Original.Creditor.AccountType = transaction.CreditorAccountType;
                response.Original.From = isoMessage.ToBIC ?? _callbackLinks.BIC ?? "NA";
                response.Original.To = isoMessage.FromBIC ?? _callbackLinks.Agent ?? _callbackLinks.BIC ?? "NA";
                response.Original.EndToEndId = statusReq.OriginalEndToEnd ?? isoMessage.EndToEndId ?? "E2E";
                response.TxId = statusReq.OrgnlTxId;
                if (response.Original.Amount <= 0 && transaction.Amount > 0) response.Original.Amount = transaction.Amount;
                if (string.IsNullOrWhiteSpace(response.Original.Currency)) response.Original.Currency = transaction.Currency;

                // Build, persist, and sign response mirroring the final status
                var rspFinal = PaymentStatusRequestResponseBuilder.Build(response);
                await _isoService.PersistStatusResponseAsync(record, response.Status ?? "RJCT", response.Reason ?? string.Empty, response.AdditionalInfo ?? string.Empty, rspFinal, ct);
                return _signer.SignEnvelope(rspFinal);
            }

            // Step 6: If DB status is Pending/Unknown, query CoreBank; else map DB to response
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
                // Minimal mapping: reflect current DB status only
                response.Status = (isoMessage.Status == TransactionStatus.Success) ? ACSC : "RJCT";
                response.Reason = isoMessage.Reason ?? string.Empty;
                response.TxId = isoMessage.TxId ?? string.Empty;
            }

            // Minimal safeguards for schema-required fields
            response.Original.Debtor.Name = transaction.DebtorName;
            response.Original.Creditor.Name = transaction.CreditorName;
            response.Original.Debtor.Account = transaction.DebtorAccount;
            response.Original.Creditor.Account = transaction.CreditorAccount;
            response.Original.Debtor.AccountType = transaction.DebtorAccountType;
            response.Original.Creditor.AccountType = transaction.CreditorAccountType;
            response.Original.From = isoMessage.ToBIC ?? _callbackLinks.BIC ?? "NA";
            response.Original.To = isoMessage.FromBIC ?? _callbackLinks.Agent ?? _callbackLinks.BIC ?? "NA";
            response.Original.EndToEndId = statusReq.OriginalEndToEnd ?? isoMessage.EndToEndId ?? "E2E";
            response.TxId = statusReq.OrgnlTxId;
            if (response.Original.Amount <= 0 && transaction.Amount > 0) response.Original.Amount = transaction.Amount;
            if (string.IsNullOrWhiteSpace(response.Original.Currency)) response.Original.Currency = transaction.Currency;

            // Step 6: Build, persist, and sign response
            var rsp = PaymentStatusRequestResponseBuilder.Build(response);
            await _isoService.PersistStatusResponseAsync(record, response.Status ?? "RJCT", response.Reason ?? string.Empty, response.AdditionalInfo ?? string.Empty, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] INCOMING pacs.002 Handler Exception for TxId {TxId}", cid, request?.OriginalTxId);
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
