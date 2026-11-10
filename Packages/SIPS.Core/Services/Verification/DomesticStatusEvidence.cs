using System.Text;
using System.Xml.Linq;
using SIPS.ISO20022.Helpers;
using SIPS.PostgreSQL.Models;

namespace SIPS.Core.Services.Verification;

/// <summary>Correlates stored/live network reports; live callers must verify signatures first.</summary>
internal static class DomesticStatusEvidence
{
    public static bool TryRead(ISOMessage payment, string? xml, out PaymentRequestResponseBuilder.Response? report)
    {
        report = null;
        if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(payment.TxId)) return false;
        try
        {
            var parsed = PaymentRequestResponseBuilder.Parse(xml);
            if (parsed is null || parsed.TxId != payment.TxId
                || parsed.Original?.EndToEndId != payment.EndToEndId) return false;
            // The general parser defaults missing TxSts to RJCT. A default is not
            // network evidence. Multiple transaction entries are ambiguous here.
            XNamespace ns = "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.12";
            var entries = XDocument.Parse(xml).Descendants(ns + "TxInfAndSts").ToArray();
            if (entries.Length != 1) return false;
            var status = (string?)entries[0].Element(ns + "TxSts");
            if (string.IsNullOrWhiteSpace(status) || status != parsed.Status) return false;
            report = parsed;
            return true;
        }
        catch (Exception) { return false; } // Invalid or incomplete network XML is unresolved.
    }

    public static bool TryReadStored(ISOMessage payment, out PaymentRequestResponseBuilder.Response? report)
        => TryRead(payment, payment.Response is { Length: > 0 } ? Encoding.UTF8.GetString(payment.Response) : null, out report);
}
