using System.ComponentModel.DataAnnotations;
namespace SIPS.Emv.Models;

public class MerchantInfoLanguageTemplate : IValidateObject
{
    private bool _validating;

    [EmvSpecification(0, MaxLength = 2)]
    [RequireUTF8]
    [MaxLength(2)]
    [Required]
    public string? LanguagePreference { get; set; }

    [EmvSpecification(1)]
    [Required]
    [MaxLength(25)]
    public string? MerchantNameAlternateLanguage { get; set; }

    [EmvSpecification(2)]
    [MaxLength(15)]
    public string? MerchantCityAlternateLanguage { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (_validating)
        {
            return [];
        }

        try
        {
            _validating = true;

            var validationResults = new List<ValidationResult>();

            Validator.TryValidateObject(this, validationContext, validationResults);

            return validationResults;
        }
        finally
        {
            _validating = false;
        }
    }
}