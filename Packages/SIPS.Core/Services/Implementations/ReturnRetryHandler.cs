using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SIPS.Adapter;
using SIPS.Core.Options;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Persistence;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using static SIPS.Core.Constants;

namespace SIPS.Core.Services.Implementations;

/// <summary>
/// Handles retry operations for ReadyForReturn transactions
/// </summary>
public sealed class ReturnRetryHandler(
    ISO20022Options options,
    ILogger<ReturnRetryHandler> logger,
    IJsonAdapter jsonAdapter,
    ICallbackClient callback,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    ICallbackOrchestrator callbacks,
    IStatusOrchestrator statusOrchestrator,
    IOptions<CoreOptions> coreOptions
) : IReturnRetryHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly ILogger<ReturnRetryHandler> _logger = logger;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly ICallbackClient _callback = callback;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly ICallbackOrchestrator _callbacks = callbacks;
    private readonly IStatusOrchestrator _statusOrchestrator = statusOrchestrator;
    private readonly CoreOptions _core = coreOptions.Value;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<ReturnRetryResult> RetryReturnAsync(string txId, CancellationToken ct)
    {
        var cid = _correlation.Create();
        ISOMessage? claimedMessage = null;
        _logger.LogInformation("[{CorrelationId}] Starting CoreBank retry for TxId {TxId}", cid, txId);

        try
        {
            // Step 1: Retrieve ISO message by TxId
            var isoMessage = await _persistence.GetISOMessageWithTransactionsByTxIdAsync(txId, ct);

            if (isoMessage == null)
            {
                _logger.LogWarning("[{CorrelationId}] ISO message not found for TxId {TxId}", cid, txId);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = "Transaction not found",
                    TxId = txId,
                    Status = "NotFound"
                };
            }

            // Step 2: Verify transaction is in ReadyForReturn status
            // Do not allow retrying transactions that are already successful or in other terminal states
            if (isoMessage.Status == TransactionStatus.Success)
            {
                _logger.LogInformation("[{CorrelationId}] Transaction TxId {TxId} is already successful. No retry needed.",
                    cid, txId);
                return new ReturnRetryResult
                {
                    Success = true,
                    Message = "Transaction already completed successfully",
                    TxId = txId,
                    Status = isoMessage.Status.ToString(),
                    Reason = isoMessage.Reason,
                    AdditionalInfo = isoMessage.AdditionalInfo
                };
            }

            if (isoMessage.Status != TransactionStatus.ReadyForReturn)
            {
                _logger.LogWarning("[{CorrelationId}] Transaction TxId {TxId} is not in ReadyForReturn status. Current status: {Status}",
                    cid, txId, isoMessage.Status);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = $"Transaction is not in ReadyForReturn status. Current status: {isoMessage.Status}",
                    TxId = txId,
                    Status = isoMessage.Status.ToString()
                };
            }

            // CoreBank retries are counted separately from IPS/SAF status polling.
            const int MaxCoreBankRetries = 2;
            if (isoMessage.CoreBankRetryCount >= MaxCoreBankRetries)
            {
                _logger.LogWarning("[{CorrelationId}] Maximum CoreBank retry attempts reached for TxId {TxId}. Attempts: {RetryCount}.",
                    cid, txId, isoMessage.CoreBankRetryCount);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = $"Maximum retry attempts ({MaxCoreBankRetries}) reached. Route to dispute channel or return to IPS if within 2 calendar days.",
                    TxId = txId,
                    Status = isoMessage.Status.ToString(),
                    Reason = "Maximum retry attempts exceeded - CoreBank rejected transaction",
                    AdditionalInfo = $"CoreBank retry attempts: {isoMessage.CoreBankRetryCount}. No further automatic retries allowed. Route to dispute channel or return to IPS."
                };
            }

            // Step 4: Get transaction details
            var transaction = isoMessage.Transactions.FirstOrDefault();
            if (transaction == null)
            {
                _logger.LogWarning("[{CorrelationId}] No transaction details found for TxId {TxId}", cid, txId);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = "Transaction details not found",
                    TxId = txId,
                    Status = "NoTransactionDetails"
                };
            }

            if (string.IsNullOrWhiteSpace(transaction.TxId))
            {
                _logger.LogWarning("[{CorrelationId}] Cannot retry CoreBank operation because the transaction identifier is missing.", cid);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = "Transaction identifier is missing",
                    TxId = txId,
                    Status = isoMessage.Status.ToString(),
                    Reason = "Missing transaction identifier",
                    AdditionalInfo = "Manual intervention required"
                };
            }

            Response<System.Text.Json.Nodes.JsonObject?>? result = null;
            var retryId = $"{transaction.TxId}-retry-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var coreBankResponseKind = "CB_PaymentResponse";

            var callbackUrl = _core.IncludeCoreBankOnListing
                ? _callbackLinks.CompletionNotification
                : string.IsNullOrWhiteSpace(isoMessage.ReturnId)
                    ? _callbackLinks.Transfer
                    : _callbackLinks.Return;
            if (string.IsNullOrWhiteSpace(callbackUrl))
            {
                var operation = _core.IncludeCoreBankOnListing
                    ? "CompletionNotification"
                    : string.IsNullOrWhiteSpace(isoMessage.ReturnId) ? "Transfer" : "Return";
                _logger.LogWarning("[{CorrelationId}] {Operation} URL is not configured for CoreBank retry TxId {TxId}", cid, operation, txId);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = $"{operation} URL is not configured",
                    TxId = txId,
                    Status = isoMessage.Status.ToString(),
                    Reason = "CoreBank callback unavailable",
                    AdditionalInfo = "Manual intervention required"
                };
            }

            var claimed = await _persistence.TryClaimCoreBankRetryAsync(
                isoMessage.Id,
                isoMessage.xmin,
                MaxCoreBankRetries,
                ct);
            if (!claimed)
            {
                _logger.LogWarning("[{CorrelationId}] CoreBank retry was not claimed for TxId {TxId}; another retry is active or the record changed.", cid, txId);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = "Another retry is already in progress or the transaction changed",
                    TxId = txId,
                    Status = "RetryConflict",
                    Reason = "Concurrent retry prevented",
                    AdditionalInfo = "Refresh the transaction before retrying again"
                };
            }

            claimedMessage = isoMessage;
            isoMessage.CoreBankRetryCount++;
            isoMessage.Status = TransactionStatus.ReadyForReturn;

            if (_core.IncludeCoreBankOnListing)
            {
                // Path A: Permission enabled. Bank already saw the request. Send Completion Notification.
                var notificationHeaders = new Dictionary<string, string>
                {
                    { API_Key, _callbackLinks.Key! },
                    { API_Secret, _callbackLinks.Secret! }
                };
                if (!string.IsNullOrWhiteSpace(transaction.TxId))
                {
                    notificationHeaders["X-Transaction-Id"] = transaction.TxId;
                    notificationHeaders["X-Idempotency-Key"] = string.IsNullOrWhiteSpace(isoMessage.ReturnId)
                        ? transaction.TxId
                        : isoMessage.ReturnId;
                }
                if (!string.IsNullOrWhiteSpace(isoMessage.ReturnId))
                    notificationHeaders["X-Return-Id"] = isoMessage.ReturnId;

                var notificationDto = new CBCompletionNotification
                {
                    OriginalTxId = transaction.TxId ?? string.Empty,
                    OriginalEndToEndId = transaction.EndToEndId,
                    Status = ACSC,
                    Reason = isoMessage.Reason ?? "Return retry confirmed",
                    AdditionalInfo = isoMessage.AdditionalInfo ?? "Retry successful"
                };

                _logger.LogInformation("[{CorrelationId}] Calling CoreBank CompletionNotification endpoint for TxId {TxId} (Retry Flow)", cid, txId);
                using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));
                result = await _callbacks.SendJsonAsync(
                    _callbackLinks.CompletionNotification!,
                    notificationHeaders,
                    notificationDto,
                    CB_CompletionNotification,
                    _jsonAdapter,
                    _correlation,
                    _jsonSerializerOptions,
                    _callback,
                    coreBankCts.Token,
                    cid
                );
                coreBankResponseKind = CB_CompletionNotificationResponse;
            }
            else
            {
                // Path B: Permission disabled. The bank has not executed the operation yet.
                // ReadyForReturn has two meanings:
                // - no ReturnId: the original incoming payment was accepted by IPS but was not
                //   credited in CoreBank, so retry the Transfer request;
                // - ReturnId present: an actual return was confirmed and the CoreBank reversal
                //   still needs to be executed, so retry the Return request.
                var headers = new Dictionary<string, string>
                {
                    { API_Key, _callbackLinks.Key! },
                    { API_Secret, _callbackLinks.Secret! }
                };

                if (!string.IsNullOrWhiteSpace(transaction.TxId))
                    headers["X-Transaction-Id"] = transaction.TxId;
                
                headers["X-Idempotency-Key"] = string.IsNullOrWhiteSpace(isoMessage.ReturnId)
                    ? transaction.TxId
                    : isoMessage.ReturnId;
                if (!string.IsNullOrWhiteSpace(retryId))
                    headers["X-Retry-Id"] = retryId;

                using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));

                if (string.IsNullOrWhiteSpace(isoMessage.ReturnId))
                {
                    var paymentDto = new CBPaymentRequestDto
                    {
                        FromBIC = transaction.FromBIC ?? string.Empty,
                        LocalInstrument = transaction.LocalInstrument ?? string.Empty,
                        CategoryPurpose = transaction.CategoryPurpose ?? string.Empty,
                        EndToEndId = transaction.EndToEndId ?? string.Empty,
                        TxId = transaction.TxId,
                        Amount = transaction.Amount,
                        Currency = transaction.Currency ?? string.Empty,
                        DebtorName = transaction.DebtorName ?? string.Empty,
                        DebtorAccount = transaction.DebtorAccount ?? string.Empty,
                        DebtorAddress = transaction.DebtorAddress ?? string.Empty,
                        DebtorAccountType = transaction.DebtorAccountType ?? string.Empty,
                        DebtorAgentBIC = transaction.DebtorAgentBIC ?? string.Empty,
                        DebtorIssuer = transaction.DebtorIssuer ?? string.Empty,
                        CreditorName = transaction.CreditorName ?? string.Empty,
                        CreditorAccount = transaction.CreditorAccount ?? string.Empty,
                        CreditorAddress = transaction.CreditorAddress ?? string.Empty,
                        CreditorAccountType = transaction.CreditorAccountType ?? string.Empty,
                        CreditorAgentBIC = transaction.CreditorAgentBIC ?? string.Empty,
                        CreditorIssuer = transaction.CreditorIssuer ?? string.Empty,
                        RemittanceInformation = transaction.RemittanceInformation ?? string.Empty,
                        Date = DateTime.UtcNow,
                        ToBIC = isoMessage.FromBIC ?? string.Empty,
                        SettlementMethod = "CLRG",
                        ChargeBearer = "SLEV",
                        BizMsgIdr = isoMessage.BizMsgIdr ?? string.Empty,
                        MsgDefIdr = isoMessage.MsgDefIdr ?? string.Empty,
                        ClearingSystem = string.Empty,
                        MsgId = isoMessage.MsgId ?? string.Empty
                    };
                    BillReferenceMapper.Apply(paymentDto);

                    _logger.LogInformation("[{CorrelationId}] Calling CoreBank Transfer endpoint for payment retry TxId {TxId} with RetryId {RetryId}", cid, txId, retryId);
                    result = await _callbacks.SendJsonAsync(
                        _callbackLinks.Transfer!,
                        headers,
                        paymentDto,
                        CB_PaymentRequest,
                        _jsonAdapter,
                        _correlation,
                        _jsonSerializerOptions,
                        _callback,
                        coreBankCts.Token,
                        cid
                    );
                }
                else
                {
                    headers["X-Return-Id"] = isoMessage.ReturnId;

                    var returnDto = new CBReturnRequestDto
                    {
                        FromBIC = isoMessage.FromBIC ?? string.Empty,
                        OriginalEndToEnd = transaction.EndToEndId ?? string.Empty,
                        OrgnlTxId = transaction.TxId,
                        ReturnId = isoMessage.ReturnId,
                        Reason = isoMessage.Reason ?? "Return retry",
                        AdditionalInfo = isoMessage.AdditionalInfo ?? string.Empty
                    };

                    _logger.LogInformation("[{CorrelationId}] Calling CoreBank Return endpoint for TxId {TxId} with RetryId {RetryId}", cid, txId, retryId);
                    result = await _callbacks.SendJsonAsync(
                        _callbackLinks.Return!,
                        headers,
                        returnDto,
                        CB_ReturnRequest,
                        _jsonAdapter,
                        _correlation,
                        _jsonSerializerOptions,
                        _callback,
                        coreBankCts.Token,
                        cid
                    );
                }
            }

            // Step 7: Handle CoreBank response
            if (result == null)
            {
                _logger.LogError("[{CorrelationId}] CoreBank callback returned null for TxId {TxId}", cid, txId);
                result = Response<JsonObject?>.Fail(
                    "CoreBank callback returned null response",
                    System.Net.HttpStatusCode.BadGateway);
            }

            _logger.LogInformation("[{CorrelationId}] CoreBank callback completed for TxId {TxId}, StatusCode={StatusCode}",
                cid, txId, result.StatusCode);

            // Step 8: Parse CoreBank response
            var cbResponse = result.Data != null ? ParseCallbackResult(result.Data, coreBankResponseKind) : null;
            var cbStatus = cbResponse?.Status ?? string.Empty;

            _logger.LogInformation("[{CorrelationId}] CoreBank response status: {Status} for TxId {TxId}", cid, cbStatus, txId);

            // Step 9: Determine final status based on CoreBank response
            // IMPORTANT: This handler should NEVER set status to Failed
            // Only two outcomes: Success (if CoreBank processed) or ReadyForReturn (if CoreBank rejected/failed)
            bool coreBankSuccess = result.IsSuccess &&
                result.StatusCode >= System.Net.HttpStatusCode.OK &&
                result.StatusCode < System.Net.HttpStatusCode.MultipleChoices &&
                _statusOrchestrator.IsSuccessStatus(cbStatus);

            // Step 11: Update database with new status
            using var dbCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            var dbCt = dbCts.Token;

            if (coreBankSuccess)
            {
                // CoreBank successfully processed the transaction
                isoMessage.Status = TransactionStatus.Success;
                isoMessage.Reason = "Transaction processed successfully - retry completed";
                isoMessage.AdditionalInfo = $"Retry completed with RetryId: {retryId}. CoreBank processing successful.";
                _logger.LogInformation("[{CorrelationId}] CoreBank retry completed successfully for TxId {TxId} with RetryId {RetryId}",
                    cid, txId, retryId);
            }
            else
            {
                // CoreBank rejected or failed - keep in ReadyForReturn.
                isoMessage.Status = TransactionStatus.ReadyForReturn;
                isoMessage.Reason = "CoreBank processing failed - ready for retry";
                isoMessage.AdditionalInfo = $"CoreBank rejected. HTTP: {(int)result.StatusCode}, Status: {cbStatus}. Retry attempt {isoMessage.CoreBankRetryCount}.";
                _logger.LogWarning("[{CorrelationId}] CoreBank retry failed for TxId {TxId}. Status: ReadyForReturn, HTTP: {HttpStatus}, CoreBank Status: {CbStatus}, RetryCount: {RetryCount}",
                    cid, txId, result.StatusCode, cbStatus, isoMessage.CoreBankRetryCount);
            }

            // Persist the CoreBank response
            isoMessage.CoreBankResponse = JsonSerializer.Serialize(result, _jsonSerializerOptions);
            await _persistence.ISOMessageResponseAsync(isoMessage, dbCt);
            claimedMessage = null;

            return new ReturnRetryResult
            {
                Success = isoMessage.Status == TransactionStatus.Success,
                Message = isoMessage.Status == TransactionStatus.Success
                    ? "CoreBank processing completed successfully"
                    : "CoreBank retry failed",
                TxId = txId,
                Status = isoMessage.Status.ToString(),
                Reason = isoMessage.Reason,
                AdditionalInfo = isoMessage.AdditionalInfo,
                EndToEndId = cbResponse?.EndToEndId,
                AcceptanceDate = cbResponse?.AcceptanceDate
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Exception during CoreBank retry for TxId {TxId}", cid, txId);
            if (claimedMessage != null)
            {
                try
                {
                    claimedMessage.Status = TransactionStatus.ReadyForReturn;
                    claimedMessage.Reason = "CoreBank retry failed unexpectedly";
                    claimedMessage.AdditionalInfo = "Retry claim released after an internal error; retrying with the same idempotency key is safe.";
                    using var releaseCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
                    await _persistence.ISOMessageResponseAsync(claimedMessage, releaseCts.Token);
                }
                catch (Exception releaseEx)
                {
                    _logger.LogCritical(releaseEx, "[{CorrelationId}] Failed to release CoreBank retry claim for TxId {TxId}", cid, txId);
                }
            }
            return new ReturnRetryResult
            {
                Success = false,
                Message = $"Exception occurred: {ex.Message}",
                TxId = txId,
                Status = "Error",
                Reason = "Exception during retry",
                AdditionalInfo = ex.ToString()
            };
        }
    }

    private PaymentResponseDto ParseCallbackResult(JsonObject data, string transformKey = "CB_PaymentResponse")
    {
        try
        {
            if (string.Equals(transformKey, CB_CompletionNotificationResponse, StringComparison.Ordinal))
            {
                var completionMapped = _jsonAdapter.Transform(data, CB_CompletionNotificationResponse);
                var completion = _jsonAdapter.ToObject<CBCompletionNotificationResponse>(completionMapped);
                if (completion == null)
                    return new PaymentResponseDto { Status = string.Empty, TxId = string.Empty };

                return new PaymentResponseDto
                {
                    Status = completion.Status ?? string.Empty,
                    AdditionalInfo = completion.AdditionalInfo,
                    Reason = completion.Reason
                };
            }

            var md = _jsonAdapter.Transform(data, transformKey);
            var cb = _jsonAdapter.ToObject<CBPaymentStatusResponseDto>(md);

            if (cb == null)
                return new PaymentResponseDto { Status = string.Empty, TxId = string.Empty };

            return new PaymentResponseDto
            {
                Status = cb.Status ?? string.Empty,
                TxId = cb.TxId ?? string.Empty,
                AcceptanceDate = cb.AcceptanceDate,
                AdditionalInfo = cb.AdditionalInfo,
                Reason = cb.Reason,
                EndToEndId = cb.EndToEndId ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse CoreBank callback result");
            return new PaymentResponseDto { Status = string.Empty, TxId = string.Empty };
        }
    }
}
