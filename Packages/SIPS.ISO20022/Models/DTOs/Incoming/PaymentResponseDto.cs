namespace SIPS.ISO20022.Models.DTOs;

public sealed class PaymentResponseDto
{
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? AdditionalInfo { get; set; }
    public DateTime? AcceptanceDate { get; set; }
    public string TxId { get; set; } = string.Empty;
    public string EndToEndId { get; set; } = string.Empty;
}
