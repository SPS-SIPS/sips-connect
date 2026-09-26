using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Connect.Services;
using SIPS.PostgreSQL.Enums;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using SIPS.XMLDsig.Xades.Options;
using SIPS.XMLDsig.Xades.Services;
using Xunit;

namespace SIPS.Connect.Tests;

/// <summary>
/// The literal, unmodified bytes the Gateway POSTed for the S3 RECALL_OUTCOME_UNRESOLVED signal (real, gateway-synthesized
/// pacs.002.001.12; not hand-written). See art/papss/evidence/recall-outcome-unresolved-callback-20260926.md.
///
/// We do not have the Gateway's signing certificate: it was an ephemeral, in-memory self-signed test certificate created
/// fresh for that one test run (Sps.Papss.Service.Tests, "WP-HOST SIPS Test CA" / "CN=PAPSS") and never exported alongside
/// the captured XML, and the signature's KeyInfo carries only an X509IssuerSerial (no embedded certificate) - a Guevara-style
/// registry lookup by issuer+serial is required to obtain the public key, exactly as production does. Full cryptographic
/// trust validation of this exact fixture is therefore not possible without the Gateway also sharing that certificate's
/// public PEM (no private key needed). What IS provable, and what this test proves: our verifier accepts this real
/// signature's structure - references, algorithms, canonicalization, KeyInfo, XAdES QualifyingProperties, exactly the
/// WpSipsPapss profile shape our own signer produces and our verifier expects - and fails ONLY at certificate resolution,
/// never at structural/reference validation. The domain-level correlation/outcome/idempotency tests
/// (SIPS.Connect.PostgresTests.PapssRecallOperationTests) exercise the same literal bytes through the real parsing and
/// state-machine path below the signature check, which is where the S3 recall-recovery behaviour actually lives.
/// </summary>
public sealed class PapssRecallCapturedFixtureTests
{
    public const string FixtureSha256 = "031a4692cc5eafb313b55e90236f9d35437ec07dfb25e7d6037df30c53f8cac3";

    public static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "recall-outcome-unresolved-callback-20260926.xml");

    public static string LoadFixture()
    {
        var xml = File.ReadAllText(FixturePath);
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))).ToLowerInvariant();
        Assert.Equal(FixtureSha256, actual);
        return xml;
    }

    [Fact]
    public void Fixture_bytes_match_the_documented_gateway_capture_hash()
        => LoadFixture(); // the SHA-256 assertion is inside LoadFixture(); this proves the copied file was not retyped.

    [Fact]
    public async Task Captured_payload_is_a_structurally_valid_wp_sips_xades_bes_signature()
    {
        var xml = LoadFixture();
        var options = new XadesOptions();
        var download = new Mock<ICertificateDownloadService>();
        download.Setup(x => x.GetCertificatesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((CertificateDownloadResponse?)null, "no certificate registered for this fixture test"));
        var verifier = new NativeVerifier(options, NullLogger<NativeVerifier>.Instance, new NoOpCertificateService(NullLogger<NoOpCertificateService>.Instance), download.Object);

        // VerifySignature (unlike VerifyWithProvenance) runs the WP-SIPS/PAPSS structural profile checks (reference count,
        // algorithms, canonicalization, KeyInfo/SignedProperties binding, the enveloped BusinessLayer reference) BEFORE
        // attempting certificate resolution. No exception here means the real captured signature's shape is accepted;
        // CertificateStatus "Invalid" then confirms the ONLY failure is "no certificate available", not anything structural.
        var (result, verbose) = await verifier.VerifySignature(xml, checkOwnerShip: true, XadesProfile.WpSipsPapss, CancellationToken.None);
        Assert.False(result);
        Assert.Equal("Invalid", verbose.CertificateStatus);
        Assert.Equal("Not Verified", verbose.SignatureStatus);
    }

    [Fact]
    public void Captured_payload_carries_the_confirmed_wire_fields()
    {
        var xml = LoadFixture();
        var document = System.Xml.Linq.XDocument.Parse(xml);
        var appHdr = document.Descendants().Single(x => x.Name.LocalName == "AppHdr");
        Assert.Equal("pacs.002.001.12", appHdr.Elements().Single(x => x.Name.LocalName == "MsgDefIdr").Value);
        Assert.Equal("WP-SIPS-XADES-SHA256RSA", appHdr.Elements().Single(x => x.Name.LocalName == "BizSvc").Value);
        var rltd = appHdr.Elements().Single(x => x.Name.LocalName == "Rltd");
        Assert.Equal("SIPS-0123456789abcdef0123cccc", rltd.Elements().Single(x => x.Name.LocalName == "BizMsgIdr").Value);
        Assert.Equal("camt.056.001.08", rltd.Elements().Single(x => x.Name.LocalName == "MsgDefIdr").Value);

        var tx = document.Descendants().Single(x => x.Name.LocalName == "TxInfAndSts");
        var group = tx.Elements().Single(x => x.Name.LocalName == "OrgnlGrpInf");
        Assert.Equal("SIPS-0123456789abcdef0123cccc", group.Elements().Single(x => x.Name.LocalName == "OrgnlMsgId").Value);
        Assert.Equal("camt.056.001.08", group.Elements().Single(x => x.Name.LocalName == "OrgnlMsgNmId").Value);
        Assert.Equal("E2E-OUT-1", tx.Elements().Single(x => x.Name.LocalName == "OrgnlEndToEndId").Value);
        Assert.Equal("TX-OUT-1", tx.Elements().Single(x => x.Name.LocalName == "OrgnlTxId").Value);
        Assert.Equal("PDNG", tx.Elements().Single(x => x.Name.LocalName == "TxSts").Value);
        var rsn = tx.Elements().Single(x => x.Name.LocalName == "StsRsnInf").Elements().Single(x => x.Name.LocalName == "Rsn");
        Assert.Null(rsn.Elements().SingleOrDefault(x => x.Name.LocalName == "Cd"));
        Assert.Equal("RECALL_OUTCOME_UNRESOLVED", rsn.Elements().Single(x => x.Name.LocalName == "Prtry").Value);
        // Free text varies by failure mode; only assert it is non-empty, not its exact content.
        Assert.False(string.IsNullOrWhiteSpace(tx.Elements().Single(x => x.Name.LocalName == "StsRsnInf").Elements().Single(x => x.Name.LocalName == "AddtlInf").Value));
    }

    [Fact]
    public void Real_payload_parses_through_the_production_pipeline_to_the_unresolved_outcome()
    {
        // The same PapssPaymentMessages/PapssRecallRules path the store uses, driven by the literal captured bytes.
        var xml = LoadFixture();
        var report = PapssPaymentMessages.ParseStatusReport(xml);
        Assert.Equal(("SIPS-0123456789abcdef0123cccc", "camt.056.001.08", "TX-OUT-1", "E2E-OUT-1", "PDNG", "RECALL_OUTCOME_UNRESOLVED"),
            (report.OriginalMessageId, report.OriginalMessageType, report.OriginalTxId, report.OriginalEndToEndId, report.Status, report.ReasonCode));
        Assert.True(PapssRecallMessages.IsRecallAnswer(report.OriginalMessageType));
        Assert.Equal(PapssOutcome.RecallOutcomeUnresolved, PapssRecallRules.OutcomeOfPapssAnswer(report.Status, report.ReasonCode));
        Assert.Equal(PapssEventDisposition.Applied, PapssRecallRules.EvaluatePapssStatus(PapssOutcome.RecallPending, report.Status, report.ReasonCode));

        // The gateway-internal PapssProvenance supplement (never sent to PAPSS) is still readable despite the known,
        // documented cosmetic bug that mislabels this synthesized message's own TxSts/StsRsnInf as NETWORK_REPORTED.
        Assert.NotNull(report.Provenance);
        Assert.Equal("b4a8d1ef005c84998d6b1e504f0b8d1a", report.Provenance!.SourceMessageId);
        Assert.Equal("1e16475886d80cd551aa9c3da532abaa926b5d9bfc80c3ed965a03624cd6e456", report.Provenance.RawEvidenceReference);
    }
}
