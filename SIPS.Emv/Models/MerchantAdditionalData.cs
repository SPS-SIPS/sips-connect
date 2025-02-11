using System.ComponentModel.DataAnnotations;
namespace SIPS.Emv.Models;

public class MerchantAdditionalData
{
    [EmvSpecification(1, MaxLength = 25)]
    [RequireUTF8]
    [MaxLength(25)]
    public string? BillNumber { get; set; }

    [EmvSpecification(2, MaxLength = 25)]
    [RequireUTF8]
    [MaxLength(25)]
    public string? MobileNumber { get; set; }

    [EmvSpecification(3, MaxLength = 25)]
    [RequireUTF8]
    [MaxLength(25)]
    public string? StoreLabel { get; set; }

    [EmvSpecification(4, MaxLength = 25)]
    [RequireUTF8]
    [MaxLength(25)]
    public string? LoyaltyNumber { get; set; }

    [EmvSpecification(5, MaxLength = 25)]
    [RequireUTF8]
    [MaxLength(25)]
    public string? ReferenceLabel { get; set; }

    [EmvSpecification(6, MaxLength = 25)]
    [RequireUTF8]
    [MaxLength(25)]
    public string? CustomerLabel { get; set; }

    [EmvSpecification(7, MaxLength = 25)]
    [RequireUTF8]
    [MaxLength(25)]
    public string? TerminalLabel { get; set; }

    [EmvSpecification(8, MaxLength = 25)]
    [RequireUTF8]
    [MaxLength(25)]
    public string? PurposeOfTransaction { get; set; }

    [EmvSpecification(9, MaxLength = 25)]
    [RequireUTF8]
    [MaxLength(25)]
    public string? AdditionalConsumerDataRequest { get; set; }
}