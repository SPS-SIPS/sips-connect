using SIPS.ISO20022.Models.DTOs;

namespace SIPS.ISO20022.Interfaces;

/// <summary>
/// Handles retry operations for ReadyForReturn transactions
/// </summary>
public interface IReturnRetryHandler
{
    /// <summary>
    /// Retries a ReadyForReturn transaction by calling CoreBank to complete the return
    /// </summary>
    /// <param name="txId">Transaction ID to retry</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result indicating success or failure with details</returns>
    Task<ReturnRetryResult> RetryReturnAsync(string txId, CancellationToken ct);
}