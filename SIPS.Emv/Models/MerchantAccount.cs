using System.ComponentModel.DataAnnotations;
namespace SIPS.Emv.Models;
public class MerchantAccount : IValidateObject
{
    private bool _validating;
    public MerchantAccount()
    {
        PaymentNetworkSpecific = [];
    }

    [EmvSpecification(0, MaxLength = 32)]
    [Required]
    [RequireUTF8]
    [MaxLength(32)]
    public string GlobalUniqueIdentifier { get; set; } = string.Empty;
    public Dictionary<int, string> PaymentNetworkSpecific { get; set; }

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

            var invalidIdentifiers = PaymentNetworkSpecific.Keys.Count(k => k < 1 || k > 99);
            if (0 < invalidIdentifiers)
            {
                validationResults.Add(new ValidationResult("PaymentNetworkSpecific", [nameof(PaymentNetworkSpecific)]));
            }

            return validationResults;
        }
        finally
        {
            _validating = false;
        }
    }
}
