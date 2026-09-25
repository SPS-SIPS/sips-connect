using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using SIPS.Connect.Services;
using SIPS.ISO20022.Helpers;
using Xunit;
using P = SIPS.Connect.Services.PapssFieldProvenance;

namespace SIPS.Connect.Tests;

/// <summary>The gateway's signed per-field provenance (SplmtryData PapssProvenance) on PAPSS pacs.002 / pacs.004 callbacks.</summary>
public sealed class PapssProvenanceUnitTests
{
    const string Raw = "sha256:0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";

    [Fact]
    public void Status_amount_provenance_is_read_and_its_absence_is_UNSPECIFIED_LEGACY()
    {
        var legacy = PapssPaymentOperationUnitTests.Pacs002("PAPSS-PRV-1", "M", "pacs.008.001.10", "TX-1", "E2E-1", "ACSC");
        var old = PapssPaymentMessages.ParseStatusReport(legacy);
        Assert.Null(old.Provenance);
        Assert.Equal(P.UnspecifiedLegacy, old.AmountSource);
        Assert.True(P.IsReportedAmount(old.AmountSource));

        var reconstructed = PapssPaymentMessages.ParseStatusReport(GatewayProvenanceXml.Add(legacy, Raw,
            (P.StatusAmountPath, P.LocalReconstruction), ("TxInfAndSts/OrgnlTxId", P.IdentifierTranslation), ("TxInfAndSts/OrgnlTxRef/Dbtr", P.DefaultFiller)));
        Assert.Equal(P.LocalReconstruction, reconstructed.AmountSource);
        Assert.False(P.IsReportedAmount(reconstructed.AmountSource));
        Assert.Equal(("PAPSS-PRV-1", Raw), (reconstructed.Provenance!.SourceMessageId, reconstructed.Provenance.RawEvidenceReference));
        Assert.Equal(P.IdentifierTranslation, reconstructed.Provenance.SourceOf("TxInfAndSts/OrgnlTxId"));
        // The amount itself is still read (it is the SIPS contract), only its meaning changes.
        Assert.Equal(10m, reconstructed.Amount);

        var reported = PapssPaymentMessages.ParseStatusReport(GatewayProvenanceXml.Add(legacy, Raw, (P.StatusAmountPath, P.NetworkReported)));
        Assert.Equal(P.NetworkReported, reported.AmountSource);Assert.True(P.IsReportedAmount(reported.AmountSource));

        // A supplement without the amount field (a legacy gateway effect) leaves the amount UNSPECIFIED_LEGACY.
        Assert.Equal(P.UnspecifiedLegacy, PapssPaymentMessages.ParseStatusReport(GatewayProvenanceXml.Add(legacy, Raw, ("TxInfAndSts/OrgnlTxId", P.IdentifierTranslation))).AmountSource);
    }

    [Fact]
    public void Return_category_purpose_provenance_is_read()
    {
        var pacs004 = ReturnPaymentRequestBuilder.Build(new() { From = "PHBXSLFR", To = "ZKBASOS0", CreDt = DateTime.UtcNow, NumberOfTransactions = 1, LocalInstrument = "USDP", CategoryPurpose = "CASH", ReturnId = "RTN-P", OrgnlTxId = "TX-9", OriginalEndToEnd = "E2E-9", OriginalCurrency = "USD", OriginalAmount = 10m, ReturnReason = "FOCR", AdditionalInfo = "requested", DebtorAgent = "PHBXSLFR", CreditorAgent = "ZKBASOS0" }).document;
        Assert.Equal((P.UnspecifiedLegacy, P.UnspecifiedLegacy), (PapssPaymentMessages.ParseReturn(pacs004).AmountSource, PapssPaymentMessages.ParseReturn(pacs004).CategoryPurposeSource));
        var message = PapssPaymentMessages.ParseReturn(GatewayProvenanceXml.Add(pacs004, Raw, (P.ReturnAmountPath, P.NetworkReported), (P.ReturnCategoryPurposePath, P.LocalReconstruction)));
        Assert.Equal((P.NetworkReported, P.LocalReconstruction, Raw), (message.AmountSource, message.CategoryPurposeSource, message.Provenance!.RawEvidenceReference));
    }

    [Fact]
    public void Inconsistent_or_unknown_provenance_is_refused()
    {
        var xml = PapssPaymentOperationUnitTests.Pacs002("PAPSS-PRV-2", "M", "pacs.008.001.10", "TX-2", "E2E-2", "ACSC");
        // Unknown vocabulary (UNSPECIFIED_LEGACY is receiver-side only and is never transmitted).
        Assert.Throws<InvalidDataException>(() => PapssPaymentMessages.ParseStatusReport(GatewayProvenanceXml.Add(xml, Raw, (P.StatusAmountPath, "PAPSS_STATUS"))));
        Assert.Throws<InvalidDataException>(() => PapssPaymentMessages.ParseStatusReport(GatewayProvenanceXml.Add(xml, Raw, (P.StatusAmountPath, P.UnspecifiedLegacy))));
        // Repeated field, or two supplements.
        Assert.Throws<InvalidDataException>(() => PapssPaymentMessages.ParseStatusReport(GatewayProvenanceXml.Add(xml, Raw, (P.StatusAmountPath, P.NetworkReported), (P.StatusAmountPath, P.LocalReconstruction))));
        Assert.Throws<InvalidDataException>(() => PapssPaymentMessages.ParseStatusReport(GatewayProvenanceXml.Add(GatewayProvenanceXml.Add(xml, Raw), Raw)));
        // The supplement must describe this PAPSS message.
        var other = XDocument.Parse(GatewayProvenanceXml.Add(xml, Raw, (P.StatusAmountPath, P.NetworkReported)));
        other.Descendants(XName.Get("SourceMessageId", P.Namespace)).Single().Value = "PAPSS-OTHER";
        Assert.Throws<InvalidDataException>(() => PapssPaymentMessages.ParseStatusReport(other.ToString()));
        // Only in the transaction's SplmtryData.
        var misplaced = XDocument.Parse(GatewayProvenanceXml.Add(xml, Raw));
        var supplement = misplaced.Descendants().Single(x => x.Name.LocalName == "SplmtryData");supplement.Remove();
        misplaced.Descendants().Single(x => x.Name.LocalName == "FIToFIPmtStsRpt").Add(supplement);
        Assert.Throws<InvalidDataException>(() => PapssPaymentMessages.ParseStatusReport(misplaced.ToString()));
    }

    [Theory]
    [InlineData("pacs.002.001.12")]
    [InlineData("pacs.004.001.11")]
    public void Supplement_shape_is_schema_valid_in_the_ISO_position(string definition)
    {
        var xml = definition == "pacs.002.001.12"
            ? GatewayProvenanceXml.Add(PapssPaymentOperationUnitTests.Pacs002("PAPSS-PRV-3", "M", "pacs.008.001.10", "TX-3", "E2E-3", "ACSC"), Raw, (P.StatusAmountPath, P.LocalReconstruction))
            : GatewayProvenanceXml.Add(ReturnPaymentRequestBuilder.Build(new() { From = "PHBXSLFR", To = "ZKBASOS0", CreDt = DateTime.UtcNow, NumberOfTransactions = 1, LocalInstrument = "USDP", CategoryPurpose = "CASH", ReturnId = "RTN-X", OrgnlTxId = "TX-9", OriginalEndToEnd = "E2E-9", OriginalCurrency = "USD", OriginalAmount = 10m, ReturnReason = "FOCR", AdditionalInfo = "requested", DebtorAgent = "PHBXSLFR", CreditorAgent = "ZKBASOS0" }).document, Raw, (P.ReturnCategoryPurposePath, P.LocalReconstruction));
        var schemas = new XmlSchemaSet { XmlResolver = null };
        foreach (var file in new[] { definition + ".xsd", "SPS.PAPSS.PROVENANCE.001.xsd" })
            using (var reader = XmlReader.Create(Path.Combine(AppContext.BaseDirectory, "ProvenanceSchemas", file))) schemas.Add(null, reader);
        schemas.Compile();
        var document = XDocument.Parse(xml).Descendants().Single(x => x.Name.LocalName == "Document" && x.Name.NamespaceName.EndsWith(definition, StringComparison.Ordinal));
        var errors = new List<string>();
        new XDocument(new XElement(document)).Validate(schemas, (_, e) => errors.Add(e.Message));
        Assert.True(errors.Count == 0, string.Join(" | ", errors));
    }
}
