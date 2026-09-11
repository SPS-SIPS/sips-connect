namespace SIPS.ISO20022.Models.DTOs;
public sealed class VerificationRequestDto
{
    public string? Rail { get; set; }
    public string Alias { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string ToBIC { get; set; } = default!;
    public string? MsgId { get; set; }
    public string? Code { get; set; }
}
