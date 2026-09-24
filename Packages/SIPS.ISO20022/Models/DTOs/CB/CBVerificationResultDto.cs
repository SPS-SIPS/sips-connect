namespace SIPS.ISO20022.Models.DTOs.CB;

/// <summary>
/// Asynchronous payee-verification result delivered to the participant bank when a
/// verification report (acmt.024) arrives on the incoming endpoint, e.g. the result of a
/// cross-border PAPSS name enquiry. Mapped through the <c>CB_VerificationResult</c> JsonAdapter mapping.
/// </summary>
public sealed class CBVerificationResultDto
{
    /// <summary>The id returned to the bank as <c>requestMessageId</c> when the verification was submitted (original BAH BizMsgIdr).</summary>
    public string RequestMessageId { get; set; } = string.Empty;
    /// <summary>The original acmt.023 Assgnmt/MsgId echoed in OrgnlAssgnmt.</summary>
    public string OriginalMsgId { get; set; } = string.Empty;
    /// <summary>The original verification identifier (acmt.023 Vrfctn/Id, echoed as Rpt/OrgnlId).</summary>
    public string VerificationId { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string? AccountName { get; set; }
    public string? Address { get; set; }
    public string? Currency { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? AdditionalInfo { get; set; }
    /// <summary>Participant that produced the report (BAH From).</summary>
    public string FromBIC { get; set; } = string.Empty;
    /// <summary>Local participant the report is addressed to (BAH To).</summary>
    public string ToBIC { get; set; } = string.Empty;
    /// <summary>BizMsgIdr of the acmt.024 report itself.</summary>
    public string ResponseMessageId { get; set; } = string.Empty;
}
