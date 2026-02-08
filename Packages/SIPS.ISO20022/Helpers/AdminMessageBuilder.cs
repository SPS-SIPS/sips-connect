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
        var timestamp = DateTime.UtcNow.ToString("o");
        var msgId = Guid.NewGuid().ToString();
        
        var message = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Document xmlns=""urn:iso:std:iso:20022:tech:xsd:admi.002.001.01"">
  <MsgRjct>
    <RltdRef>
      <Ref>{originalMsgId ?? msgId}</Ref>
    </RltdRef>
    <Rsn>
      <RjctgPtyRsn>{reasonCode}</RjctgPtyRsn>
      {(string.IsNullOrWhiteSpace(additionalInfo) ? "" : $"<AddtlRsnInf>{System.Security.SecurityElement.Escape(additionalInfo)}</AddtlRsnInf>")}
    </Rsn>
  </MsgRjct>
</Document>";

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
