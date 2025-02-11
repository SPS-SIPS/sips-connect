using System.ComponentModel.DataAnnotations;
using System.Globalization;
namespace SIPS.Emv.Attributes;

[AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
public class ValidateObjectAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        if (null != value)
        {
            var context = new ValidationContext(value, null, null);
            Validator.TryValidateObject(value, context, results, true);

            if (0 != results.Count)
            {
                var compositeResults = new CompositeValidationResult(string.Format(CultureInfo.CurrentCulture, "Validation for {0} failed", validationContext.DisplayName));
                results.ForEach(compositeResults.AddResult);

                return compositeResults;
            }
        }

        return ValidationResult.Success;
    }
}