using System.Text;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Enums;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using Microsoft.Extensions.Logging;
using SIPS.PostgreSQL.Models;
using System.Text.Json;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Correlation;
namespace SIPS.Core.Services;
public sealed class OutgoingTransactionStatusHandler(
    ISO20022Options options,
    ILogger<OutgoingTransactionStatusHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IIncomingRecorder record,
    ISignatureService signature,
    IPersistenceGateway persistence,
    ICorrelationService correlation
    ) : IOutgoingTransactionStatusHandler
{
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ISO20022Options _configuration = options;
    private readonly ILogger<OutgoingTransactionStatusHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly INativeVerifier _verifier = verifier;
    private readonly IIncomingRecorder _record = record;
    private readonly ISignatureService _signature = signature;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    public async Task<Response<PaymentResponseDto>> HandleAsync(StatusRequestDto message, CancellationToken ct)
    {
        var fromBIC = _configuration.BIC ?? throw new InvalidOperationException("BIC not found in configuration.");
        var url = _configuration.SIPS ?? throw new InvalidOperationException("SIPS not found in configuration.");
        var cid = _correlation.Create(message.TxId);

        try
        {
            // Step 1: Retrieve ISO message by TxId
            var isoMessage = await _persistence.GetISOMessageByTxIdAsync(message.TxId, ct);
            _logger.LogInformation("[{CorrelationId}] Retrieved ISO message: {ISOMessage}", cid, JsonSerializer.Serialize(isoMessage, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));
            if (isoMessage == null)
                return Response<PaymentResponseDto>.Fail("Transaction not found", System.Net.HttpStatusCode.NotFound);

            // Step 2: Build and sign request
            if (!BuildRequest(fromBIC, isoMessage, message, out var request))
                return Response<PaymentResponseDto>.Fail("Failed to build the request.", System.Net.HttpStatusCode.BadRequest);
            var signed = _signer.SignEnvelope(request);
            isoMessage.Round++;
            var record = await CreateISOMessageAsync(signed, isoMessage, ct);

            // Step 3: Call SIPS and handle response
            var responseMessage = await SendRequestAsync(url, signed, ct, cid);
            var responseMessageStatus = await HandleSIPSCallExceptionAsync(record, responseMessage, ct);
            if (!responseMessageStatus.IsSuccess)
                return responseMessageStatus;

            // Step 4: Parse and persist SIPS response
            if (!TryParse(responseMessage?.Data!, out var rs) || rs == null)
            {
                _logger.LogError("[{CorrelationId}] Failed to parse the message: {message}", cid, responseMessage?.Data ?? "");
                await PersistISOMessageAsync(record, RJCT, "Failed to parse the message", "Failed to parse the message", responseMessage?.Data!, ct);
                return Response<PaymentResponseDto>.Fail("Failed to parse the message.", System.Net.HttpStatusCode.BadRequest);
            }
            await PersistISOMessageAsync(record, rs.Status ?? RJCT, rs.Reason ?? MISS, rs.AdditionalInfo ?? string.Empty, responseMessage!.Data!, ct);

            // Step 5: Return success response
            return Response<PaymentResponseDto>.Success(new PaymentResponseDto
            {
                Status = rs.Status ?? RJCT,
                AcceptanceDate = rs.AcceptanceDate,
                TxId = rs.TxId ?? string.Empty,
                EndToEndId = rs.Original?.EndToEndId ?? string.Empty,
                Reason = rs.Reason ?? string.Empty,
                AdditionalInfo = rs.AdditionalInfo ?? string.Empty
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("[{CorrelationId}] Failed to Send Request To SIPS Error: {Error}", cid, ex);
            return Response<PaymentResponseDto>.Fail("Failed to Send Request To SIPS", System.Net.HttpStatusCode.InternalServerError);
        }
    }

    private static bool BuildRequest(string fromBIC, ISOMessage isoMessage, StatusRequestDto message, out string request)
    {
        request = PaymentStatusRequestBuilder.Build(new PaymentStatusRequestBuilder.Request
        {
            From = fromBIC,
            To = isoMessage.FromBIC,
            MsgDefIdr = SupportedMessageTypes.CreditTransferStatusRequest.Id,
            OriginalEndToEnd = isoMessage.EndToEndId!,
            OrgnlTxId = message.TxId,
            MsgId = Transformers.GenerateId(fromBIC),
            CreDt = DateTime.UtcNow,
        });

        return request != null;
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

        return await _persistence.RecordISOMessageStatusAsync(entity, ct);
    }

    private async Task<Response<string>?> SendRequestAsync(string url, string signed, CancellationToken ct, string cid)
    {
        var content = new StringContent(signed, Encoding.UTF8, "application/xml");
        // Log the callback URL and payload
        _logger.LogInformation("[{CorrelationId}] Callback URL: {Url}", cid, url);
        _logger.LogInformation("[{CorrelationId}] Callback Payload: {Payload}", cid, signed);
        return await _httpClient.Send4XML(url, content, ct);
    }

    private async Task<Response<PaymentResponseDto>> HandleSIPSCallExceptionAsync(
    ISOMessageStatus record,
    Response<string>? responseMessage,
    CancellationToken ct)
    {
        // Check for null or missing data
        if (responseMessage == null || responseMessage.Data == null)
        {
            return await LogPersistAndReturnAsync(
                record,
                logMessage: "Failed to get valid response from SIPS",
                persistMessage: "Failed to get valid response from SIPS",
                data: "",
                failMessage: "Failed to get Valid Response from SIPS",
                statusCode: System.Net.HttpStatusCode.BadRequest,
                ct: ct);
        }

        // Handle timeout or bad gateway responses
        if (responseMessage.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.BadGateway)
        {
            return await LogPersistAndReturnAsync(
                record,
                logMessage: responseMessage.Message,
                persistMessage: "Request to SIPS timed out!",
                data: responseMessage.Data,
                failMessage: "Request to SIPS timed out!",
                statusCode: responseMessage.StatusCode,
                ct: ct);
        }

        // Handle bad request or unauthorized responses
        if (responseMessage.StatusCode == System.Net.HttpStatusCode.BadRequest ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return await LogPersistAndReturnAsync(
                record,
                logMessage: responseMessage.Message,
                persistMessage: "Failed to get valid response from SIPS",
                data: responseMessage.Data,
                failMessage: "Failed to get Valid Response from SIPS",
                statusCode: responseMessage.StatusCode,
                ct: ct);
        }

        // Verify signature
        var (ok, verbose) = await _signature.VerifyAsync(responseMessage.Data, ct);
        if (!ok)
        {
            _logger.LogError("Failed to verify the signature: {Verbose}", responseMessage.Data);
            await PersistISOMessageAsync(record, RJCT, "Failed to verify the signature", "Failed to verify the signature", responseMessage.Data, ct);
            return Response<PaymentResponseDto>.Fail("Failed to verify the signature from SIPS.", System.Net.HttpStatusCode.BadRequest);
        }

        // If all checks pass, return a successful response.
        return Response<PaymentResponseDto>.Success(new PaymentResponseDto
        {
            Status = ACSC,
        });
    }

    // verification delegated to shared signature service

    private async Task<Response<PaymentResponseDto>> LogPersistAndReturnAsync(
        ISOMessageStatus record,
        string logMessage,
        string persistMessage,
        string data,
        string failMessage,
        System.Net.HttpStatusCode statusCode,
        CancellationToken ct)
    {
        _logger.LogError("Failed to receive valid response from SIPS: {Message}", logMessage);
        await PersistISOMessageAsync(record, RJCT, persistMessage, persistMessage, data, ct);
        return Response<PaymentResponseDto>.Fail(failMessage, statusCode);
    }

    private async Task PersistISOMessageAsync(ISOMessageStatus isoMessage, string status, string reason, string? additionalInfo, string rsp, CancellationToken ct)
    {
        isoMessage.Response = Encoding.UTF8.GetBytes(rsp);
        isoMessage.Status = status == ACSC ? TransactionStatus.Success : TransactionStatus.Failed;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        // Persist the message status
        isoMessage.ISOMessage.Status = status == ACSC ? TransactionStatus.Success : TransactionStatus.Failed;
        await _persistence.ISOMessageStatusResponseAsync(isoMessage, ct);
    }

    private static bool TryParse(string message, out PaymentRequestResponseBuilder.Response? response)
    {
        response = PaymentRequestResponseBuilder.Parse(message);

        if (response == null)
        {
            return false;
        }

        return true;
    }
}