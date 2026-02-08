using System.ComponentModel.DataAnnotations;

namespace SIPS.Emv.Models;

public sealed class MerchantUnreservedDictionary : Dictionary<int, MerchantUnreservedTemplate>, IValidatableObject
{
    private bool _validating;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (_validating)
        {
            return [];
        }

        try
        {
            _validating = true;

            var results = new List<ValidationResult>();
            foreach (var item in this)
            {
                var context = new ValidationContext(item.Value, null, null);
                Validator.TryValidateObject(item.Value, context, results, true);
            }

            return results;
        }
        finally
        {
            _validating = false;
        }
    }
}
