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
namespace SIPS.Core.Services;
public sealed class IncomingReturnTransactionHandler(
    ISO20022Options options,
    ILogger<IncomingReturnTransactionHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IJsonAdapter jsonAdapter,
    IIncomingRecorder record,
    ISignatureService signature,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    ICallbackClient callback,
    IResponseFactory responses
    ) : IIncomingReturnTransactionHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ILogger<IncomingReturnTransactionHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IIncomingRecorder _record = record;
    private readonly ISignatureService _signature = signature;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly ICallbackClient _callback = callback;
    private readonly IResponseFactory _responses = responses;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        var cid = _correlation.Create();
        // Step 1: Verify signature and parse message
        var (isValid, request) = await VerifyAndParseAsync(message, ct, cid);
        if (!isValid || request == null)
            return ErrorResponse("Failed to verify the signature or parse the message.");

        // Step 2: Save the message as you received it
        var entity = CreateISOMessage(request, message);
        var record = await _persistence.RecordISOMessageAsync(entity, ct);

        // Step 3: Get the original message
        var originalMessage = await _persistence.GetISOMessageWithTransactionsByTxIdAsync(request.OrgnlTxId, ct);
        var response = BuildInitialResponse(request);
        _logger.LogInformation("[{CorrelationId}] Retrieved original message response (IRTH): {response}", cid, JsonSerializer.Serialize(response, _jsonSerializerOptions));

        // Step 4: Check if the message is a transaction request message and if it is not null
        if (originalMessage == null || originalMessage.MessageType != PostgreSQL.Enums.ISOMessageType.TransactionRequest)
        {
            response.AdditionalInfo = "Failed to get the Message.";
            response.Reason = MISS;
            response.Status = RJCT;
            var rsp = ReturnPaymentResponseBuilder.Build(response);
            await PersistISOMessageAsync(record, response.Status ?? RJCT, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, response.TxId ?? string.Empty, request.OriginalEndToEnd ?? string.Empty, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }

        try
        {
            // Step 5: Send callback and parse result
            var callbackResult = await SendAndParseCallbackAsync(request, response, originalMessage, ct, cid);
            _logger.LogInformation("[{CorrelationId}] Callback result: {CallbackResult}", cid, JsonSerializer.Serialize(callbackResult, _jsonSerializerOptions));
            // Step 6: Build, persist, and sign response
            var rsp = ReturnPaymentResponseBuilder.Build(response);
            _logger.LogInformation("[{CorrelationId}] Built response (IRTH): {Response}", cid, rsp);
            await PersistISOMessageAsync(record, response.Status ?? RJCT, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] INCOMING PS Handler Exception for TxId {TxId}", cid, request.OrgnlTxId);
            response.AdditionalInfo = "Failed to transfer: " + ex.Message;
            var rsp = ReturnPaymentResponseBuilder.Build(response);
            await PersistISOMessageAsync(record, response.Status ?? RJCT, response.Reason ?? MISS, response.AdditionalInfo ?? string.Empty, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
    }

    private async Task<(bool, ReturnPaymentRequestBuilder.Request?)> VerifyAndParseAsync(string message, CancellationToken ct, string cid)
    {
        var (ok, verbose) = await _signature.VerifyAsync(message, ct);
        if (!ok)
        {
            _logger.LogError("[{CorrelationId}] Failed to verify the signature: verbose {verbose}", cid, verbose);
            return (false, null);
        }
        if (!TryParse(message, out var request))
            return (false, null);
        return (true, request);
    }

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
            Status = RJCT,
            Reason = MISS,
            AdditionalInfo = "Failed to get the Message.",
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

    private async Task<ISO20022.Models.DTOs.Response<JsonObject?>> SendAndParseCallbackAsync(ReturnPaymentRequestBuilder.Request request, ReturnPaymentResponseBuilder.Response response, PostgreSQL.Models.ISOMessage originalMessage, CancellationToken ct, string cid)
    {
        var responseMessage = await SendCallbackAsync(request, ct, cid);
        if (responseMessage.StatusCode == HttpStatusCode.OK && responseMessage.Data != null)
        {
            ParseCallbackResult(responseMessage.Data, response, originalMessage);
        }
        else
        {
            response.AdditionalInfo = "Failed to get response from CB.";
        }
        return responseMessage;
    }

    private string ErrorResponse(string message)
    {
        return AdminMessage.Generate(message);
    }

    private static bool TryParse(string message, out ReturnPaymentRequestBuilder.Request request)
    {
        request = ReturnPaymentRequestBuilder.Parse(message);

        if (request == null || request.OrgnlTxId == null || request.OriginalEndToEnd == null || request.ReturnId == null)
        {
            return false;
        }

        return true;
    }
    private async Task<ISO20022.Models.DTOs.Response<JsonObject?>> SendCallbackAsync(ReturnPaymentRequestBuilder.Request request, CancellationToken ct, string cid)
    {
        JsonObject md = _jsonAdapter.Transform(new CBReturnRequestDto
        {
            FromBIC = request.From,
            OriginalEndToEnd = request.OriginalEndToEnd,
            OrgnlTxId = request.OrgnlTxId,
            ReturnId = request.ReturnId,
            Reason = request.ReturnReason,
            AdditionalInfo = request.AdditionalInfo,
        }, CB_ReturnRequest);

        var requestToCB = JsonSerializer.Serialize(md, _jsonSerializerOptions);
        // Log the callback URL and payload
        _logger.LogInformation("[{CorrelationId}] Callback URL: {Url}", cid, _callbackLinks.Return);
        _logger.LogInformation("[{CorrelationId}] Callback Payload: {Payload}", cid, requestToCB);

        var content = new StringContent(requestToCB, Encoding.UTF8, "application/json");

        var responseMessage = await _callback.SendAsync(_callbackLinks.Return!,
            new Dictionary<string, string>() {
                    { API_Key, _callbackLinks.Key! },
                    { API_Secret, _callbackLinks.Secret! }
            }, content, ct, cid);

        return responseMessage;
    }
    private void ParseCallbackResult(JsonObject data, ReturnPaymentResponseBuilder.Response response, PostgreSQL.Models.ISOMessage originalMessage)
    {
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);

        var md = _jsonAdapter.Transform(js!, CB_ReturnResponse);

        var deserializedContent = _jsonAdapter.ToObject<CBReturnResponseDto>(md);

        if (deserializedContent == null)
        {
            response.Status = RJCT;
            response.Reason = MISS;
            response.AdditionalInfo = "Failed to parse the message.";
            return;
        }

        var originalTransaction = originalMessage.Transactions.FirstOrDefault();

        response.Status = deserializedContent.Status ?? RJCT;
        response.Reason = deserializedContent.Reason ?? string.Empty;
        response.AdditionalInfo = deserializedContent.AdditionalInfo ?? string.Empty;
        response.TxId = deserializedContent.OrgnlTxId ?? string.Empty;

        response.Original.From = originalMessage.FromBIC ?? string.Empty;
        response.Original.To = originalMessage.ToBIC ?? string.Empty;
        response.Original.BizMsgIdr = originalMessage.BizMsgIdr ?? string.Empty;
        response.Original.MsgId = originalMessage.MsgId ?? string.Empty;
        response.Original.ClearingSystem = "FP";
        response.Original.CreDt = originalMessage.Date.UtcDateTime;
        if (originalTransaction != null)
        {
            response.Original.LocalInstrument = originalTransaction.LocalInstrument ?? string.Empty;
            response.Original.CategoryPurpose = originalTransaction.CategoryPurpose ?? string.Empty;
            response.Original.OriginalEndToEnd = originalTransaction.EndToEndId ?? string.Empty;
            response.Original.OrgnlTxId = originalTransaction.TxId ?? string.Empty;
            response.Original.OriginalAmount = originalTransaction.Amount;
            response.Original.OriginalCurrency = originalTransaction.Currency ?? string.Empty;
        }
    }
    private async Task PersistISOMessageAsync(PostgreSQL.Models.ISOMessage isoMessage, string status, string reason, string? additionalInfo, string rsp, CancellationToken ct)
    {
        isoMessage.Response = Encoding.UTF8.GetBytes(rsp);
        isoMessage.Status = status == ACSC ? PostgreSQL.Enums.TransactionStatus.Success : PostgreSQL.Enums.TransactionStatus.Failed;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        await _record.ISOMessageResponseAsync(isoMessage, ct);
    }

    private static PostgreSQL.Models.ISOMessage CreateISOMessage(ReturnPaymentRequestBuilder.Request request, string message)
    {
        var entity = new PostgreSQL.Models.ISOMessage
        {
            MessageType = PostgreSQL.Enums.ISOMessageType.ReturnRequest,
            Date = DateTimeOffset.Now.ToUniversalTime(),
            FromBIC = request.From,
            ToBIC = request.To,
            Message = Encoding.UTF8.GetBytes(message),
            Status = PostgreSQL.Enums.TransactionStatus.Pending,
            TxId = request.OrgnlTxId,
            EndToEndId = request.OriginalEndToEnd,
            ReturnId = request.ReturnId,
            BizMsgIdr = request.BizMsgIdr,
            MsgDefIdr = request.MsgDefIdr,
            MsgId = request.MsgId
        };
        entity.Transactions.Add(new PostgreSQL.Models.Transaction
        {
            Type = PostgreSQL.Enums.TransactionType.ReturnWithdrawal,
            FromBIC = request.From,
            LocalInstrument = request.LocalInstrument,
            CategoryPurpose = request.CategoryPurpose,
            EndToEndId = request.OriginalEndToEnd,
            TxId = request.OriginalEndToEnd,
            Amount = request.OriginalAmount,
            Currency = request.OriginalCurrency,
            DebtorAccount = string.Empty,
            CreditorAccount = string.Empty,
            DebtorAccountType = string.Empty,
            DebtorAgentBIC = string.Empty,
            DebtorIssuer = string.Empty,
            DebtorName = string.Empty,
            CreditorAccountType = string.Empty,
            CreditorAgentBIC = string.Empty,
            CreditorIssuer = string.Empty,
            CreditorName = string.Empty,
            RemittanceInformation = request.ReturnReason + " " + request.AdditionalInfo
        });

        return entity;
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