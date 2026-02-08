namespace SIPS.Example.Consumer.Models.CB;

public class StatusResponse
{
    public string FromBIC { get; set; } = default!;
    public string LocalInstrument { get; set; } = default!;
    public string CategoryPurpose { get; set; } = default!;
    public string EndToEndId { get; set; } = default!;
    public string TxId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    // Debtor
    public string DebtorName { get; set; } = default!;
    public string? DebtorPostalAddress { get; set; } = default!;
    public string DebtorAccount { get; set; } = default!;
    public string DebtorAccountType { get; set; } = default!;
    public string DebtorAgentBIC { get; set; } = default!;
    public string DebtorIssuer { get; set; } = "C";

    // Creditor
    public string CreditorName { get; set; } = default!;
    public string CreditorPostalAddress { get; set; } = default!;
    public string CreditorAccount { get; set; } = default!;
    public string CreditorAccountType { get; set; } = default!;
    public string CreditorAgentBIC { get; set; } = default!;
    public string CreditorIssuer { get; set; } = "C";

    public string RemittanceInformation { get; set; } = default!;
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? AdditionalInfo { get; set; }
    public DateTime AcceptanceDate { get; set; }

    // Extra
    public DateTime Date { get; set; }
    public string? SettlementMethod { get; set; }
    public string? ChargeBearer { get; set; }
    public string ToBIC { get; set; } = default!;
    public string BizMsgIdr { get; set; } = default!;
    public string MsgDefIdr { get; set; } = default!;
    public string ClearingSystem { get; set; } = default!;
    public string MsgId { get; set; } = default!;
}
