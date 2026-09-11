namespace SIPS.ISO20022.Models.DTOs.CB;

public sealed class StatusRequestDto
{
    public string? Rail { get; set; }
    public string TxId { get; set; } = default!;
    public string EndToEnd { get; set; } = default!;
    public string ToBIC { get; set; } = default!;
}
