using System.ComponentModel.DataAnnotations;

namespace SIPS.Emv.Interfaces;
public interface IValidateObject
{
    IEnumerable<ValidationResult> Validate(ValidationContext validationContext);
}
