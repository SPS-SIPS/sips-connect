namespace SIPS.ISO20022.Models.DTOs.CB;

public sealed class CBReturnResponseDto
{
    public string OriginalEndToEnd { get; set; } = default!;
    public string OrgnlTxId { get; set; } = default!;
    public string Status { get; set; } = default!;
    public string? Reason { get; set; } = default!;
    public string? AdditionalInfo { get; set; } = default!;
}