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
using SIPS.PostgreSQL.Enums;
using SIPS.ISO20022.Models.DTOs;

namespace SIPS.Core.Services;

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
    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        var cid = _correlation.Create();
        using var dbCts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(10));
        var dbCt = dbCts.Token;
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

        // Step 3: Record the incoming status report under parent ISOMessage
        var record = await _isoService.RecordIncomingStatusAsync(isoMessage, message, ct);
        var transaction = isoMessage.Transactions.FirstOrDefault();

        if (transaction == null)
        {
            await _isoService.PersistStatusResponseAsync(record, TransactionStatus.Failed, "Failed To locate", "Not found", "No Message Found", dbCt);
            return AdminMessage.Generate("Failed to get the Transaction.");
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

        // Step 5: Incoming completion status is always present; if Pending exists, decide flow:
        // - RJCT: persist as failed and return (no CoreBank call)
        // - ACSC: forward original payment to CoreBank, then persist and return
        response.Status = request.Status;
        response.Reason = request.Reason ?? string.Empty;
        response.AdditionalInfo = request.AdditionalInfo ?? string.Empty;

        // If related ISO message is not pending anymore, just mirror and return without forwarding
        if (isoMessage.Status != TransactionStatus.Pending)
        {
            await _isoService.PersistStatusResponseAsync(record, TransactionStatus.Failed, "Not Pending", "Only Pendig Transaction Can be modified", "Invalid Process", dbCt);
            return AdminMessage.Generate($"No Pending Transaction Related to this TxId {request.TxId}");
        }

        // If RJCT -> persist and return without CoreBank call
        if (request.Status == RJCT)
        {
            var rspRej = PaymentStatusRequestResponseBuilder.Build(response);
            isoMessage.Status = TransactionStatus.Failed;
            isoMessage.Reason = !string.IsNullOrWhiteSpace(response.Reason) ? response.Reason : "Received rejection confirmation";
            isoMessage.AdditionalInfo = "Standard Rejection Confirmaiton Notification Received";
            await _isoService.PersistStatusResponseAsync(record, TransactionStatus.Failed, isoMessage.Reason, isoMessage.AdditionalInfo, rspRej, dbCt);
            return _signer.SignEnvelope(rspRej);
        }

        var headers = new Dictionary<string, string>() {
                        { API_Key, _callbackLinks.Key! },
                        { API_Secret, _callbackLinks.Secret! }
                    };
        // Idempotency key for safe retries downstream
        var idem = transaction?.TxId ?? request.TxId;
        if (!string.IsNullOrWhiteSpace(idem))
            headers["X-Idempotency-Key"] = idem!;

        // Here incomingStatus must be ACSC; build CB payment request payload for CB

        var tx = transaction!;
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

        var result = await _callbacks.SendJsonAsync(
                _callbackLinks.Transfer!,
                    headers,
                    dto,
                    CB_PaymentRequest,
                    _jsonAdapter,
                    _correlation,
                    _jsonSerializerOptions,
                    _callback,
                    ct,
                    cid
                );

        _logger.LogInformation("[{CorrelationId}] Forwarded transaction to CoreBank for TxId {TxId}, {result}", cid, request.TxId, JsonSerializer.Serialize(result, _jsonSerializerOptions));

        // Persist raw CoreBank response JSON on the parent ISOMessage for audit/operations

        var crResponse = ParseCallbackResult(result.Data!);
        var cbProcessed = crResponse.Status == ACSC;

        isoMessage.Status = cbProcessed ? TransactionStatus.Success : TransactionStatus.ReadyForReturn;
        isoMessage.AdditionalInfo = cbProcessed ? "Processed" : "Queaed For Return!";
        isoMessage.Reason = cbProcessed ? "Processed Transaction" : "Transaction is ready for return";

        isoMessage.CoreBankResponse = JsonSerializer.Serialize(result, _jsonSerializerOptions);

        response.Original.Debtor.Name = tx.DebtorName ?? request.Original?.Debtor?.Name ?? "NA";
        response.Original.Creditor.Name = tx.CreditorName ?? request.Original?.Creditor?.Name ?? "NA";
        response.Original.Debtor.Account = tx.DebtorAccount ?? request.Original?.Debtor?.Account ?? "NA";
        response.Original.Creditor.Account = tx.CreditorAccount ?? request.Original?.Creditor?.Account ?? "NA";
        response.Original.Debtor.AccountType = tx.DebtorAccountType ?? request.Original?.Debtor?.AccountType ?? "ACCT";
        response.Original.Creditor.AccountType = tx.CreditorAccountType ?? request.Original?.Creditor?.AccountType ?? "ACCT";
        response.Original.From = isoMessage.ToBIC ?? _callbackLinks.BIC ?? "NA";
        response.Original.To = isoMessage.FromBIC ?? _callbackLinks.Agent ?? _callbackLinks.BIC ?? "NA";
        response.Original.EndToEndId = statusReq.OriginalEndToEnd ?? isoMessage.EndToEndId ?? "E2E";
        response.TxId = statusReq.OrgnlTxId;
        response.Original.Amount = tx.Amount;
        response.Original.Currency = tx.Currency ?? string.Empty;
        response.Original.CategoryPurpose = tx.CategoryPurpose ?? string.Empty;

        var rspFinal = PaymentStatusRequestResponseBuilder.Build(response);

        await _isoService.PersistStatusResponseAsync(
            record,
            TransactionStatus.Success,
            isoMessage.Reason,
            isoMessage.AdditionalInfo,
            rspFinal,
            dbCt);

        return _signer.SignEnvelope(rspFinal);
    }

    // verification and parsing now delegated to shared services, record/persist via IISOMessageService
    private PaymentResponseDto ParseCallbackResult(JsonObject data)
    {
        // convert the responseContent to a JsonObject
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);
        var md = _jsonAdapter.Transform(js!, "CB_PaymentResponse");
        var deserializedContent = _jsonAdapter.ToObject<PaymentResponseDto>(md);

        return deserializedContent;
    }
}
