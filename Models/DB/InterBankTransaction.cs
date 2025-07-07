using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SIPS.Example.Consumer.Enums;

namespace SIPS.Example.Consumer.Models.DB;

public sealed class InterBankTransaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column(TypeName = "uniqueidentifier")]
    [Required]
    public Guid Id { get; set; }
    public string VerificationMessageId { get; set; } = default!;
    public string BizMsgIdr { get; set; } = default!;
    public string MsgDefIdr { get; set; } = default!;
    public DateTimeOffset CreDt { get; set; }
    public DateTimeOffset CreDtTm { get; set; }
    public int NbOfTxs { get; set; } = 1;
    public SettlementMethods SettlementMethod { get; set; } = default!;
    public string ClearingSystem { get; set; } = default!;
    public LclInstrm PaymentTypeInformation { get; set; } = default!;
    public CategoryPurpose PaymentTypeInformationCategoryPurpose { get; set; } = default!;
    public string InstructingAgent { get; set; } = default!;
    public string InstructedAgent { get; set; } = default!;
    public string EndToEndIdentification { get; set; } = default!;
    public string TransactionIdentification { get; set; } = default!;
    public string? UETR { get; set; }
    public decimal InterbankSettlementAmount { get; set; }
    public string InterbankSettlementCurrency { get; set; } = default!;
    public DateTimeOffset AcceptanceDateTime { get; set; }
    public decimal InstructedAmount { get; set; }
    public string InstructedCurrency { get; set; } = default!;
    public decimal Total { get; set; }
    public ChargeBearerType ChargeBearer { get; set; } = default!;
    public string Debtor { get; set; } = default!;
    public string? DebtorPostalAddress { get; set; } = default!;
    public string DebtorAccount { get; set; } = default!;
    public string DebtorAccountType { get; set; } = default!;
    public string Creditor { get; set; } = default!;
    public string CreditorAccount { get; set; } = default!;
    public string CreditorAccountType { get; set; } = default!;
    public string InstructionForNextAgent { get; set; } = default!;
    public string Purpose { get; set; } = default!;
    public string RemittanceInformationStructuredType { get; set; } = default!;
    public string RemittanceInformationStructuredNumber { get; set; } = default!;
    public string RemittanceInformationUnstructured { get; set; } = default!;
    public TransactionStatus Status { get; set; } = default!;
}