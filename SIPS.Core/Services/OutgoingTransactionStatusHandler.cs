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
    SIPS.Core.Services.Abstractions.ICallbackOrchestrator callbacks,
    SIPS.Adapter.IJsonAdapter jsonAdapter,
    SIPS.Core.Services.Callback.ICallbackClient callback,
    IOptions<CoreOptions> coreOptions
    ) : IOutgoingTransactionStatusHandler
// Note: This handler is used by SAF to check status of both:
// 1. Pending payment transactions (pacs.008)
// 2. ReadyForReturn transactions (pacs.004)
// The MessageType field distinguishes between them
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
    private readonly SIPS.Core.Services.Abstractions.ICallbackOrchestrator _callbacks = callbacks;
    private readonly SIPS.Adapter.IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly SIPS.Core.Services.Callback.ICallbackClient _callback = callback;
    private readonly CoreOptions _core = coreOptions.Value;
    private readonly System.Text.Json.JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
    };
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
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            }));
            if (isoMessage == null)
                return Response<PaymentResponseDto>.Fail("Transaction not found", System.Net.HttpStatusCode.NotFound);

            // Guard: Don't reprocess transactions with terminal statuses
            if (isoMessage.Status == TransactionStatus.Success ||
                isoMessage.Status == TransactionStatus.Failed ||
                isoMessage.Status == TransactionStatus.ReadyForReturn)
            {
                _logger.LogInformation("[{CorrelationId}] Transaction {TxId} already has terminal status {Status}. Returning cached status without reprocessing.",
                    cid, isoMessage.TxId, isoMessage.Status);

                // Return the existing status without making a new IPS call
                return Response<PaymentResponseDto>.Success(new PaymentResponseDto
                {
                    Status = _statusOrchestrator.MapToIsoStatusCode(isoMessage.Status),
                    TxId = isoMessage.TxId ?? string.Empty,
                    EndToEndId = isoMessage.EndToEndId ?? string.Empty,
                    Reason = isoMessage.Reason ?? string.Empty,
                    AdditionalInfo = isoMessage.AdditionalInfo ?? string.Empty,
                    AcceptanceDate = default
                });
            }

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

            // Check if this is an INCOMING return scenario (original payment marked ReadyForReturn)
            // This happens when IncomingReturnTransactionHandler received pacs.004 but pacs.002 never arrived
            // If confirmed via SAF, we need to trigger CoreBank callback to complete the return
            var wasReadyForReturn = isoMessage.Status == TransactionStatus.ReadyForReturn;
            var isOriginalPayment = isoMessage.MessageType == PostgreSQL.Enums.ISOMessageType.TransactionRequest;

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

            // If this was a ReadyForReturn payment (incoming return scenario) and status is now confirmed,
            // trigger CoreBank callback to complete the return (reverse the credit)
            if (wasReadyForReturn && isOriginalPayment && finalStatus == TransactionStatus.Success)
            {
                _logger.LogInformation("[{CorrelationId}] Incoming return for payment {TxId} confirmed via SAF. Calling CoreBank to process return.", cid, isoMessage.TxId);

                // Call CoreBank to reverse the credit
                // This is the SAF fallback path for incoming return completion when pacs.002 was delayed/missing
                var returnCompleted = await CallCoreBankReturnAsync(isoMessage, ct, cid, dbCt);

                if (!returnCompleted)
                {
                    // CBS reversal failed - revert to ReadyForReturn for manual intervention
                    _logger.LogWarning("[{CorrelationId}] CoreBank return reversal failed for TxId {TxId}. Reverting to ReadyForReturn status.", cid, isoMessage.TxId);
                    isoMessage.Status = TransactionStatus.ReadyForReturn;
                    isoMessage.Reason = "Return confirmed by IPS but CBS reversal failed";
                    isoMessage.AdditionalInfo = "Manual intervention required to complete return";

                    // Update the persisted status
                    await _persistence.ISOMessageStatusResponseAsync(record, dbCt);
                }
                else
                {
                    _logger.LogInformation("[{CorrelationId}] CoreBank return reversal completed successfully for TxId {TxId}", cid, isoMessage.TxId);
                }
            }

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

    private Task<Response<PaymentResponseDto>> LogPersistAndReturnAsync(
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
        return Task.FromResult(Response<PaymentResponseDto>.Fail(failMessage, statusCode));
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

    /// <summary>
    /// Calls CoreBank to reverse a credit for an incoming return transaction.
    /// This is triggered by SAF when a ReadyForReturn transaction is confirmed via pacs.028.
    /// This is the fallback path when pacs.002 never arrived.
    /// </summary>
    private async Task<bool> CallCoreBankReturnAsync(
        ISOMessage isoMessage,
        CancellationToken ct,
        string cid,
        CancellationToken dbCt)
    {
        try
        {
            var headers = new Dictionary<string, string>() {
                { API_Key, _configuration.Key! },
                { API_Secret, _configuration.Secret! }
            };

            var idem = isoMessage.TxId;
            if (!string.IsNullOrWhiteSpace(idem))
                headers["X-Idempotency-Key"] = idem!;
            if (!string.IsNullOrWhiteSpace(isoMessage.TxId))
                headers["X-Transaction-Id"] = isoMessage.TxId!;
            if (!string.IsNullOrWhiteSpace(isoMessage.ReturnId))
                headers["X-Return-Id"] = isoMessage.ReturnId;

            // Get transaction details for the return
            var transaction = isoMessage.Transactions.FirstOrDefault();

            // Build return request payload for CoreBank
            var returnDto = new CBReturnRequestDto
            {
                FromBIC = isoMessage.FromBIC ?? string.Empty,
                OriginalEndToEnd = transaction?.EndToEndId ?? string.Empty,
                OrgnlTxId = isoMessage.TxId ?? string.Empty,
                ReturnId = isoMessage.ReturnId ?? string.Empty,
                Reason = "Return confirmed by IPS via SAF",
                AdditionalInfo = isoMessage.AdditionalInfo ?? "Return processed via Store-and-Forward mechanism"
            };

            var result = await _callbacks.SendJsonAsync(
                _configuration.Return!,
                headers,
                returnDto,
                CB_ReturnRequest,
                _jsonAdapter,
                _correlation,
                _jsonSerializerOptions,
                _callback,
                ct,
                cid
            );

            // Guard against null callback result
            if (result == null)
            {
                _logger.LogError("[{CorrelationId}] CoreBank return callback returned null for TxId {TxId}", cid, isoMessage.TxId);
                return false;
            }

            _logger.LogInformation("[{CorrelationId}] CoreBank return callback completed for TxId {TxId}, StatusCode={StatusCode}", cid, isoMessage.TxId, result.StatusCode);

            // Parse CoreBank response
            if (result.Data == null)
            {
                _logger.LogWarning("[{CorrelationId}] CoreBank return callback returned null data for TxId {TxId}", cid, isoMessage.TxId);
                return false;
            }

            // Transform and parse the response
            var js = result.Data;
            var md = _jsonAdapter.Transform(js, "CB_PaymentResponse");
            var cbResponse = _jsonAdapter.ToObject<CBPaymentStatusResponseDto>(md);

            if (cbResponse == null || string.IsNullOrWhiteSpace(cbResponse.Status))
            {
                _logger.LogWarning("[{CorrelationId}] CoreBank return response has no status for TxId {TxId}", cid, isoMessage.TxId);
                return false;
            }

            // Check if CBS successfully reversed the credit
            // Map the CBS status - if it indicates success, the return is complete
            var (parentStatus, _, reason, additionalInfo) = _statusOrchestrator.MapCompletionStatus(
                ACSC,  // IPS confirmed the return
                cbResponse.Status,  // CBS return result
                false);

            // If CBS reversal succeeded, keep the Success status
            // If CBS reversal failed, the caller will revert to ReadyForReturn
            if (parentStatus == TransactionStatus.Success)
            {
                isoMessage.Reason = reason;
                isoMessage.AdditionalInfo = additionalInfo ?? "Return completed via SAF";
                return true;
            }
            else
            {
                _logger.LogWarning("[{CorrelationId}] CoreBank return reversal failed for TxId {TxId}. CBS Status={Status}", cid, isoMessage.TxId, cbResponse.Status);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Exception calling CoreBank return callback for TxId {TxId}", cid, isoMessage.TxId);
            return false;
        }
    }
}