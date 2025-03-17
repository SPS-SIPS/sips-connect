namespace SIPS.ISO20022.Models.DTOs;

public sealed class ReturnPaymentRequestDto
{
    public string OriginalTxId { get; set; } = default!;
    public string OriginalEndToEndId { get; set; } = default!;
    public string Reason { get; set; } = default!;
    public string AdditionalInfo { get; set; } = default!;
    public string ReturnId { get; set; }
}