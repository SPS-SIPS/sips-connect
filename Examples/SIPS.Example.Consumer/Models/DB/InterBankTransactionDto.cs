using System;
using SIPS.Example.Consumer.Enums;

namespace SIPS.Example.Consumer.Models.DB;

public class InterBankTransactionDto
{
    public Guid Id { get; set; }
    public string VerificationMessageId { get; set; } = string.Empty;
    public string BizMsgIdr { get; set; } = string.Empty;
    public string MsgDefIdr { get; set; } = string.Empty;
    public DateTimeOffset CreDt { get; set; }
    public DateTimeOffset CreDtTm { get; set; }
    public int NbOfTxs { get; set; } = 1;
    public string SettlementMethod { get; set; } = string.Empty;
    public string ClearingSystem { get; set; } = string.Empty;
    public string PaymentTypeInformation { get; set; } = string.Empty;
    public string PaymentTypeInformationCategoryPurpose { get; set; } = string.Empty;
    public string InstructingAgent { get; set; } = string.Empty;
    public string InstructedAgent { get; set; } = string.Empty;
    public string EndToEndIdentification { get; set; } = string.Empty;
    public string TransactionIdentification { get; set; } = string.Empty;
    public string? UETR { get; set; }
    public decimal InterbankSettlementAmount { get; set; }
    public string InterbankSettlementCurrency { get; set; } = string.Empty;
    public DateTimeOffset AcceptanceDateTime { get; set; }
    public decimal InstructedAmount { get; set; }
    public string InstructedCurrency { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string ChargeBearer { get; set; } = string.Empty;
    public string Debtor { get; set; } = string.Empty;
    public string? DebtorPostalAddress { get; set; } = string.Empty;
    public string DebtorAccount { get; set; } = string.Empty;
    public string DebtorAccountType { get; set; } = string.Empty;
    public string Creditor { get; set; } = string.Empty;
    public string CreditorAccount { get; set; } = string.Empty;
    public string CreditorAccountType { get; set; } = string.Empty;
    public string InstructionForNextAgent { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string RemittanceInformationStructuredType { get; set; } = string.Empty;
    public string RemittanceInformationStructuredNumber { get; set; } = string.Empty;
    public string RemittanceInformationUnstructured { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
