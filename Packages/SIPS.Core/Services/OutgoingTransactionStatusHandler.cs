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
using SIPS.Core.Services.Metrics;
using static SIPS.Core.Constants;
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
        using var _totalTrack = SipsMetrics.TrackStep("Outgoing", "Status", "Total");
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
            string request;
            using (SipsMetrics.TrackStep("Outgoing", "Status", "BuildAndSign"))
            {
                if (!BuildRequest(fromBIC, isoMessage, message, out request))
                    return Response<PaymentResponseDto>.Fail("Failed to build the request.", System.Net.HttpStatusCode.BadRequest);
            }
            var signed = _signer.SignEnvelope(request);
            isoMessage.Round++;
            
            ISOMessageStatus record;
            using (SipsMetrics.TrackStep("Outgoing", "Status", "DbSave"))
            {
                record = await CreateISOMessageAsync(signed, isoMessage, dbCt);
            }

            // Step 3: Call SIPS and handle response
            Response<string> responseMessage;
            using (SipsMetrics.TrackStep("Outgoing", "Status", "SipsCall"))
            {
                responseMessage = await _sips.SendAsync(url, signed, ct, cid);
            }
            var responseMessageStatus = await HandleSIPSCallExceptionAsync(record, isoMessage, responseMessage, dbCt, cid);
            if (!responseMessageStatus.IsSuccess)
                return responseMessageStatus;

            // Step 4: Parse and persist SIPS response (single persist)
            if (!TryParse(responseMessage?.Data!, out var rs) || rs == null)
            {
                _logger.LogError("[{CorrelationId}] Failed to parse IPS status response: {message}", cid, responseMessage?.Data ?? "");
                await _isoService.MarkForCheckStatusAsync(isoMessage, "Failed to parse IPS status response", dbCt);

                // Return PDNG for SAF - will retry on next run
                return Response<PaymentResponseDto>.Success(new PaymentResponseDto
                {
                    Status = PDNG,
                    TxId = isoMessage.TxId ?? string.Empty,
                    EndToEndId = isoMessage.EndToEndId ?? string.Empty,
                    Reason = "Status check pending - IPS response parsing failed",
                    AdditionalInfo = "IPS status response invalid. Transaction marked for retry."
                });
            }

            // Use StatusOrchestrator to map IPS status code consistently
            var finalStatus = _statusOrchestrator.MapSingleStatus(rs.Status ?? RJCT, "IPS");

            // Check if this is an INCOMING return scenario (original payment marked ReadyForReturn)
            // This happens when IncomingReturnTransactionHandler received pacs.004 but pacs.002 never arrived
            // If confirmed via SAF, we need to trigger CoreBank callback to complete the return
            var wasReadyForReturn = isoMessage.Status == TransactionStatus.ReadyForReturn;
            var isTransferRequest = isoMessage.MessageType == PostgreSQL.Enums.ISOMessageType.TransactionRequest;
            var isIncoming = isoMessage.ToBIC == fromBIC; 

            // Handle completion for Transaction Requests (Payments) resolved via SAF
            if (isTransferRequest && (finalStatus == TransactionStatus.Success || finalStatus == TransactionStatus.Failed))
            {
                if (isIncoming)
                {
                    if (_core.IncludeCoreBankOnListing)
                    {
                        // Path A: Bank previously received the payment (Pending). Send Completion Notification.
                        _logger.LogInformation("[{CorrelationId}] SAF resolved status for INCOMING transaction {TxId}. Notifying CoreBank.", cid, isoMessage.TxId);
                        
                        // For Incoming, if finalStatus is Failed, we likely don't need to notify if it was never accepted by CoreBank?
                        // But if it was Pending in CoreBank, we MUST tell them it failed (RJCT).
                        // If Success, we tell them ACSC.
                        
                        var notificationSuccess = await NotifyCoreBankCompletionAsync(isoMessage, rs.Status ?? RJCT, rs.Reason, rs.AdditionalInfo, ct, cid);

                        if (!notificationSuccess)
                        {
                            _logger.LogWarning("[{CorrelationId}] CoreBank completion notification failed for TxId {TxId}. Marking for retry.", cid, isoMessage.TxId);
                            await _isoService.MarkForCheckStatusAsync(
                                 isoMessage,
                                 $"CoreBank callback failed - will retry notification. Final status: {rs.Status}",
                                 ct);
                            
                            return Response<PaymentResponseDto>.Success(new PaymentResponseDto
                            {
                                Status = PDNG,
                                TxId = isoMessage.TxId ?? string.Empty,
                                EndToEndId = isoMessage.EndToEndId ?? string.Empty,
                                Reason = "Status resolved but CoreBank notification pending",
                                AdditionalInfo = $"Final status: {rs.Status}. Notification will be retried. Notification Failure."
                            });
                        }
                    }
                    else
                    {
                        // Path B: Bank has NOT seen this payment. "Late Binding".
                        // If Success: Send Transfer Request.
                        // If Failed: Do nothing (Bank never knew about it).
                        
                        if (finalStatus == TransactionStatus.Success)
                        {
                            _logger.LogInformation("[{CorrelationId}] SAF resolved status for INCOMING transaction {TxId}. IncludeCoreBankOnListing=false, sending Transfer Request (Late Binding).", cid, isoMessage.TxId);
                            var transferSuccess = await CallCoreBankTransferAsync(isoMessage, ct, cid, dbCt);

                            if (!transferSuccess)
                            {
                                _logger.LogWarning("[{CorrelationId}] CoreBank transfer failed for TxId {TxId}. Marking for retry.", cid, isoMessage.TxId);
                                await _isoService.MarkForCheckStatusAsync(
                                     isoMessage,
                                     "CoreBank transfer failed after SAF status resolution",
                                     ct);
                                
                                return Response<PaymentResponseDto>.Success(new PaymentResponseDto
                                {
                                    Status = PDNG,
                                    TxId = isoMessage.TxId ?? string.Empty,
                                    EndToEndId = isoMessage.EndToEndId ?? string.Empty,
                                    Reason = "Status resolved but CoreBank transfer pending",
                                    AdditionalInfo = "Transfer Request failed. Will be retried."
                                });
                            }
                        }
                        else
                        {
                             _logger.LogInformation("[{CorrelationId}] SAF resolved status for INCOMING transaction {TxId} as Failed. IncludeCoreBankOnListing=false, so skipping CoreBank notification (never persisted).", cid, isoMessage.TxId);
                        }
                    }
                }
                else
                {
                    // OUTGOING Payment
                     _logger.LogInformation("[{CorrelationId}] SAF resolved status for OUTGOING transaction {TxId}. Always Permissive: Notifying CoreBank.", cid, isoMessage.TxId);

                    // Outgoing Transactions ALWAYS notify CoreBank of completion (Permissive Mode),
                    // because the Bank initiated them and is holding funds/state.
                    
                    var notificationSuccess = await NotifyCoreBankCompletionAsync(isoMessage, rs.Status ?? RJCT, rs.Reason, rs.AdditionalInfo, ct, cid);

                    if (!notificationSuccess)
                    {
                        _logger.LogWarning("[{CorrelationId}] CoreBank completion notification failed for Outgoing TxId {TxId} during SAF. Marking for retry.", cid, isoMessage.TxId);
                        
                        await _isoService.MarkForCheckStatusAsync(
                             isoMessage,
                             $"CoreBank callback failed - will retry notification. Final status: {rs.Status}",
                             ct);
                        
                        return Response<PaymentResponseDto>.Success(new PaymentResponseDto
                        {
                            Status = PDNG,
                            TxId = isoMessage.TxId ?? string.Empty,
                            EndToEndId = isoMessage.EndToEndId ?? string.Empty,
                            Reason = "Status resolved but CoreBank notification pending",
                            AdditionalInfo = $"Final status: {rs.Status}. Notification will be retried (Outgoing)."
                        });
                    }
                }
            }

            // If this was a ReadyForReturn payment (incoming return scenario) and status is now confirmed,
            // trigger CoreBank callback to complete the return (reverse the credit)
            if (wasReadyForReturn && isTransferRequest && finalStatus == TransactionStatus.Success)
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
            To = isoMessage.FromBIC == fromBIC ? isoMessage.ToBIC : isoMessage.FromBIC,
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
        // Handle timeout, bad gateway, or connection errors FIRST - mark for SAF retry
        if (responseMessage == null ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.BadGateway ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.InternalServerError)
        {
            var statusDescription = responseMessage?.StatusCode.ToString() ?? "Connection Error";
            _logger.LogWarning("[{CorrelationId}] IPS status request timeout/connection error - marking for SAF retry. Status: {Status}",
                correlationId, statusDescription);
            await _isoService.MarkForCheckStatusAsync(
                isoMessage,
                $"IPS status request timeout/connection error: {statusDescription}",
                ct);
            return Response<PaymentResponseDto>.Fail(
                "Request to IPS timed out or connection error - transaction marked for retry",
                responseMessage?.StatusCode ?? System.Net.HttpStatusCode.InternalServerError);
        }

        // Check for missing data (after timeout/connection error check)
        if (responseMessage.Data == null)
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
            _logger.LogWarning("[{CorrelationId}] Failed to verify IPS status signature: {Verbose}. Marking for SAF status check retry.", correlationId, verbose);
            await _isoService.MarkForCheckStatusAsync(
                isoMessage,
                "Failed to verify IPS signature on status response",
                ct);
            
            // Return PDNG instead of FAIL - request reached IPS, but status response signature is suspect.
            // SAF will retry the status inquiry on the next run.
            return Response<PaymentResponseDto>.Success(new PaymentResponseDto
            {
                Status = PDNG,
                TxId = isoMessage.TxId ?? string.Empty,
                EndToEndId = isoMessage.EndToEndId ?? string.Empty,
                Reason = "Pending - IPS status signature verification failed",
                AdditionalInfo = "IPS status inquiry received but response signature is invalid. Marked for retry."
            });
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
        try
        {
            response = PaymentRequestResponseBuilder.Parse(message);

            if (response == null)
            {
                return false;
            }

            return true;
        }
        catch
        {
            // XML parsing exceptions (e.g., "Root element is missing")
            response = null;
            return false;
        }
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

            var transaction = isoMessage.Transactions.FirstOrDefault();
            Response<System.Text.Json.Nodes.JsonObject?>? result = null;

            if (_core.IncludeCoreBankOnListing)
            {
                 // Path A: Bank previously approved the return (Permission Step). 
                 // Send Completion Notification to confirm finality.
                 var notificationDto = new CBCompletionNotification
                 {
                     OriginalTxId = isoMessage.TxId ?? string.Empty,
                     OriginalEndToEndId = transaction?.EndToEndId,
                     Status = ACSC,
                     Reason = "Return confirmed by IPS via SAF",
                     AdditionalInfo = isoMessage.AdditionalInfo ?? "Final return success via SAF"
                 };

                 result = await _callbacks.SendJsonAsync(
                    _configuration.CompletionNotification!,
                    headers,
                    notificationDto,
                    CB_CompletionNotification,
                    _jsonAdapter,
                    _correlation,
                    _jsonSerializerOptions,
                    _callback,
                    ct,
                    cid
                );
            }
            else
            {
                // Path B: Bank has NOT seen this return yet. Use "Late Binding" logic.
                // Send Return Request to execute the reversal.
                var returnDto = new CBReturnRequestDto
                {
                    FromBIC = isoMessage.FromBIC ?? string.Empty,
                    OriginalEndToEnd = transaction?.EndToEndId ?? string.Empty,
                    OrgnlTxId = isoMessage.TxId ?? string.Empty,
                    ReturnId = isoMessage.ReturnId ?? string.Empty,
                    Reason = "Return confirmed by IPS via SAF",
                    AdditionalInfo = isoMessage.AdditionalInfo ?? "Return processed via Store-and-Forward mechanism"
                };

                result = await _callbacks.SendJsonAsync(
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
            }

            // Guard against null callback result
            if (result == null)
            {
                _logger.LogError("[{CorrelationId}] CoreBank return callback returned null for TxId {TxId}", cid, isoMessage.TxId);
                return false;
            }

            _logger.LogInformation("[{CorrelationId}] CoreBank return callback completed for TxId {TxId}, StatusCode={StatusCode}", cid, isoMessage.TxId, result.StatusCode);

            if (result.Data == null)
            {
                _logger.LogWarning("[{CorrelationId}] CoreBank return callback returned null data for TxId {TxId}", cid, isoMessage.TxId);
                return false;
            }

            // Transform and parse the response (Structure is same for both Notification and Return Request responses: CB_PaymentResponse)
            var js = result.Data;
            var md = _jsonAdapter.Transform(js, "CB_PaymentResponse");
            var cbResponse = _jsonAdapter.ToObject<CBPaymentStatusResponseDto>(md);

            if (cbResponse == null || string.IsNullOrWhiteSpace(cbResponse.Status))
            {
                _logger.LogWarning("[{CorrelationId}] CoreBank return response has no status for TxId {TxId}", cid, isoMessage.TxId);
                return false;
            }

            // Check if CBS successfully reversed the credit (or processed notification)
            var (parentStatus, _, reason, additionalInfo) = _statusOrchestrator.MapCompletionStatus(
                ACSC,  // IPS confirmed the return
                cbResponse.Status,  // CBS result
                false);

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

    /// <summary>
    /// Notifies CoreBank of transaction completion when SAF resolves the final status.
    /// This is called when an outgoing transaction that previously timed out (returned PDNG) is now resolved.
    /// CoreBank can use this to finalize the transaction status on their side.
    /// </summary>
    private async Task<bool> NotifyCoreBankCompletionAsync(
        ISOMessage isoMessage,
        string status,
        string? reason,
        string? additionalInfo,
        CancellationToken ct,
        string cid)
    {
        try
        {
            // Only send completion notification if the URL is configured
            if (string.IsNullOrWhiteSpace(_configuration.CompletionNotification))
            {
                _logger.LogWarning("[{CorrelationId}] CompletionNotification URL not configured. Skipping CoreBank notification for TxId {TxId}", cid, isoMessage.TxId);
                return true; // Not configured is not a failure - just skip
            }

            var headers = new Dictionary<string, string>() {
                { API_Key, _configuration.Key! },
                { API_Secret, _configuration.Secret! }
            };

            var idem = isoMessage.TxId;
            if (!string.IsNullOrWhiteSpace(idem))
                headers["X-Idempotency-Key"] = idem!;
            if (!string.IsNullOrWhiteSpace(isoMessage.TxId))
                headers["X-Transaction-Id"] = isoMessage.TxId!;

            // Build completion notification payload for CoreBank
            var notificationDto = new CBCompletionNotification
            {
                OriginalTxId = isoMessage.TxId ?? string.Empty,
                OriginalEndToEndId = isoMessage.EndToEndId ?? string.Empty,
                Status = status,
                Reason = reason ?? string.Empty,
                AdditionalInfo = additionalInfo ?? "Transaction status resolved via SAF"
            };

            var result = await _callbacks.SendJsonAsync(
                _configuration.CompletionNotification!,
                headers,
                notificationDto,
                CB_CompletionNotification,
                _jsonAdapter,
                _correlation,
                _jsonSerializerOptions,
                _callback,
                ct,
                cid
            );

            if (result == null)
            {
                _logger.LogWarning("[{CorrelationId}] CoreBank completion notification returned null for TxId {TxId}", cid, isoMessage.TxId);
                return false; // Null response = failure, should retry
            }

            // Check for successful HTTP status codes (2xx)
            if (result.StatusCode >= System.Net.HttpStatusCode.OK && result.StatusCode < System.Net.HttpStatusCode.MultipleChoices)
            {
                _logger.LogInformation("[{CorrelationId}] CoreBank completion notification sent successfully for TxId {TxId}, StatusCode={StatusCode}",
                    cid, isoMessage.TxId, result.StatusCode);
                return true;
            }

            _logger.LogWarning("[{CorrelationId}] CoreBank completion notification failed for TxId {TxId}, StatusCode={StatusCode}",
                cid, isoMessage.TxId, result.StatusCode);
            return false; // Non-2xx status = failure, should retry
        }
        catch (Exception ex)
        {
            // Network errors, timeouts, etc. should trigger retry
            _logger.LogError(ex, "[{CorrelationId}] Exception sending CoreBank completion notification for TxId {TxId}", cid, isoMessage.TxId);
            return false; // Exception = failure, should retry
        }
    }

    /// <summary>
    /// Calls CoreBank to execute a Transfer (Late Binding) for an incoming payment.
    /// This is triggered by SAF when an Incoming Payment (Pending) is confirmed as Success via pacs.028,
    /// AND IncludeCoreBankOnListing is false (meaning Bank hasn't seen it yet).
    /// </summary>
    private async Task<bool> CallCoreBankTransferAsync(
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

            var transaction = isoMessage.Transactions.FirstOrDefault();
            
            var idem = isoMessage.TxId;
            if (!string.IsNullOrWhiteSpace(idem))
                headers["X-Idempotency-Key"] = idem!;
            if (!string.IsNullOrWhiteSpace(isoMessage.TxId))
                headers["X-Transaction-Id"] = isoMessage.TxId!;

            // Build Transfer Request DTO
            // Use properties from the stored Transaction, falling back to empty strings if missing
            var dto = new CBPaymentRequestDto
            {
                FromBIC = transaction?.FromBIC ?? string.Empty,
                LocalInstrument = transaction?.LocalInstrument ?? string.Empty,
                CategoryPurpose = transaction?.CategoryPurpose ?? string.Empty,
                EndToEndId = transaction?.EndToEndId ?? string.Empty,
                TxId = transaction?.TxId ?? isoMessage.TxId ?? string.Empty,
                Amount = transaction?.Amount ?? 0,
                Currency = transaction?.Currency ?? string.Empty,
                DebtorName = transaction?.DebtorName ?? string.Empty,
                DebtorAccount = transaction?.DebtorAccount ?? string.Empty,
                DebtorAddress = transaction?.DebtorAddress ?? string.Empty,
                DebtorAccountType = transaction?.DebtorAccountType ?? string.Empty,
                DebtorAgentBIC = transaction?.DebtorAgentBIC ?? string.Empty,
                DebtorIssuer = transaction?.DebtorIssuer ?? string.Empty,
                CreditorName = transaction?.CreditorName ?? string.Empty,
                CreditorAccount = transaction?.CreditorAccount ?? string.Empty,
                CreditorAddress = transaction?.CreditorAddress ?? string.Empty,
                CreditorAccountType = transaction?.CreditorAccountType ?? string.Empty,

                CreditorAgentBIC = transaction?.CreditorAgentBIC ?? string.Empty,
                CreditorIssuer = transaction?.CreditorIssuer ?? string.Empty,
                RemittanceInformation = transaction?.RemittanceInformation ?? string.Empty,
                Date = DateTime.UtcNow,
                ToBIC = isoMessage.ToBIC ?? string.Empty, // Target is Us (Receiver)
                SettlementMethod = "CLRG",
                ChargeBearer = "SLEV",
                BizMsgIdr = isoMessage.BizMsgIdr ?? string.Empty,
                MsgDefIdr = isoMessage.MsgDefIdr ?? string.Empty,
                ClearingSystem = string.Empty,
                MsgId = isoMessage.MsgId ?? string.Empty
            };

            var result = await _callbacks.SendJsonAsync(
                _configuration.Transfer!,
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

            // Guard against null callback result
            if (result == null)
            {
                _logger.LogError("[{CorrelationId}] CoreBank transfer callback returned null for TxId {TxId}", cid, isoMessage.TxId);
                return false;
            }

            _logger.LogInformation("[{CorrelationId}] CoreBank transfer callback completed for TxId {TxId}, StatusCode={StatusCode}", cid, isoMessage.TxId, result.StatusCode);

            if (result.Data == null)
            {
                _logger.LogWarning("[{CorrelationId}] CoreBank transfer callback returned null data for TxId {TxId}", cid, isoMessage.TxId);
                return false;
            }

            // Transform and parse the response
            var js = result.Data;
            var md = _jsonAdapter.Transform(js, "CB_PaymentResponse");
            var cbResponse = _jsonAdapter.ToObject<CBPaymentStatusResponseDto>(md);

            if (cbResponse == null || string.IsNullOrWhiteSpace(cbResponse.Status))
            {
                _logger.LogWarning("[{CorrelationId}] CoreBank transfer response has no status for TxId {TxId}", cid, isoMessage.TxId);
                return false;
            }

            // Check if CBS processed the transfer successfully
            // We use MapCompletionStatus to check if the CBS status maps to Success
            var (parentStatus, _, reason, additionalInfo) = _statusOrchestrator.MapCompletionStatus(
                ACSC,  // IPS confirmed Success
                cbResponse.Status,  // CBS result
                false);

            if (parentStatus == TransactionStatus.Success)
            {
                isoMessage.Reason = reason;
                isoMessage.AdditionalInfo = additionalInfo ?? "Transfer completed via SAF";
                return true;
            }
            else
            {
                _logger.LogWarning("[{CorrelationId}] CoreBank transfer failed for TxId {TxId}. CBS Status={Status}", cid, isoMessage.TxId, cbResponse.Status);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Exception calling CoreBank transfer callback for TxId {TxId}", cid, isoMessage.TxId);
            return false;
        }
    }
}