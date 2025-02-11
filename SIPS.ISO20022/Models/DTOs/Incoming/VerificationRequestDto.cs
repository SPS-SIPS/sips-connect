namespace SIPS.ISO20022.Models.DTOs;
public sealed class VerificationRequestDto
{
    public string Alias { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string ToBIC { get; set; } = default!;
}