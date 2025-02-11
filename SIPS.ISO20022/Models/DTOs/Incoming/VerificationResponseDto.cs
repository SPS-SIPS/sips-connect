namespace SIPS.ISO20022.Models.DTOs;

public sealed class VerificationResponseDto
{
    public bool IsVerified { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string SIPSRequestId { get; set; } = string.Empty;
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Currency { get; set; }
}
