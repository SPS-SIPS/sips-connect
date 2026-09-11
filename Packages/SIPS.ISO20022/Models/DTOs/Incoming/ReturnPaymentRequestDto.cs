namespace SIPS.ISO20022.Models.DTOs;

public sealed class ReturnPaymentRequestDto
{
    public string? Rail { get; set; }
    public string ToBIC { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public string OriginalCurrency { get; set; } = string.Empty;
    public string OriginalTxId { get; set; } = default!;
    public string OriginalEndToEndId { get; set; } = default!;
    public string Reason { get; set; } = default!;
    public string AdditionalInfo { get; set; } = default!;
    public string ReturnId { get; set; } = default!;
    public string LocalInstrument { get; set; } = default!;
    public string CategoryPurpose { get; set; } = default!;
}
