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

            // Step 3: Check retry limits (max 2 retries)
            // Round 1: Initial attempt, Round 2: First retry, Round 3: Second retry
            // Block at Round 3+ to prevent further CoreBank calls
            // Transaction stays in ReadyForReturn for dispute channel or IPS return (if within 2 calendar days)
            const int MaxAllowedRound = 2;
            if (isoMessage.Round > MaxAllowedRound)
            {
                _logger.LogWarning("[{CorrelationId}] Maximum retry attempts reached for TxId {TxId}. Round: {Round}. No further CoreBank retries allowed.",
                    cid, txId, isoMessage.Round);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = $"Maximum retry attempts ({MaxAllowedRound}) reached. Route to dispute channel or return to IPS if within 2 calendar days.",
                    TxId = txId,
                    Status = isoMessage.Status.ToString(), // Keep current status (ReadyForReturn)
                    Reason = "Maximum retry attempts exceeded - CoreBank rejected transaction",
                    AdditionalInfo = $"Current round: {isoMessage.Round}. No further automatic retries allowed. Route to dispute channel or return to IPS."
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

            // Step 4: Prepare headers for CoreBank call
            var headers = new Dictionary<string, string>
            {
                { API_Key, _callbackLinks.Key! },
                { API_Secret, _callbackLinks.Secret! }
            };

            if (!string.IsNullOrWhiteSpace(transaction.TxId))
                headers["X-Transaction-Id"] = transaction.TxId;
            var retryId = $"{transaction.TxId}-retry-{DateTime.UtcNow:yyyyMMddHHmmss}";
            if (!string.IsNullOrWhiteSpace(transaction.TxId))
                headers["X-Idempotency-Key"] = retryId;
            if (!string.IsNullOrWhiteSpace(retryId))
                headers["X-Retry-Id"] = retryId;

            // Step 5: Build payment request payload for CoreBank with full transaction data
            var paymentDto = new CBPaymentRequestDto
            {
                FromBIC = transaction.FromBIC ?? string.Empty,
                LocalInstrument = transaction.LocalInstrument ?? string.Empty,
                CategoryPurpose = transaction.CategoryPurpose ?? string.Empty,
                EndToEndId = transaction.EndToEndId ?? string.Empty,
                TxId = transaction.TxId ?? string.Empty,
                Amount = transaction.Amount,
                Currency = transaction.Currency ?? string.Empty,
                DebtorName = transaction.DebtorName ?? string.Empty,
                DebtorAccount = transaction.DebtorAccount ?? string.Empty,
                DebtorAccountType = transaction.DebtorAccountType ?? string.Empty,
                DebtorAgentBIC = transaction.DebtorAgentBIC ?? string.Empty,
                DebtorIssuer = transaction.DebtorIssuer ?? string.Empty,
                CreditorName = transaction.CreditorName ?? string.Empty,
                CreditorAccount = transaction.CreditorAccount ?? string.Empty,
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

            _logger.LogInformation("[{CorrelationId}] Calling CoreBank transfer endpoint for TxId {TxId} with RetryId {RetryId}",
                cid, txId, retryId);

            // Step 6: Call CoreBank transfer endpoint to retry the return request
            var result = await _callbacks.SendJsonAsync(
                _callbackLinks.Transfer!,
                headers,
                paymentDto,
                CB_PaymentRequest,
                _jsonAdapter,
                _correlation,
                _jsonSerializerOptions,
                _callback,
                ct,
                cid
            );

            // Step 7: Handle CoreBank response
            if (result == null)
            {
                _logger.LogError("[{CorrelationId}] CoreBank callback returned null for TxId {TxId}", cid, txId);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = "CoreBank callback returned null response",
                    TxId = txId,
                    Status = "CallbackFailed",
                    Reason = "CoreBank callback failed",
                    AdditionalInfo = "Manual intervention required"
                };
            }

            _logger.LogInformation("[{CorrelationId}] CoreBank callback completed for TxId {TxId}, StatusCode={StatusCode}",
                cid, txId, result.StatusCode);

            // Step 8: Parse CoreBank response
            var cbResponse = result.Data != null ? ParseCallbackResult(result.Data) : null;
            var cbStatus = cbResponse?.Status ?? string.Empty;

            _logger.LogInformation("[{CorrelationId}] CoreBank response status: {Status} for TxId {TxId}", cid, cbStatus, txId);

            // Step 9: Determine final status based on CoreBank response
            // IMPORTANT: This handler should NEVER set status to Failed
            // Only two outcomes: Success (if CoreBank processed) or ReadyForReturn (if CoreBank rejected/failed)
            bool coreBankSuccess = _statusOrchestrator.IsSuccessStatus(cbStatus);

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
                // CoreBank rejected or failed - keep in ReadyForReturn and increment round
                isoMessage.Status = TransactionStatus.ReadyForReturn;
                isoMessage.Round++;
                isoMessage.Reason = "CoreBank processing failed - ready for retry";
                isoMessage.AdditionalInfo = $"CoreBank rejected. Status: {cbStatus}. Retry attempt {isoMessage.Round}.";
                _logger.LogWarning("[{CorrelationId}] CoreBank retry failed for TxId {TxId}. Status: ReadyForReturn, CoreBank Status: {CbStatus}, Round: {Round}",
                    cid, txId, cbStatus, isoMessage.Round);
            }

            // Persist the CoreBank response
            isoMessage.CoreBankResponse = JsonSerializer.Serialize(result, _jsonSerializerOptions);
            await _persistence.ISOMessageResponseAsync(isoMessage, dbCt);

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

    private PaymentResponseDto ParseCallbackResult(JsonObject data)
    {
        try
        {
            var md = _jsonAdapter.Transform(data, "CB_PaymentResponse");
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
