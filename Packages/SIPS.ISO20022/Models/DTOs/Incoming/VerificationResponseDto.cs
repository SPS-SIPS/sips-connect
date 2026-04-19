using SIPS.ISO20022.Models;
namespace SIPS.ISO20022.Models.DTOs;

public sealed class VerificationResponseDto
{
    public bool IsVerified { get; set; }
    // Absent if IsVerified is true.
    public string Reason { get; set; } = string.Empty;
    public string SIPSRequestId { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("accountNo")]
    public string? AccountNo { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("accountType")]
    public string? AccountType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("address")]
    public string? Address { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("currency")]
    public string? Currency { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("parsed")]
    public QrCodeData? Parsed { get; set; }
}
