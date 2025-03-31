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
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Enums;
namespace SIPS.Core.Services;
public sealed class IncomingTransactionStatusHandler(
    ISO20022Options options,
    ILogger<IncomingTransactionStatusHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IJsonAdapter jsonAdapter,
    IIncomingRecorder record
    ) : IIncomingTransactionStatusHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ILogger<IncomingTransactionStatusHandler> _logger = logger;
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

        var isoMessage = await _record.GetISOMessageByTxIdAsync(request.OrgnlTxId, ct);

        if (isoMessage == null)
        {
            // Persist the incoming message - for reference
            await CreateISOMessage(request, message, ct);
            return AdminMessage.Generate("Failed to get the Message.");
        }

        var record = await CreateISOMessageAsync(message, isoMessage, ct);

        var response = new PaymentStatusRequestResponseBuilder.Response
        {
            From = request.To,
            To = request.From,
            MsgDefIdr = request.MsgDefIdr,
            BizMsgIdr = request.BizMsgIdr,
            MsgId = request.MsgId,
            CreDt = request.CreDt,
            Status = RJCT,
            Reason = MISS,
            Original = new PaymentRequestBuilder.Request
            {
                From = request.From,
                To = request.To,
                MsgDefIdr = request.MsgDefIdr,
                BizMsgIdr = request.BizMsgIdr,
                MsgId = request.MsgId,
                CreDt = request.CreDt,
                TxId = request.OrgnlTxId,
                EndToEndId = request.OriginalEndToEnd,
            }
        };

        try
        {
            var responseMessage = await SendCallbackAsync(request, ct);

            if (responseMessage.StatusCode == HttpStatusCode.OK && responseMessage.Data != null)
            {
                ParseCallbackResult(responseMessage.Data, response);
            }
            else
            {
                await PersistISOMessageAsync(record, RJCT, "Failed to parse the message", "Failed to parse the message", "", ct);
                response.AdditionalInfo = "Failed to get response from CB.";
            }

            // Build the response
            var rsp = PaymentStatusRequestResponseBuilder.Build(response);
            // Persist the message
            await PersistISOMessageAsync(record, response.Status, response.Reason, response.AdditionalInfo, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "INCOMING PS Handler Exception for TxId {TxId}", request.OrgnlTxId);
            response.AdditionalInfo = "Failed to transfer: " + ex.Message;
            var rsp = PaymentStatusRequestResponseBuilder.Build(response);
            await PersistISOMessageAsync(record, response.Status, response.Reason, response.AdditionalInfo, rsp, ct);
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
    private static bool TryParse(string message, out PaymentStatusRequestBuilder.Request request)
    {
        request = PaymentStatusRequestBuilder.Parse(message);

        if (request == null || request.OrgnlTxId == null || request.OriginalEndToEnd == null)
        {
            return false;
        }

        return true;
    }
    private async Task<ISO20022.Models.DTOs.Response<JsonObject?>> SendCallbackAsync(PaymentStatusRequestBuilder.Request request, CancellationToken ct)
    {
        JsonObject md = _jsonAdapter.Transform(new CBStatusRequestDto
        {
            FromBIC = request.From,
            OriginalEndToEnd = request.OriginalEndToEnd,
            OrgnlTxId = request.OrgnlTxId
        }, CB_StatusRequest);

        var requestToCB = JsonSerializer.Serialize(md, _jsonSerializerOptions);
        // Call the API to get the account details
        var content = new StringContent(requestToCB, Encoding.UTF8, "application/json");

        var responseMessage = await _httpClient.Send(_callbackLinks.Status!,
            new Dictionary<string, string>() {
                    { API_Key, _callbackLinks.Key! },
                    { API_Secret, _callbackLinks.Secret! }
            }, content, ct);

        return responseMessage;
    }
    private void ParseCallbackResult(JsonObject data, PaymentStatusRequestResponseBuilder.Response response)
    {
        Console.WriteLine("Callback result: " + data.ToString());
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);

        var md = _jsonAdapter.Transform(js!, CB_PaymentStatusResponse);

        var deserializedContent = _jsonAdapter.ToObject<CBPaymentStatusResponseDto>(md);

        if (deserializedContent == null)
        {
            response.Status = RJCT;
            response.Reason = MISS;
            return;
        }

        response.Status = deserializedContent.Status;
        response.Reason = deserializedContent.Reason;
        response.AdditionalInfo = deserializedContent.AdditionalInfo;
        response.AcceptanceDate = deserializedContent.AcceptanceDate;
        response.TxId = deserializedContent.TxId;
        response.Original.From = deserializedContent.FromBIC;
        response.Original.To = deserializedContent.ToBIC;
        response.Original.BizMsgIdr = deserializedContent.BizMsgIdr;
        response.Original.MsgId = deserializedContent.MsgId;
        response.Original.ClearingSystem = deserializedContent.ClearingSystem;
        response.Original.MsgDefIdr = deserializedContent.MsgDefIdr;
        response.Original.CreDt = deserializedContent.Date;
        response.Original.LocalInstrument = deserializedContent.LocalInstrument;
        response.Original.CategoryPurpose = deserializedContent.CategoryPurpose;
        response.Original.EndToEndId = deserializedContent.EndToEndId;
        response.Original.TxId = deserializedContent.TxId;
        response.Original.Amount = deserializedContent.Amount;
        response.Original.Currency = deserializedContent.Currency;
        response.Original.Debtor.Name = deserializedContent.DebtorName;
        response.Original.Debtor.Account = deserializedContent.DebtorAccount;
        response.Original.Debtor.AccountType = deserializedContent.DebtorAccountType;
        response.Original.Debtor.AgentBIC = deserializedContent.DebtorAgentBIC;
        response.Original.Debtor.Issuer = deserializedContent.DebtorIssuer;
        response.Original.Creditor.Name = deserializedContent.CreditorName;
        response.Original.Creditor.Account = deserializedContent.CreditorAccount;
        response.Original.Creditor.AccountType = deserializedContent.CreditorAccountType;
        response.Original.Creditor.AgentBIC = deserializedContent.CreditorAgentBIC;
        response.Original.Creditor.Issuer = deserializedContent.CreditorIssuer;
        response.Original.Ustrd = deserializedContent?.RemittanceInformation;
    }
    private async Task PersistISOMessageAsync(ISOMessageStatus isoMessage, string status, string reason, string? additionalInfo, string rsp, CancellationToken ct)
    {
        isoMessage.Response = Encoding.UTF8.GetBytes(rsp);
        isoMessage.Status = status == ACSC ? TransactionStatus.Success : TransactionStatus.Failed;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        // Persist the message status
        isoMessage.ISOMessage.Status = status == ACSC ? TransactionStatus.Success : TransactionStatus.Failed;
        await _record.ISOMessageStatusResponseAsync(isoMessage, ct);
    }
    private async Task<ISOMessageStatus> CreateISOMessageAsync(string request, ISOMessage isoMessage, CancellationToken ct)
    {
        var entity = new ISOMessageStatus
        {
            ISOMessageId = isoMessage.Id,
            Date = DateTimeOffset.Now.ToUniversalTime(),
            Message = Encoding.UTF8.GetBytes(request),
            Status = TransactionStatus.Pending,
        };

        return await _record.ISOMessageStatusAsync(entity, ct);
    }

    private async Task<ISOMessage> CreateISOMessage(PaymentStatusRequestBuilder.Request request, string message, CancellationToken ct)
    {
        // record the incoming message
        return await _record.ISOMessageAsync(
                   new ISOMessage
                   {
                       MessageType = ISOMessageType.StatusRequest,
                       Date = DateTimeOffset.Now.ToUniversalTime(),
                       FromBIC = request.From,
                       ToBIC = request.To,
                       Message = Encoding.UTF8.GetBytes(message),
                       Status = TransactionStatus.Failed,
                       Reason = "Failed to get the Message from the database.",
                       BizMsgIdr = request.BizMsgIdr,
                       MsgDefIdr = request.MsgDefIdr,
                       MsgId = request.MsgId
                   }
               , ct);
    }
}