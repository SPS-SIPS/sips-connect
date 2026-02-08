namespace SIPS.ISO20022.Models.DTOs;

/// <summary>
/// Result of a return retry operation
/// </summary>
public sealed class ReturnRetryResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TxId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? AdditionalInfo { get; set; }
    public string? EndToEndId { get; set; }
    public DateTime? AcceptanceDate { get; set; }
}