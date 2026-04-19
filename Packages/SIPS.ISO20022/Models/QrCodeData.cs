namespace SIPS.ISO20022.Models;

public class QrCodeData
{
    public string AccountId { get; set; } = string.Empty;
    public string BankBICCode { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string AcquirerId { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? MerchantName { get; set; }
    public string? MerchantCity { get; set; }
    public string? PayloadFormatIndicator { get; set; }
    public string? PointOfInitializationMethod { get; set; }
    public string? AccountName { get; set; }
    public string? Particulars { get; set; }
    public string? BankName { get; set; }
}
