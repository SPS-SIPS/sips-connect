namespace SIPS.ISO20022.Models.DTOs.CB;

public sealed class CBVerificationRequestDto
{
    public string Alias { get; set; } = default!;
    public string? Type { get; set; }
    public string FromBIC { get; set; } = default!;
    public string VerificationId { get; set; } = default!;
    public string? InvoiceIdOrUpr { get; set; }
    public string? AccountNo { get; set; }
    public string Agent { get; set; } = default!;
    public string VerificationRequestId { get; set; } = default!;
    public string? PayerBankCode { get; set; }
    public string? PayerChannel { get; set; }
}
