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
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Services;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Implementations;

namespace SIPS.Core.Services;

public sealed class IncomingVerificationHandler(
    ISO20022Options options,
    ILogger<IncomingVerificationHandler> logger,
    INativeSigner signer,
    IJsonAdapter jsonAdapter,
    IPersistenceGateway persistence,
    IPayeeVerificationRequestParser parser,
    ISignatureService signature,
    ICorrelationService correlation,
    ICallbackClient callback,
    IInboundMessageService inbound,
    ICallbackOrchestrator callbacks,
    IISOMessageService isoService
) : IIncomingVerificationHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly ILogger<IncomingVerificationHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly IPayeeVerificationRequestParser _parser = parser;
    private readonly ISignatureService _signature = signature;
    private readonly ICorrelationService _correlation = correlation;
    private readonly ICallbackClient _callback = callback;
    private readonly IInboundMessageService _inbound = inbound;
    private readonly ICallbackOrchestrator _callbacks = callbacks;
    private readonly IISOMessageService _isoService = isoService;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Compatibility constructor for tests and existing code paths
    public IncomingVerificationHandler(
        ISO20022Options options,
        ILogger<IncomingVerificationHandler> logger,
        INativeSigner signer,
        IJsonAdapter jsonAdapter,
        IPersistenceGateway persistence,
        IPayeeVerificationRequestParser parser,
        ISignatureService signature,
        ICorrelationService correlation,
        ICallbackClient callback)
        : this(options, logger, signer, jsonAdapter, persistence, parser, signature, correlation, callback,
              new InboundMessageService(signature),
              new CallbackOrchestrator(),
              new ISOMessageService(persistence))
    {
    }

    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        var cid = _correlation.Create();
        // Step 1: Verify signature and parse message via helper
        var (isValid, request) = await _inbound.VerifyAndParseAsync(
            message,
            (xml) =>
            {
                if (!_parser.TryParse(xml, out var req)) return (false, (PayeeVerificationBuilder.Request?)null);
                return (true, req);
            },
            ct,
            cid);
        if (!isValid || request == null)
            return ErrorResponse("Failed to verify the signature or parse the message.");

        // Step 2: Record the incoming ISO message via helper
        var isoMessage = await _isoService.RecordIncomingVerificationAsync(request, message, ct);

        // Step 3: Prepare response object
        var response = BuildInitialResponse(request);

        try
        {
            // Step 4: Send callback and parse result via orchestrator
            var headers = new Dictionary<string, string>() {
                { API_Key, _callbackLinks.Key! },
                { API_Secret, _callbackLinks.Secret! }
            };
            // Normalize alias and type prior to CoreBank matching
            var normalizedAlias = (request.Alias ?? string.Empty).Trim();
            if (normalizedAlias.StartsWith("USD:", StringComparison.OrdinalIgnoreCase))
                normalizedAlias = normalizedAlias.Substring(4).TrimStart();
            var normalizedType = request.Type;
            if (normalizedAlias.StartsWith("SO", StringComparison.OrdinalIgnoreCase))
                normalizedType = IBAN;

            var dto = new CBVerificationRequestDto
            {
                Alias = normalizedAlias,
                Type = normalizedType,
                FromBIC = request.From,
                VerificationId = request.SIPSRequestId!
            };
            var responseMessage = await _callbacks.SendJsonAsync(
                _callbackLinks.Verification!,
                headers,
                dto,
                CB_VerificationRequest,
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
                response.AdditionalInfo = responseMessage.Message;
            }

            // Step 5: Build, persist, and sign response via helper
            var rsp = PayeeVerificationResponseBuilder.Build(response);
            await _isoService.PersistResponseAsync(isoMessage, response.Verified ? SUCC : MISS, response.Reason, response.AdditionalInfo, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Failed to verify payee", cid);
            var rsp = PayeeVerificationResponseBuilder.Build(response);
            await _isoService.PersistResponseAsync(isoMessage, response.Verified ? SUCC : MISS, response.Reason, response.AdditionalInfo, rsp, ct);
            return _signer.SignEnvelope(rsp);
        }
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

    private string ErrorResponse(string message)
    {
        return AdminMessage.Generate(message);
    }
}