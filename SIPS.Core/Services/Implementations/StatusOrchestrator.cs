using Microsoft.Extensions.Logging;
using SIPS.Core.Services.Abstractions;
using SIPS.PostgreSQL.Enums;

namespace SIPS.Core.Services.Implementations;

/// <summary>
/// Centralized implementation for mapping ISO 20022 status codes to internal TransactionStatus enums.
/// Implements the business rules defined in SVIP v1.5 specification.
/// </summary>
public sealed class StatusOrchestrator : IStatusOrchestrator
{
    private readonly ILogger<StatusOrchestrator> _logger;

    // ISO 20022 status codes
    private const string ACSC = "ACSC"; // AcceptedSettlementCompleted
    private const string RJCT = "RJCT"; // Rejected
    private const string SUCC = "SUCC"; // Success (verification)
    private const string MISS = "MISS"; // Missing (verification)

    public StatusOrchestrator(ILogger<StatusOrchestrator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public (TransactionStatus parentStatus, TransactionStatus childStatus, string reason, string additionalInfo) 
        MapCompletionStatus(string ipsStatusCode, string? coreBankStatusCode, bool isReturnFlow = false)
    {
        // Normalize inputs and map full descriptions to ISO codes
        var ipsCode = NormalizeStatusCode(ipsStatusCode);
        var cbCode = string.IsNullOrWhiteSpace(coreBankStatusCode) ? null : NormalizeStatusCode(coreBankStatusCode);

        _logger.LogDebug("[StatusOrchestrator] Mapping completion status: IPS={IpsCode}, CB={CbCode}, IsReturn={IsReturn}", 
            ipsCode, cbCode ?? "null", isReturnFlow);

        // Rule 1: IPS sent RJCT → transaction failed at IPS level
        if (ipsCode == RJCT)
        {
            var status = TransactionStatus.Failed;
            return (status, status, "Received rejection confirmation", "Standard Rejection Confirmation Notification Received");
        }

        // Rule 2: IPS sent ACSC (or SUCC for verification)
        if (ipsCode == ACSC || ipsCode == SUCC)
        {
            // Rule 2a: No CoreBank response (callback failed or returned null)
            if (string.IsNullOrWhiteSpace(cbCode))
            {
                var status = TransactionStatus.ReadyForReturn;
                return (status, status, 
                    "CoreBank callback failed", 
                    "IPS accepted but CoreBank processing unavailable");
            }

            // Rule 2b: CoreBank processed successfully
            if (cbCode == ACSC || cbCode == SUCC)
            {
                var status = TransactionStatus.Success;
                return (status, status, 
                    isReturnFlow ? "Return Processed" : "Processed Transaction", 
                    "Processed");
            }

            // Rule 2c: CoreBank rejected (IPS accepted but CoreBank failed to credit)
            // This is the classic ReadyForReturn scenario
            var readyStatus = TransactionStatus.ReadyForReturn;
            return (readyStatus, readyStatus, 
                "Transaction is ready for return", 
                "Queued For Return!");
        }

        // Rule 3: Unknown or missing IPS status → treat as failure
        _logger.LogWarning("[StatusOrchestrator] Unknown IPS status code: {IpsCode}. Treating as failure.", ipsCode);
        var failedStatus = TransactionStatus.Failed;
        return (failedStatus, failedStatus, 
            "Unknown status from IPS", 
            $"Unrecognized IPS status: {ipsCode}");
    }

    /// <inheritdoc/>
    public TransactionStatus MapSingleStatus(string statusCode, string source = "Unknown")
    {
        var code = NormalizeStatusCode(statusCode);

        _logger.LogDebug("[StatusOrchestrator] Mapping single status: Code={Code}, Source={Source}", code, source);

        return code switch
        {
            ACSC => TransactionStatus.Success,
            SUCC => TransactionStatus.Success,
            RJCT => TransactionStatus.Failed,
            MISS => TransactionStatus.Failed,
            "" => TransactionStatus.Failed, // Empty/null treated as failure
            _ => TransactionStatus.Failed // Unknown codes treated as failure
        };
    }

    /// <inheritdoc/>
    public string MapToIsoStatusCode(TransactionStatus status)
    {
        return status switch
        {
            TransactionStatus.Success => ACSC,
            TransactionStatus.Failed => RJCT,
            TransactionStatus.Pending => ACSC, // Pending still shows as ACSC at ISO level (technical ack)
            TransactionStatus.ReadyForReturn => ACSC, // ReadyForReturn shows as ACSC to IPS (accepted at switch)
            TransactionStatus.CheckStatus => ACSC, // CheckStatus is internal, show as ACSC externally
            _ => RJCT // Default to rejection for safety
        };
    }

    /// <inheritdoc/>
    public bool IsSuccessStatus(string? statusCode)
    {
        var code = NormalizeStatusCode(statusCode);
        return code == ACSC || code == SUCC;
    }

    /// <inheritdoc/>
    public bool IsRejectionStatus(string? statusCode)
    {
        var code = NormalizeStatusCode(statusCode);
        return code == RJCT || code == MISS;
    }

    /// <summary>
    /// Normalizes status codes by trimming and converting to uppercase
    /// </summary>
    private string NormalizeStatusCode(string? statusCode)
    {
        if (string.IsNullOrWhiteSpace(statusCode))
            return string.Empty;

        return statusCode.Trim().ToUpperInvariant();
    }
}
