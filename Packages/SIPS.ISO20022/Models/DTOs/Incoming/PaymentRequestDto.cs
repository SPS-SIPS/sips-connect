namespace SIPS.ISO20022.Models.DTOs;

public sealed class PaymentRequestDto
{
    public string ToBIC { get; set; } = default!;
    public string LocalInstrument { get; set; } = default!;
    public string CategoryPurpose { get; set; } = default!;
    public string EndToEndId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    // Debtor
    public string DebtorName { get; set; } = default!;
    public string DebtorAccount { get; set; } = default!;
    public string DebtorAddress { get; set; } = string.Empty;
    public string DebtorAccountType { get; set; } = default!;
    public string DebtorAgentBIC { get; set; } = default!;
    public string DebtorIssuer { get; set; } = "C";


    // Creditor
    public string CreditorName { get; set; } = default!;
    public string CreditorAccount { get; set; } = default!;
    public string CreditorAddress { get; set; } = string.Empty;
    public string CreditorAccountType { get; set; } = default!;
    public string CreditorAgentBIC { get; set; } = default!;
    public string CreditorIssuer { get; set; } = "C";


    public string RemittanceInformation { get; set; } = default!;
    public string? TxId { get; set; }
}
