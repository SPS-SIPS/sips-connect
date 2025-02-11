using System.ComponentModel.DataAnnotations;
using System.Text;
namespace SIPS.Emv.Models;

public class MerchantPayload : IValidateObject
{
    private bool _validating;

    [EmvSpecification(00, MaxLength = 2)]
    [Required]
    public string PayloadFormatIndicator { get; set; } = "02";

    [EmvSpecification(01, MaxLength = 2)]
    [Range(11, 12)]
    public string? PointOfInitializationMethod { get; set; }

    [EmvSpecification(id: 29, IsParent = true)]
    [ValidateObject]
    public MerchantAccountDictionary? MerchantAccount { get; set; }

    [EmvSpecification(52, MaxLength = 4)]
    [Required]
    public int MerchantCategoryCode { get; set; }

    [EmvSpecification(53, MaxLength = 3)]
    [Required]
    public int TransactionCurrency { get; set; }

    [EmvSpecification(54, MaxLength = 13)]
    public decimal? TransactionAmount { get; set; }

    [EmvSpecification(55, MaxLength = 2)]
    [Range(1, 3)]
    public int? TipOrConvenienceIndicator { get; set; }

    [EmvSpecification(56, MaxLength = 13)]
    [MaxLength(13)]
    [RequireUTF8]
    public string? ValueOfConvenienceFeeFixed { get; set; }


    [EmvSpecification(57, MaxLength = 5)]
    [RequireUTF8]
    public string? ValueOfConvenienceFeePercentage { get; set; }

    [EmvSpecification(58, MaxLength = 2)]
    [Required]
    [MaxLength(2)]
    [RequireUTF8]
    public string? CountyCode { get; set; }

    [EmvSpecification(59)]
    [Required]
    [MaxLength(25)]
    [RequireUTF8]
    public string? MerchantName { get; set; }

    [EmvSpecification(60, MaxLength = 15)]
    [Required]
    [MaxLength(15)]
    [RequireUTF8]
    public string? MerchantCity { get; set; }

    [EmvSpecification(61, MaxLength = 10)]
    [MaxLength(10)]
    [RequireUTF8]
    public string? PostalCode { get; set; }

    [EmvSpecification(62, IsParent = true)]
    [ValidateObject]
    public MerchantAdditionalData? AdditionalData { get; set; }

    [EmvSpecification(64, IsParent = true)]
    [ValidateObject]
    public MerchantInfoLanguageTemplate? MerchantInformation { get; set; }


    [EmvSpecification(91, IsParent = true)]
    [ValidateObject]
    public MerchantUnreservedDictionary? UnreservedTemplate { get; set; }

    [EmvSpecification(63, MaxLength = 4)]
    [MaxLength(4)]
    [RequireUTF8]
    public string? CRC { get; internal set; }

    public string GeneratePayload()
    {
        var validationContext = new ValidationContext(this);
        var errors = Validate(validationContext);
        if (errors.Any())
        {
            var errorMessageBuilder = new StringBuilder();
            errorMessageBuilder.AppendLine("The following errors occurred while validating the MerchantPayload:");

            foreach (var item in errors)
            {
                errorMessageBuilder.AppendLine(item.ErrorMessage);
            }

            throw new InvalidOperationException(errorMessageBuilder.ToString());
        }

        var payload = new PayloadEncoder().GeneratePayload(this);
        return payload;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (_validating)
        {
            return [];
        }

        try
        {
            _validating = true;

            var errors = new List<ValidationResult>();

            Validator.TryValidateObject(this, validationContext, errors, true);

            if (PayloadFormatIndicator != "01")
            {
                errors.Add(new ValidationResult("The payload format indicator must have '01' as value", [nameof(PayloadFormatIndicator)]));
            }

            if (TipOrConvenienceIndicator.HasValue)
            {
                switch (TipOrConvenienceIndicator.Value)
                {
                    case 1:
                        // The mobile application should prompt the consumer to enter a tip to be paid to the merchant
                        break;

                    case 2:
                        if (string.IsNullOrWhiteSpace(ValueOfConvenienceFeeFixed))
                        {
                            errors.Add(new ValidationResult("A fixed value of convenience fee is required, when Tip or Convenience Indicator is set to 2", [nameof(ValueOfConvenienceFeeFixed)]));
                        }
                        break;

                    case 3:
                        if (string.IsNullOrWhiteSpace(ValueOfConvenienceFeePercentage))
                        {
                            errors.Add(new ValidationResult("A fixed value of convenience fee is required, when Tip or Convenience Indicator is set to 3", [nameof(ValueOfConvenienceFeePercentage)]));
                        }
                        break;

                    default:
                        errors.Add(new ValidationResult("Tip or Convenience Indicator must be either 1, 2, or 3", [nameof(TipOrConvenienceIndicator)]));
                        break;
                }
            }

            if (null != MerchantAccount && 1 <= MerchantAccount.Count)
            {
                var invalidIdentifiers = MerchantAccount.Keys.Count(k => k < 2 || k > 51);
                if (0 < invalidIdentifiers)
                {
                    errors.Add(new ValidationResult("Merchant Account Info Id should allocate between 26 and 51", [nameof(MerchantAccount)]));
                }
            }
            else
            {
                errors.Add(new ValidationResult("Global Unique Identifier of the merchant account is required", [nameof(MerchantAccount)]));
            }

            return errors;
        }
        finally
        {
            _validating = false;
        }
    }
}
