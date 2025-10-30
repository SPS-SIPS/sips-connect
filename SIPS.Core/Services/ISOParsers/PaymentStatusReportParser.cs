using System.Xml.Linq;

namespace SIPS.Core.Services.ISOParsers;

public interface IPaymentStatusReportParser
{
    bool TryParse(string xml, out PaymentStatusReportParser.ReportRequest? request);
}

public sealed class PaymentStatusReportParser : IPaymentStatusReportParser
{
    public sealed record ReportRequest(
        string OriginalTxId,
        string? OriginalEndToEndId,
        string? Status,
        string? Reason,
        string? AdditionalInfo);

    public bool TryParse(string xml, out ReportRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try
        {
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            // identify by TxId or OrgnlTxId
            var txId = FindFirstValue(doc, "TxId") ?? FindFirstValue(doc, "OrgnlTxId");
            if (string.IsNullOrWhiteSpace(txId)) return false;

            // extract EndToEnd, TxSts (transaction status), reason and additional info
            var endToEnd = FindFirstValue(doc, "EndToEndId") ?? FindFirstValue(doc, "OrgnlEndToEndId");
            var status = FindFirstValue(doc, "TxSts");
            var reason = FindFirstValue(doc, "Rsn") ?? FindFirstValue(doc, "Reason");
            var additional = FindFirstValue(doc, "AddtlInf") ?? FindFirstValue(doc, "AdditionalInfo");

            request = new ReportRequest(txId.Trim(), endToEnd?.Trim(), status?.Trim(), reason?.Trim(), additional?.Trim());
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
