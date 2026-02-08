namespace SIPS.ISO20022.Helpers;

/// <summary>
/// Builder for admi.002 (MessageReject) responses per SmartVista IPS protocol.
/// Used for technical/protocol errors: invalid XML, signature failures, duplicate IDs, missing mandatory elements.
/// </summary>
public static class AdminMessageBuilder
{
    /// <summary>
    /// Generates a simple admi.002-style error message for technical/protocol rejections.
    /// NOTE: This is a simplified implementation pending full ISO20022 admi.002 schema integration.
    /// </summary>
    public static string Generate(
        string reasonCode,
        string? additionalInfo = null,
        string? originalMsgId = null)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var msgId = Guid.NewGuid().ToString();
        var message = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<BusinessLayer xmlns:header=""urn:iso:std:iso:20022:tech:xsd:head.001.001.03"" xmlns:document=""urn:iso:std:iso:20022:tech:xsd:admi.002.001.01"">
  <header:AppHdr>
    <header:BizMsgIdr>{msgId}</header:BizMsgIdr>
    <header:MsgDefIdr>admi.002.001.01</header:MsgDefIdr>
    <header:CreDt>{timestamp}</header:CreDt>
  </header:AppHdr>
  <document:Document>
    <document:MsgRjct>
      <document:RltdRef>
        <document:Ref>{originalMsgId ?? msgId}</document:Ref>
      </document:RltdRef>
      <document:Rsn>
        <document:RjctgPtyRsn>{reasonCode}</document:RjctgPtyRsn>
        {(string.IsNullOrWhiteSpace(additionalInfo) ? "" : $"<document:AddtlRsnInf>{System.Security.SecurityElement.Escape(additionalInfo)}</document:AddtlRsnInf>")}
      </document:Rsn>
    </document:MsgRjct>
  </document:Document>
</BusinessLayer>";

        return message;
    }
}

/// <summary>
/// SmartVista-aligned reason codes for admi.002 technical rejections.
/// </summary>
public static class AdminRejectReasonCodes
{
    public const string InvalidXML = "InvalidXML";
    public const string SignatureInvalid = "SignatureInvalid";
    public const string MandatoryElementMissing = "MandatoryElementMissing";
    public const string DuplicateMessageID = "DuplicateMessageID";
    public const string DuplicateMessageInProcess = "DuplicateMessageInProcess";
    public const string TechnicalError = "TechnicalError";
}
