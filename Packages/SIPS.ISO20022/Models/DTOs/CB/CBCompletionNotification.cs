namespace SIPS.ISO20022.Models.DTOs.CB;

public sealed class CBCompletionNotification
{
    public string OriginalTxId { get; set; } = default!;
    public string? OriginalEndToEndId { get; set; } = default!;
    public string Status { get; set; } = default!;
    public string? Reason { get; set; } = default!;
    public string? AdditionalInfo { get; set; } = default!;
}
