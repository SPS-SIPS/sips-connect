namespace SIPS.ISO20022.Models.DTOs.CB;

public sealed class CBVerificationRequestDto
{
    public string Alias { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string FromBIC { get; set; } = default!;
    public string VerificationId { get; set; } = default!;
}