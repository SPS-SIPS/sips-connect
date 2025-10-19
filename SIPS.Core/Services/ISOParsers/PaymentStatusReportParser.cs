using System.Xml.Linq;

namespace SIPS.Core.Services.ISOParsers;

public interface IPaymentStatusReportParser
{
    bool TryParse(string xml, out PaymentStatusReportParser.ReportRequest? request);
}

public sealed class PaymentStatusReportParser : IPaymentStatusReportParser
{
    public sealed record ReportRequest(string OrgnlTxId);

    public bool TryParse(string xml, out ReportRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try
        {
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            // look for TxId or OrgnlTxId anywhere
            var txId = FindFirstValue(doc, "TxId") ?? FindFirstValue(doc, "OrgnlTxId");
            if (string.IsNullOrWhiteSpace(txId)) return false;
            request = new ReportRequest(txId.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindFirstValue(XDocument doc, string localName)
    {
        foreach (var el in doc.Descendants())
        {
            if (el.Name.LocalName == localName)
                return el.Value;
        }
        return null;
    }
}
