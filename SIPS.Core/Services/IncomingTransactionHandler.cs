using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIPS.Adapter;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Services;
using Microsoft.Extensions.Logging;
namespace SIPS.Core.Services;
public sealed class IncomingTransactionHandler(
    ISO20022Options options,
    ILogger<IncomingTransactionHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IJsonAdapter jsonAdapter,
    IIncomingRecorder record
    ) : IIncomingTransactionHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ILogger<IncomingTransactionHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly INativeVerifier _verifier = verifier;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IIncomingRecorder _record = record;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        // Verify the signature
        if (!await VerifySignatureAsync(message, ct))
        {
            return AdminMessage.Generate("Failed to verify the signature.");
        }

        // Parse the message
        if (!TryParse(message, out var request))
        {
            return AdminMessage.Generate("Failed to parse the message.");
        }

        var entity = CreateISOMessage(request, message);
        var record = await _record.ISOMessageAsync(entity, ct);
        var response = new PaymentRequestResponseBuilder.Response
        {
            From = request.To,
            To = request.From,
            MsgDefIdr = request.MsgDefIdr,
            BizMsgIdr = request.BizMsgIdr,
            MsgId = request.MsgId,
            CreDt = request.CreDt,
            Original = request,
            Status = RJCT,
            Reason = MISS,
        };

        try
        {
            // Send the request to the CB
            var responseMessage = await SendCallbackAsync(request, ct);

            if (responseMessage.StatusCode == HttpStatusCode.OK && responseMessage.Data != null)
            {
                ParseCallbackResult(responseMessage.Data, response);
            }
            else
            {
                response.AdditionalInfo = "Failed to get response from CB.";
            }
            // Build the response
            var rsp = PaymentRequestResponseBuilder.Build(response);
            await PersistISOMessageAsync(record, response.Status, response.Reason, response.AdditionalInfo, response.TxId, request.EndToEndId, rsp, ct);
            // Sign and return the response
            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "INCOMING PS Handler Exception for TxId {TxId}", request.TxId);
            response.AdditionalInfo = "Failed to process Transaction";
            var rsp = PaymentRequestResponseBuilder.Build(response);
            await PersistISOMessageAsync(record, response.Status, response.Reason, response.AdditionalInfo, response.TxId, request.EndToEndId, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
    }

    private async Task<bool> VerifySignatureAsync(string message, CancellationToken ct)
    {
        var (result, verbose) = await _verifier.VerifySignature(message, false, ct);

        if (!result)
        {
            _logger.LogError("Failed to verify the signature: verbose {verbose}", verbose);
        }

        return result;
    }

    private static bool TryParse(string message, out PaymentRequestBuilder.Request request)
    {
        request = PaymentRequestBuilder.Parse(message);

        if (request == null)
        {
            return false;
        }

        return true;
    }

    private static PostgreSQL.Models.ISOMessage CreateISOMessage(PaymentRequestBuilder.Request request, string message)
    {
        var entity = new PostgreSQL.Models.ISOMessage
        {
            MessageType = PostgreSQL.Enums.ISOMessageType.TransactionRequest,
            Date = DateTimeOffset.Now.ToUniversalTime(),
            FromBIC = request.From,
            ToBIC = request.To,
            Message = Encoding.UTF8.GetBytes(message),
            BizMsgIdr = request.BizMsgIdr,
            MsgDefIdr = request.MsgDefIdr,
            MsgId = request.MsgId,
        };
        entity.Transactions.Add(new PostgreSQL.Models.Transaction
        {
            Type = PostgreSQL.Enums.TransactionType.Deposit,
            FromBIC = request.From,
            LocalInstrument = request.LocalInstrument,
            CategoryPurpose = request.CategoryPurpose,
            EndToEndId = request.EndToEndId,
            TxId = request.TxId,
            Amount = request.Amount,
            Currency = request.Currency,

            DebtorName = request.Debtor.Name,
            DebtorAccount = request.Debtor.Account,
            DebtorAccountType = request.Debtor.AccountType,
            DebtorAgentBIC = request.Debtor.AgentBIC,
            DebtorIssuer = request.Debtor.Issuer ?? "C",

            CreditorName = request.Creditor.Name,
            CreditorAccount = request.Creditor.Account,
            CreditorAccountType = request.Creditor.AccountType,
            CreditorAgentBIC = request.Creditor.AgentBIC,
            CreditorIssuer = request.Creditor.Issuer ?? "C",
            RemittanceInformation = request.Ustrd ?? ""
        });

        return entity;
    }

    private async Task<Response<JsonObject?>> SendCallbackAsync(PaymentRequestBuilder.Request request, CancellationToken ct)
    {
        JsonObject md = _jsonAdapter.Transform(new CBPaymentRequestDto
        {
            FromBIC = request.From,
            LocalInstrument = request.LocalInstrument,
            CategoryPurpose = request.CategoryPurpose,
            EndToEndId = request.EndToEndId,
            TxId = request.TxId,
            Amount = request.Amount,
            Currency = request.Currency,
            DebtorName = request.Debtor.Name,
            DebtorAccount = request.Debtor.Account,
            DebtorAccountType = request.Debtor.AccountType,
            DebtorAgentBIC = request.Debtor.AgentBIC,
            DebtorIssuer = request.Debtor.Issuer ?? "C",
            CreditorName = request.Creditor.Name,
            CreditorAccount = request.Creditor.Account,
            CreditorAccountType = request.Creditor.AccountType,
            CreditorAgentBIC = request.Creditor.AgentBIC,
            CreditorIssuer = request.Creditor.Issuer ?? "C",
            RemittanceInformation = request.Ustrd ?? "",
            Date = request.CreDt,
            ToBIC = request.To,
            SettlementMethod = request.SettlementMethod.ToString(),
            ChargeBearer = request.ChargeBearer.ToString(),
        }, CB_PaymentRequest);

        var requestToCB = JsonSerializer.Serialize(md, _jsonSerializerOptions);
        _logger.LogDebug("Request to CB: {Request}", requestToCB);
        // Call the API to get the account details
        var content = new StringContent(requestToCB, Encoding.UTF8, "application/json");

        var responseMessage = await _httpClient.Send(_callbackLinks.Transfer!,
           new Dictionary<string, string>() {
                    { API_Key, _callbackLinks.Key! },
                    { API_Secret, _callbackLinks.Secret! }
           }, content, ct);

        _logger.LogDebug("Response from CB: {Response}", responseMessage.Data);

        return responseMessage;
    }

    private void ParseCallbackResult(JsonObject data, PaymentRequestResponseBuilder.Response response)
    {
        // convert the responseContent to a JsonObject
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);
        var md = _jsonAdapter.Transform(js!, "CB_PaymentResponse");
        var deserializedContent = _jsonAdapter.ToObject<PaymentResponseDto>(md);

        response.Status = deserializedContent?.Status;
        response.Reason = deserializedContent?.Reason;
        response.AdditionalInfo = deserializedContent?.AdditionalInfo ?? "";
        response.AcceptanceDate = deserializedContent?.AcceptanceDate ?? DateTime.UtcNow;
        response.TxId = deserializedContent?.TxId ?? "";
    }

    private async Task PersistISOMessageAsync(PostgreSQL.Models.ISOMessage isoMessage, string status, string reason, string? additionalInfo, string txId, string end2endId, string rsp, CancellationToken ct)
    {
        isoMessage.Response = Encoding.UTF8.GetBytes(rsp);
        isoMessage.Status = status == ACSC ? PostgreSQL.Enums.TransactionStatus.Success : PostgreSQL.Enums.TransactionStatus.Failed;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        isoMessage.TxId = txId;
        isoMessage.EndToEndId = end2endId;
        await _record.ISOMessageResponseAsync(isoMessage, ct);
    }
}