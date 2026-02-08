namespace SIPS.ISO20022.Models.DTOs.CB;

public sealed class CBStatusRequestDto
{
    public string OriginalEndToEnd { get; set; } = default!;
    public string OrgnlTxId { get; set; } = default!;
    public string FromBIC { get; set; } = default!;
}
