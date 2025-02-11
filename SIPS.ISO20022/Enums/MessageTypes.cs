namespace SIPS.ISO20022.Enums;
public sealed class SupportedMessageTypes : Enumeration<string>
{
    public static SupportedMessageTypes VerificationRequest = new("acmt.023.001.03", "Verification Request", "urn:iso:std:iso:20022:tech:xsd:acmt.023.001.03");
    public static SupportedMessageTypes VerificationResponse = new("acmt.024.001.03", "Verification Response", "urn:iso:std:iso:20022:tech:xsd:acmt.024.001.03");
    public static SupportedMessageTypes CreditTransferRequest = new("pacs.008.001.10", "Credit Transfer Request", "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.10");
    public static SupportedMessageTypes CreditTransferStatusRequest = new("pacs.028.001.05", "Credit Transfer Status Request", "urn:iso:std:iso:20022:tech:xsd:pacs.028.001.05");
    public static SupportedMessageTypes CreditTransferReturnRequest = new("pacs.004.001.11", "Credit Transfer Return Request", "");
    public static SupportedMessageTypes CreditTransferResponse = new("pacs.002.001.12", "Credit Related Response", "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.12");

    public SupportedMessageTypes()
    {
    }
    public SupportedMessageTypes(string id, string name, string groupId)
        : base(id, name, groupId)
    {
    }
}