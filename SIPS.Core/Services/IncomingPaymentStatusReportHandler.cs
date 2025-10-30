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
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Services;
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
        var (isValid, request) = await _inbound.VerifyAndParseAsync<PaymentRequestResponseBuilder.Response>(
            message,
            (xml) =>
            {
                if (!_reportParser.TryParse(xml, out var req)) return (false, (PaymentRequestResponseBuilder.Response?)null);
                return (true, req);
            },
            ct,
            cid);
        if (!isValid || request == null)
            return AdminMessage.Generate("Failed to verify signature or parse TxId.");

        _logger.LogInformation("INCOMING pacs.002 Handler for data {Data}",
            JsonSerializer.Serialize(request, _jsonSerializerOptions));

        // Step 2: Retrieve ISO message by TxId
        var isoMessage = await _persistence.GetISOMessageWithTransactionsByTxIdAsync(request.TxId, ct);
        if (isoMessage == null)
        {
            return AdminMessage.Generate("Failed to get the Message.");
        }

        var transaction = isoMessage.Transactions.FirstOrDefault();

        if (transaction == null)
        {
            return AdminMessage.Generate("Failed to get the Transaction.");
        }

        // Parse incoming pacs.002 to use its debtor/creditor data as fallback where needed
        var incomingParsed = PaymentRequestResponseBuilder.Parse(message);

        // Step 3: Record the incoming status report under parent ISOMessage
        var record = await _isoService.RecordIncomingStatusAsync(isoMessage, message, ct);
        // Step 4: Prepare response object (reuse PaymentStatus request response builder)
        var statusReq = new PaymentStatusRequestBuilder.Request
        {
            From = isoMessage.ToBIC ?? string.Empty,
            To = isoMessage.FromBIC ?? string.Empty,
            OrgnlTxId = request.TxId,
            OriginalEndToEnd = request.Original?.EndToEndId ?? "",
            BizMsgIdr = isoMessage.BizMsgIdr ?? string.Empty,
            MsgDefIdr = isoMessage.MsgDefIdr ?? string.Empty,
            MsgId = isoMessage.MsgId ?? string.Empty,
        };

        var response = _responses.BuildPaymentStatusInitial(statusReq);

        try
        {
            // Step 5: Incoming completion status is always present; update DB, notify CoreBank, and respond mirroring status
            var incomingStatus = request.Status?.ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(incomingStatus))
            {
                response.Status = incomingStatus;
                response.Reason = request.Reason ?? string.Empty;
                response.AdditionalInfo = request.AdditionalInfo ?? string.Empty;

                var headers = new Dictionary<string, string>() {
                        { API_Key, _callbackLinks.Key! },
                        { API_Secret, _callbackLinks.Secret! }
                    };

                    var completionDto = new CBCompletionNotification
                    {
                        OriginalTxId = request.TxId,
                        OriginalEndToEndId = request.Original?.EndToEndId ?? string.Empty,
                        Status = incomingStatus,
                        Reason = request.Reason ?? string.Empty,
                        AdditionalInfo = request.AdditionalInfo ?? string.Empty
                    };


                var result = await _callbacks.SendJsonAsync(
                    _callbackLinks.CompletionNotification!,
                    headers,
                    completionDto,
                    CB_CompletionNotification,
                    _jsonAdapter,
                    _correlation,
                    _jsonSerializerOptions,
                    _callback,
                    ct,
                    cid
                );

                _logger.LogInformation("[{CorrelationId}] Notified CoreBank completion status for TxId {TxId}, {result}", cid, request.TxId, JsonSerializer.Serialize(result, _jsonSerializerOptions));

                // Minimal safeguards for schema-required fields (prefer transaction, then incoming pacs.002 parsed, then defaults)
                var incomingDebtorName = incomingParsed?.Original?.Debtor?.Name;
                var incomingCreditorName = incomingParsed?.Original?.Creditor?.Name;
                var incomingDebtorAccount = incomingParsed?.Original?.Debtor?.Account;
                var incomingCreditorAccount = incomingParsed?.Original?.Creditor?.Account;

                response.Original.Debtor.Name = transaction.DebtorName ?? request.Original?.Debtor?.Name ?? "NA";
                response.Original.Creditor.Name = transaction.CreditorName ?? request.Original?.Creditor?.Name ?? "NA";
                response.Original.Debtor.Account = transaction.DebtorAccount ?? request.Original?.Debtor?.Account ?? "NA";
                response.Original.Creditor.Account = transaction.CreditorAccount ?? request.Original?.Creditor?.Account ?? "NA";
                response.Original.Debtor.AccountType = transaction.DebtorAccountType ?? request.Original?.Debtor?.AccountType ?? "ACCT";
                response.Original.Creditor.AccountType = transaction.CreditorAccountType ?? request.Original?.Creditor?.AccountType ?? "ACCT";
                response.Original.From = isoMessage.ToBIC ?? _callbackLinks.BIC ?? "NA";
                response.Original.To = isoMessage.FromBIC ?? _callbackLinks.Agent ?? _callbackLinks.BIC ?? "NA";
                response.Original.EndToEndId = statusReq.OriginalEndToEnd ?? isoMessage.EndToEndId ?? "E2E";
                response.TxId = statusReq.OrgnlTxId;
                if (response.Original.Amount <= 0 && transaction.Amount > 0) response.Original.Amount = transaction.Amount;
                if (string.IsNullOrWhiteSpace(response.Original.Currency)) response.Original.Currency = string.IsNullOrWhiteSpace(transaction.Currency) ? "USD" : transaction.Currency;

                // Build, persist, and sign response mirroring the final status
                var rspFinal = PaymentStatusRequestResponseBuilder.Build(response);
                await _isoService.PersistStatusResponseAsync(record, response.Status ?? RJCT, response.Reason ?? string.Empty, response.AdditionalInfo ?? string.Empty, rspFinal, ct);
                return _signer.SignEnvelope(rspFinal);
            }
            // If we reach here without a status (unexpected), persist a rejection and return
            response.Status = RJCT;
            response.Reason = string.IsNullOrWhiteSpace(response.Reason) ? MISS : response.Reason;
            var rspErr = PaymentStatusRequestResponseBuilder.Build(response);
            await _isoService.PersistStatusResponseAsync(record, response.Status, response.Reason ?? string.Empty, response.AdditionalInfo ?? string.Empty, rspErr, ct);
            return _signer.SignEnvelope(rspErr);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] INCOMING pacs.002 Handler Exception for TxId {TxId}", cid, request?.TxId);
            response.AdditionalInfo = "Failed to process pacs.002";
            var rsp = PaymentStatusRequestResponseBuilder.Build(response);
            await _isoService.PersistStatusResponseAsync(record, response.Status ?? RJCT, response.Reason ?? string.Empty, response.AdditionalInfo ?? string.Empty, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
    }
}
