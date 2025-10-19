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
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Services;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Callback;

namespace SIPS.Core.Services;

public sealed class IncomingVerificationHandler(
    ISO20022Options options,
    ILogger<IncomingVerificationHandler> logger,
    INativeSigner signer,
    IJsonAdapter jsonAdapter,
    IIncomingRecorder record,
    IPayeeVerificationRequestParser parser,
    ISignatureService signature,
    ICorrelationService correlation,
    ICallbackClient callback
) : IIncomingVerificationHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly ILogger<IncomingVerificationHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IIncomingRecorder _record = record;
    private readonly IPayeeVerificationRequestParser _parser = parser;
    private readonly ISignatureService _signature = signature;
    private readonly ICorrelationService _correlation = correlation;
    private readonly ICallbackClient _callback = callback;
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

        // Step 2: Record the incoming ISO message
        var isoMessage = await CreateISOMessage(request, message, ct);

        // Step 3: Prepare response object
        var response = BuildInitialResponse(request);

        try
        {
            // Step 4: Send callback and parse result
            var callbackResult = await SendAndParseCallbackAsync(request, response, ct, cid);

            // Step 5: Build, persist, and sign response
            var rsp = PayeeVerificationResponseBuilder.Build(response);
            await PersistISOMessageAsync(isoMessage, response.Verified ? SUCC : MISS, response.Reason, response.AdditionalInfo, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Failed to verify payee", cid);
            var rsp = PayeeVerificationResponseBuilder.Build(response);
            await PersistISOMessageAsync(isoMessage, response.Verified ? SUCC : MISS, response.Reason, response.AdditionalInfo, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
    }

    private async Task<(bool, PayeeVerificationBuilder.Request?)> VerifyAndParseAsync(string message, CancellationToken ct, string cid)
    {
        var (ok, verbose) = await _signature.VerifyAsync(message, ct);
        if (!ok)
        {
            _logger.LogError("[{CorrelationId}] Failed to verify the signature: verbose {verbose}", cid, verbose);
            return (false, null);
        }
        if (!_parser.TryParse(message, out var request))
            return (false, null);
        return (true, request);
    }

    private PayeeVerificationResponseBuilder.Request BuildInitialResponse(PayeeVerificationBuilder.Request request)
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

    private async Task<Response<JsonObject?>> SendAndParseCallbackAsync(PayeeVerificationBuilder.Request request, PayeeVerificationResponseBuilder.Request response, CancellationToken ct, string cid)
    {
        var responseMessage = await SendCallbackAsync(request, ct, cid);
        if (responseMessage.StatusCode == HttpStatusCode.OK && responseMessage.Data != null)
        {
            ParseCallbackResult(responseMessage.Data, response);
        }
        else
        {
            response.AdditionalInfo = responseMessage.Message;
        }
        return responseMessage;
    }

    private string ErrorResponse(string message)
    {
        return AdminMessage.Generate(message);
    }

    

    private async Task<ISOMessage> CreateISOMessage(PayeeVerificationBuilder.Request request, string message, CancellationToken ct)
    {
        // record the incoming message
        return await _record.ISOMessageAsync(
                   new ISOMessage
                   {
                       MessageType = PostgreSQL.Enums.ISOMessageType.VerificationRequest,
                       Date = DateTimeOffset.Now.ToUniversalTime(),
                       FromBIC = request.From,
                       ToBIC = request.To,
                       Message = Encoding.UTF8.GetBytes(message),
                       Status = PostgreSQL.Enums.TransactionStatus.Pending,
                       BizMsgIdr = request.BizMsgIdr,
                       MsgDefIdr = request.MsgDefIdr,
                       MsgId = request.MsgId,
                       TxId = request.SIPSRequestId,
                   }
               , ct);
    }

    private async Task<Response<JsonObject?>> SendCallbackAsync(PayeeVerificationBuilder.Request request, CancellationToken ct, string cid)
    {
        JsonObject md = _jsonAdapter.Transform(new CBVerificationRequestDto
        {
            Alias = request.Alias,
            Type = request.Type,
            FromBIC = request.From,
            VerificationId = request.SIPSRequestId!
        }, CB_VerificationRequest);

        var requestToCB = JsonSerializer.Serialize(md, _jsonSerializerOptions);
        // Log the callback URL and payload
        _logger.LogInformation("[{CorrelationId}] Callback URL: {Url}", cid, _callbackLinks.Verification);
        _logger.LogInformation("[{CorrelationId}] Callback Payload: {Payload}", cid, requestToCB);

        var content = new StringContent(requestToCB, Encoding.UTF8, "application/json");
        var headers = new Dictionary<string, string>() {
            { API_Key, _callbackLinks.Key! },
            { API_Secret, _callbackLinks.Secret! }
        };
        var responseMessage = await _callback.SendAsync(_callbackLinks.Verification!, headers, content, ct, cid);

        return responseMessage;
    }

    private void ParseCallbackResult(JsonObject data, PayeeVerificationResponseBuilder.Request response)
    {
        _logger.LogInformation("Callback Response: {Response}", data.ToJsonString(_jsonSerializerOptions));
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);
        var md = _jsonAdapter.Transform(js!, CB_VerificationResponse);
        var deserializedContent = _jsonAdapter.ToObject<VerificationResponseDto>(md);

        response.Verified = deserializedContent?.IsVerified ?? false;
        response.Reason = response.Verified ? SUCC : MISS;
        response.Id = deserializedContent?.Id ?? string.Empty;
        response.Type = IBAN;
        response.Name = deserializedContent?.Name ?? string.Empty;
        response.Address = deserializedContent?.Address ?? string.Empty;
        response.Currency = deserializedContent?.Currency ?? string.Empty;
    }
    private async Task PersistISOMessageAsync(ISOMessage isoMessage, string status, string reason, string? additionalInfo, string rsp, CancellationToken ct)
    {
        isoMessage.Response = Encoding.UTF8.GetBytes(rsp);
        isoMessage.Status = status == SUCC ? PostgreSQL.Enums.TransactionStatus.Success : PostgreSQL.Enums.TransactionStatus.Failed;
        isoMessage.Reason = reason;
        await _record.ISOMessageResponseAsync(isoMessage, ct);
    }
}