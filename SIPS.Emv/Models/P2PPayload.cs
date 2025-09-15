using System.ComponentModel.DataAnnotations;
using System.Text;
namespace SIPS.Emv.Models;

public class P2PPayload : IValidatableObject
{
    private bool _validating;

    [EmvSpecification(00, MaxLength = 2)]
    [Required]
    public string PayloadFormatIndicator { get; set; } = string.Empty;

    [EmvSpecification(01, MaxLength = 2)]
    [Range(11, 12)]
    public string? PointOfInitializationMethod { get; set; }

    [EmvSpecification(27, MaxLength = 2)]
    [Required]
    public string? SchemeIdentifier { get; set; }

    [EmvSpecification(03, MaxLength = 25)]
    public string? FiName { get; set; }

    [EmvSpecification(04, MaxLength = 23)]
    public string? AccountNumber { get; set; }

    [EmvSpecification(05, MaxLength = 45)]
    public string? AccountName { get; set; }

    [EmvSpecification(06, MaxLength = 10)]
    public decimal Amount { get; set; }

    [EmvSpecification(07, MaxLength = 30)]
    public string? Particulars { get; set; }

    // [EmvSpecification(11, MaxLength = 2)]
    // [Range(26, 51)]
    // public string? SchemeIdentifierId { get; set; }

    [EmvSpecification(10, MaxLength = 4)]
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
            errorMessageBuilder.AppendLine("The following errors occurred while validating the P2P QR Code:");

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

            if (PayloadFormatIndicator != "02")
            {
                errors.Add(new ValidationResult("The payload format indicator must have '02' as value", [nameof(PayloadFormatIndicator)]));
            }

            return errors;
        }
        finally
        {
            _validating = false;
        }
    }
}

public class InitiatorInformation
{
    [EmvSpecification(01, MaxLength = 25)]
    public string? BankIdentifier { get; set; }

    [EmvSpecification(02, MaxLength = 23)]
    public string? AccountInformation { get; set; }
}
