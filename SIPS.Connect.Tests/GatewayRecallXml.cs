using System.Xml.Linq;
using SIPS.ISO20022.Helpers;

namespace SIPS.Connect.Tests;

/// <summary>The answers to our camt.056 exactly as the gateway contract (R1) delivers them on /api/v1/Incoming.</summary>
internal static class GatewayRecallXml
{
    public const string Camt029Namespace = "urn:iso:std:iso:20022:tech:xsd:camt.029.001.09";
    private const string HeaderNamespace = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";

    /// <summary>
    /// pacs.002.001.12: AppHdr BizMsgIdr = PAPSS source id, OrgnlGrpInf{OrgnlMsgId = recall id, OrgnlMsgNmId = camt.056.001.08},
    /// TxInfAndSts{StsId, OrgnlEndToEndId / OrgnlTxId = the ORIGINAL PAYMENT's bank ids, TxSts ACCP|RJCT, StsRsnInf}.
    /// </summary>
    public static string RecallStatus(string sourceMessageId, string recallId, string txId, string endToEndId, string status, string? reason = null,
        string from = "WPSIPSGW", string to = "ZKBASOS0", string statusId = "PS0001")
    {
        var xml = PaymentRequestResponseBuilder.Build(new PaymentRequestResponseBuilder.Response
        {
            From = from,
            To = to,
            Original = new PaymentRequestBuilder.Request
            {
                From = to, To = from, BizMsgIdr = recallId, MsgDefIdr = "camt.056.001.08", MsgId = recallId,
                CreDt = DateTime.UtcNow.AddMinutes(-1), EndToEndId = endToEndId, TxId = txId, Amount = 1m, Currency = "USD"
            },
            Status = status,
            Reason = reason,
            AdditionalInfo = reason is null ? null : "recall refused by PAPSS"
        });
        var document = XDocument.Parse(xml);
        document.Descendants().First(x => x.Name.LocalName == "AppHdr").Elements().First(x => x.Name.LocalName == "BizMsgIdr").Value = sourceMessageId;
        var tx = document.Descendants().Single(x => x.Name.LocalName == "TxInfAndSts");
        tx.AddFirst(new XElement(tx.Name.Namespace + "StsId", statusId));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// camt.029.001.09 RsltnOfInvstgtn: Assgnmt{Id = source id, Assgnr = gateway, Assgne = bank}, RslvdCase/Id = recall id (optional),
    /// Sts/Conf, CxlDtls/TxInfAndSts{CxlStsId, OrgnlGrpInf{payment SIPS MsgId, pacs.008.001.10}, OrgnlEndToEndId, OrgnlTxId, CxlStsRsnInf}.
    /// </summary>
    public static string Resolution(string sourceMessageId, string? recallId, string originalMessageId, string txId, string endToEndId,
        string confirmation = "RJCR", string reason = "CUST", string from = "WPSIPSGW", string to = "ZKBASOS0", string? businessService = null, string responderId = "RSP-CXL-1")
    {
        XNamespace h = HeaderNamespace, d = Camt029Namespace, e = "urn:iso:std:iso:20022:tech:xsd:recallResolution_request";
        XElement Party(string name, string id) => new(h + name, new XElement(h + "FIId", new XElement(h + "FinInstnId", new XElement(h + "Othr", new XElement(h + "Id", id)))));
        XElement Agent(string name, string id) => new(d + name, new XElement(d + "Agt", new XElement(d + "FinInstnId", new XElement(d + "Othr", new XElement(d + "Id", id)))));
        var created = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var envelope = new XElement(e + "FPEnvelope",
            new XAttribute(XNamespace.Xmlns + "header", HeaderNamespace),
            new XAttribute(XNamespace.Xmlns + "document", Camt029Namespace),
            new XAttribute("Id", "BL-" + sourceMessageId),
            new XElement(h + "AppHdr",
                Party("Fr", from), Party("To", to),
                new XElement(h + "BizMsgIdr", sourceMessageId),
                new XElement(h + "MsgDefIdr", "camt.029.001.09"),
                businessService is null ? null : new XElement(h + "BizSvc", businessService),
                new XElement(h + "CreDt", created)),
            new XElement(d + "Document",
                new XElement(d + "RsltnOfInvstgtn",
                    new XElement(d + "Assgnmt", new XElement(d + "Id", sourceMessageId), Agent("Assgnr", from), Agent("Assgne", to), new XElement(d + "CreDtTm", created)),
                    recallId is null ? null : new XElement(d + "RslvdCase", new XElement(d + "Id", recallId), new XElement(d + "Cretr", new XElement(d + "Agt", new XElement(d + "FinInstnId", new XElement(d + "BICFI", to))))),
                    new XElement(d + "Sts", new XElement(d + "Conf", confirmation)),
                    new XElement(d + "CxlDtls",
                        new XElement(d + "TxInfAndSts",
                            new XElement(d + "CxlStsId", responderId),
                            new XElement(d + "OrgnlGrpInf", new XElement(d + "OrgnlMsgId", originalMessageId), new XElement(d + "OrgnlMsgNmId", "pacs.008.001.10")),
                            new XElement(d + "OrgnlEndToEndId", endToEndId),
                            new XElement(d + "OrgnlTxId", txId),
                            new XElement(d + "CxlStsRsnInf", new XElement(d + "Rsn", new XElement(d + "Cd", reason)), new XElement(d + "AddtlInf", "customer declined")))))));
        return envelope.ToString(SaveOptions.DisableFormatting);
    }
}
