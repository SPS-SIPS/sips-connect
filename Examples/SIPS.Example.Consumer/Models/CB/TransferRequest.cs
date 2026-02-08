namespace SIPS.Example.Consumer.Models.CB;

public class TransferRequest
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
    public string DebtorAccount { get; set; } = default!;
    public string DebtorAccountType { get; set; } = default!;
    public string? DebtorAddress { get; set; }
    public string? DebtorAgentBIC { get; set; }
    public string DebtorIssuer { get; set; } = "C";

    // Creditor
    public string CreditorName { get; set; } = default!;
    public string CreditorAccount { get; set; } = default!;
    public string CreditorAccountType { get; set; } = default!;
    public string? CreditorAddress { get; set; }
    public string CreditorAgentBIC { get; set; } = default!;
    public string CreditorIssuer { get; set; } = "C";

    public string RemittanceInformation { get; set; } = default!;

    // Extra
    public DateTime Date { get; set; }
    public string ToBIC { get; set; } = default!;
    public string? SettlementMethod { get; set; }
    public string? ChargeBearer { get; set; }

    public string? BizMsgIdr { get; set; }
    public string? MsgDefIdr { get; set; }
    public string? ClearingSystem { get; set; }
    public string? MsgId { get; set; }
}
