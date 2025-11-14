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
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
namespace SIPS.Core.Services;
public sealed class OutgoingTransactionStatusHandler(
    ISO20022Options options,
    ILogger<OutgoingTransactionStatusHandler> logger,
    INativeSigner signer,
    ISignatureService signature,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    SIPS.Core.Services.Abstractions.ISipsRequestSender sips,
    SIPS.Core.Services.Abstractions.IISOMessageService isoService,
    SIPS.Core.Services.Abstractions.IStatusOrchestrator statusOrchestrator,
    IOptions<CoreOptions> coreOptions
    ) : IOutgoingTransactionStatusHandler
{
    private readonly ISO20022Options _configuration = options;
    private readonly ILogger<OutgoingTransactionStatusHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly ISignatureService _signature = signature;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly SIPS.Core.Services.Abstractions.ISipsRequestSender _sips = sips;
    private readonly SIPS.Core.Services.Abstractions.IISOMessageService _isoService = isoService;
    private readonly SIPS.Core.Services.Abstractions.IStatusOrchestrator _statusOrchestrator = statusOrchestrator;
    private readonly CoreOptions _core = coreOptions.Value;
    public async Task<Response<PaymentResponseDto>> HandleAsync(StatusRequestDto message, CancellationToken ct)
    {
        var fromBIC = _configuration.BIC ?? throw new InvalidOperationException("BIC not found in configuration.");
        var url = _configuration.SIPS ?? throw new InvalidOperationException("SIPS not found in configuration.");
        var cid = _correlation.Create(message.TxId);

        try
        {
            using var dbCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            var dbCt = dbCts.Token;
            // Step 1: Retrieve ISO message by TxId (with transactions for richer context)
            var isoMessage = await _persistence.GetISOMessageWithTransactionsByTxIdAsync(message.TxId, dbCt);
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
            var record = await CreateISOMessageAsync(signed, isoMessage, dbCt);

            // Step 3: Call SIPS and handle response
            var responseMessage = await _sips.SendAsync(url, signed, ct, cid);
            var responseMessageStatus = await HandleSIPSCallExceptionAsync(record, isoMessage, responseMessage, dbCt, cid);
            if (!responseMessageStatus.IsSuccess)
                return responseMessageStatus;

            // Step 4: Parse and persist SIPS response (single persist)
            if (!TryParse(responseMessage?.Data!, out var rs) || rs == null)
            {
                _logger.LogError("[{CorrelationId}] Failed to parse IPS status response: {message}", cid, responseMessage?.Data ?? "");
                await _isoService.MarkForCheckStatusAsync(isoMessage, "Failed to parse IPS status response", dbCt);
                return Response<PaymentResponseDto>.Fail("Failed to parse the message.", System.Net.HttpStatusCode.BadRequest);
            }

            // Use StatusOrchestrator to map IPS status code consistently
            var finalStatus = _statusOrchestrator.MapSingleStatus(rs.Status ?? RJCT, "IPS");

            // Single persist: update child status and response
            record.Response = Encoding.UTF8.GetBytes(responseMessage!.Data!);
            record.Status = finalStatus;
            record.Reason = rs.Reason ?? MISS;
            record.AdditionalInfo = rs.AdditionalInfo ?? string.Empty;

            // Update parent ISOMessage status to match
            isoMessage.Status = finalStatus;
            isoMessage.Reason = rs.Reason ?? MISS;
            isoMessage.AdditionalInfo = rs.AdditionalInfo ?? string.Empty;

            await _persistence.ISOMessageStatusResponseAsync(record, dbCt);

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



    private async Task<Response<PaymentResponseDto>> HandleSIPSCallExceptionAsync(
    ISOMessageStatus record,
    ISOMessage isoMessage,
    Response<string>? responseMessage,
    CancellationToken ct,
    string correlationId)
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

        // Handle timeout or bad gateway responses - mark for SAF retry
        if (responseMessage.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.BadGateway)
        {
            _logger.LogWarning("[{CorrelationId}] IPS status request timeout/gateway error - marking for SAF retry. Status: {Status}",
                correlationId, responseMessage.StatusCode);
            await _isoService.MarkForCheckStatusAsync(
                isoMessage,
                $"IPS status request timeout: {responseMessage.StatusCode}",
                ct);
            return Response<PaymentResponseDto>.Fail(
                "Request to IPS timed out - transaction marked for retry",
                responseMessage.StatusCode);
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
            _logger.LogError("[{CorrelationId}] Failed to verify IPS signature: {Verbose}", correlationId, verbose);
            await _isoService.MarkForCheckStatusAsync(
                isoMessage,
                "Failed to verify IPS signature on status response",
                ct);
            return Response<PaymentResponseDto>.Fail("Failed to verify the signature from IPS.", System.Net.HttpStatusCode.BadRequest);
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
        _logger.LogError("Failed to receive valid response from IPS: {Message}", logMessage);
        // Note: SAF marking handled in HandleSIPSCallExceptionAsync
        return Response<PaymentResponseDto>.Fail(failMessage, statusCode);
    }

    // Note: PersistISOMessageAsync removed - we now use single persist in main flow
    // Status mapping handled by StatusOrchestrator
    // Parent and child status updated together before single persist call

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