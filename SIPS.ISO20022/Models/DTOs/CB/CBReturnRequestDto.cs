namespace SIPS.ISO20022.Models.DTOs.CB;

public sealed class CBReturnRequestDto
{
    public string OriginalEndToEnd { get; set; } = default!;
    public string OrgnlTxId { get; set; } = default!;
    public string FromBIC { get; set; } = default!;
}
