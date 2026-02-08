using SIPS.PostgreSQL.Enums;

namespace SIPS.Core.Services.Abstractions;

/// <summary>
/// Centralized service for mapping ISO 20022 status codes to internal TransactionStatus enums.
/// Ensures consistent status determination across all incoming and outgoing handlers.
/// </summary>
public interface IStatusOrchestrator
{
    /// <summary>
    /// Maps IPS and CoreBank status codes to internal TransactionStatus for completion flows.
    /// Used by IncomingPaymentStatusReportHandler (pacs.002) and IncomingTransactionStatusHandler (pacs.028).
    /// </summary>
    /// <param name="ipsStatusCode">Status code from IPS (e.g., "ACSC", "RJCT")</param>
    /// <param name="coreBankStatusCode">Status code from CoreBank callback (e.g., "ACSC", "RJCT", null if callback failed)</param>
    /// <param name="isReturnFlow">True if this is a return transaction (pacs.004), false for normal payment</param>
    /// <returns>
    /// Tuple containing:
    /// - parentStatus: Status to set on parent ISOMessage
    /// - childStatus: Status to set on child ISOMessageStatus (should match parent for consistency)
    /// - reason: Human-readable reason for the status
    /// - additionalInfo: Additional context about the status determination
    /// </returns>
    (TransactionStatus parentStatus, TransactionStatus childStatus, string reason, string additionalInfo) 
        MapCompletionStatus(string ipsStatusCode, string? coreBankStatusCode, bool isReturnFlow = false);

    /// <summary>
    /// Maps a single status code (from IPS or CoreBank) to TransactionStatus.
    /// Used for simpler scenarios where only one status code is available.
    /// </summary>
    /// <param name="statusCode">ISO status code (e.g., "ACSC", "RJCT", "SUCC", "MISS")</param>
    /// <param name="source">Source of the status code for logging/context (e.g., "IPS", "CoreBank")</param>
    /// <returns>Mapped TransactionStatus</returns>
    TransactionStatus MapSingleStatus(string statusCode, string source = "Unknown");

    /// <summary>
    /// Maps internal TransactionStatus back to ISO 20022 status code for outgoing messages.
    /// </summary>
    /// <param name="status">Internal TransactionStatus</param>
    /// <returns>ISO status code (e.g., "ACSC", "RJCT")</returns>
    string MapToIsoStatusCode(TransactionStatus status);

    /// <summary>
    /// Determines if a given status code represents a successful transaction.
    /// </summary>
    /// <param name="statusCode">ISO status code</param>
    /// <returns>True if status represents success (ACSC, SUCC), false otherwise</returns>
    bool IsSuccessStatus(string? statusCode);

    /// <summary>
    /// Determines if a given status code represents a rejection.
    /// </summary>
    /// <param name="statusCode">ISO status code</param>
    /// <returns>True if status represents rejection (RJCT, MISS), false otherwise</returns>
    bool IsRejectionStatus(string? statusCode);
}
