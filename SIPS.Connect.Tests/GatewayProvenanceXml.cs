using System.Xml.Linq;

namespace SIPS.Connect.Tests;

/// <summary>Adds the gateway's SPS provenance supplement exactly where and how the gateway's callback builder puts it.</summary>
internal static class GatewayProvenanceXml
{
    public const string Namespace = "urn:sps:papss:provenance:001";

    public static string Add(string xml, string rawEvidenceReference, params (string Path, string Source)[] fields)
    {
        var document = XDocument.Parse(xml);
        var source = document.Descendants().First(x => x.Name.LocalName == "AppHdr").Elements().First(x => x.Name.LocalName == "BizMsgIdr").Value;
        var tx = document.Descendants().Single(x => x.Name.LocalName is "TxInfAndSts" or "TxInf");
        var root = tx.Parent!.Name.LocalName;
        XNamespace iso = tx.Name.Namespace; XNamespace p = Namespace;
        tx.Add(new XElement(iso + "SplmtryData",
            new XElement(iso + "PlcAndNm", $"/Document/{root}/{tx.Name.LocalName}/SplmtryData"),
            new XElement(iso + "Envlp", new XElement(p + "PapssProvenance",
                new XElement(p + "SourceMessageId", source),
                new XElement(p + "RawEvidenceReference", rawEvidenceReference),
                fields.Select(f => new XElement(p + "Field", new XElement(p + "Path", f.Path), new XElement(p + "Source", f.Source)))))));
        return document.ToString(SaveOptions.DisableFormatting);
    }
}
