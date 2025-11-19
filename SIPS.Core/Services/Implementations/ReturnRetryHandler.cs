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
        _logger.LogInformation("[{CorrelationId}] Starting return retry for TxId {TxId}", cid, txId);

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

            // Step 3: Check retry limits (max 2 retries = round 3)
            const int MaxRetryRound = 3;
            if (isoMessage.Round >= MaxRetryRound)
            {
                _logger.LogWarning("[{CorrelationId}] Maximum retry attempts reached for TxId {TxId}. Round: {Round}",
                    cid, txId, isoMessage.Round);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = $"Maximum retry attempts ({MaxRetryRound - 1}) reached. Return the transaction to IPS.",
                    TxId = txId,
                    Status = "MaxRetriesExceeded",
                    Reason = "Maximum retry attempts exceeded",
                    AdditionalInfo = $"Current round: {isoMessage.Round}. Return the transaction to IPS."
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
            if (!string.IsNullOrWhiteSpace(transaction.TxId))
                headers["X-Idempotency-Key"] = $"{transaction.TxId}-retry-{DateTime.UtcNow:yyyyMMddHHmmss}";
            if (!string.IsNullOrWhiteSpace(isoMessage.ReturnId))
                headers["X-Return-Id"] = isoMessage.ReturnId;

            // Step 5: Build return request payload for CoreBank
            var returnDto = new CBReturnRequestDto
            {
                FromBIC = isoMessage.FromBIC ?? string.Empty,
                OriginalEndToEnd = transaction.EndToEndId ?? string.Empty,
                OrgnlTxId = transaction.TxId ?? string.Empty,
                ReturnId = isoMessage.ReturnId ?? string.Empty,
                Reason = isoMessage.Reason ?? "Return retry - manual intervention",
                AdditionalInfo = isoMessage.AdditionalInfo ?? "Retry initiated by operator"
            };

            _logger.LogInformation("[{CorrelationId}] Calling CoreBank return endpoint for TxId {TxId} with ReturnId {ReturnId}",
                cid, txId, isoMessage.ReturnId);

            // Step 6: Call CoreBank return endpoint
            var result = await _callbacks.SendJsonAsync(
                _callbackLinks.Return!,
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

            // Step 7: Handle CoreBank response
            if (result == null)
            {
                _logger.LogError("[{CorrelationId}] CoreBank return callback returned null for TxId {TxId}", cid, txId);
                return new ReturnRetryResult
                {
                    Success = false,
                    Message = "CoreBank callback returned null response",
                    TxId = txId,
                    Status = "CallbackFailed",
                    Reason = "CoreBank return callback failed",
                    AdditionalInfo = "Manual intervention required"
                };
            }

            _logger.LogInformation("[{CorrelationId}] CoreBank return callback completed for TxId {TxId}, StatusCode={StatusCode}",
                cid, txId, result.StatusCode);

            // Step 8: Parse CoreBank response
            var cbResponse = result.Data != null ? ParseCallbackResult(result.Data) : null;
            var cbStatus = cbResponse?.Status ?? string.Empty;

            _logger.LogInformation("[{CorrelationId}] CoreBank response status: {Status} for TxId {TxId}", cid, cbStatus, txId);

            // Step 9: Map CoreBank response to final status
            var (parentStatus, childStatus, reason, additionalInfo) = _statusOrchestrator.MapCompletionStatus(
                "ACSC",  // IPS side - return was confirmed
                cbStatus,
                false);

            // Step 11: Update database with new status
            using var dbCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            var dbCt = dbCts.Token;

            isoMessage.Status = parentStatus;

            if (parentStatus == TransactionStatus.Success)
            {
                isoMessage.Reason = "Transaction returned successfully - retry completed";
                isoMessage.AdditionalInfo = $"Return completed with ReturnId: {isoMessage.ReturnId ?? "N/A"}. CoreBank reversal successful.";
                _logger.LogInformation("[{CorrelationId}] Return retry completed successfully for TxId {TxId} with ReturnId {ReturnId}",
                    cid, txId, isoMessage.ReturnId);
            }
            else
            {
                // Increment round counter for failed retries
                isoMessage.Round++;
                isoMessage.Reason = reason;
                isoMessage.AdditionalInfo = additionalInfo;
                _logger.LogWarning("[{CorrelationId}] Return retry failed for TxId {TxId}. Status: {Status}, Reason: {Reason}, Round: {Round}",
                    cid, txId, parentStatus, reason, isoMessage.Round);
            }

            // Persist the CoreBank response
            isoMessage.CoreBankResponse = JsonSerializer.Serialize(result, _jsonSerializerOptions);
            await _persistence.ISOMessageResponseAsync(isoMessage, dbCt);

            return new ReturnRetryResult
            {
                Success = parentStatus == TransactionStatus.Success,
                Message = parentStatus == TransactionStatus.Success
                    ? "Return completed successfully"
                    : "Return retry failed",
                TxId = txId,
                Status = parentStatus.ToString(),
                Reason = isoMessage.Reason,
                AdditionalInfo = isoMessage.AdditionalInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Exception during return retry for TxId {TxId}", cid, txId);
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
