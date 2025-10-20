using System.ComponentModel.DataAnnotations;

namespace SIPS.Emv.Models;

public sealed class SomQRMerchantRequest
{
    [Required]
    public int CurrencyCode { get; set; }
    [Required]
    public string MerchantId { get; set; } = string.Empty;
    [Required]
    public string MerchantName { get; set; } = string.Empty;
    [Required]
    public int MerchantCategoryCode { get; set; }
    [Required(ErrorMessage = "POI Method is required")]
    [Range(1, 3, ErrorMessage = "POI Method must be 1=QR, 2=BLE or 3=NFC")]
    public int Method { get; set; }
    [Required(ErrorMessage = "POI Method Type is required")]
    [Range(1, 2, ErrorMessage = "POI Method Type must be 1=static or 2=dynamic")]
    public int Type { get; set; }
    public string? MerchantCity { get; set; }
    public string? PostalCode { get; set; }
    public string? StoreLabel { get; set; }
    public string? TerminalLabel { get; set; }
    public decimal Amount { get; set; } = 0;
    public int? TipOrConvenienceIndicator { get; set; }
    public string? ValueOfConvenienceFeeFixed { get; set; }
    public string? ValueOfConvenienceFeePercentage { get; set; }
}

public sealed class SomQRPersonRequest : IValidatableObject
{
    public decimal Amount { get; set; } = 0;
    [Required]
    public string AccountName { get; set; } = string.Empty;
    [Required]
    public string IBAN { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public string? Particulars { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount > 0 && string.IsNullOrEmpty(CurrencyCode))
        {
            yield return new ValidationResult("CurrencyCode is required when Amount is greater than 0", [nameof(CurrencyCode)]);
        }
    }
}