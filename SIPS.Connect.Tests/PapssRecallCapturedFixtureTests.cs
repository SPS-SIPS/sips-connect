using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Org.BouncyCastle.OpenSsl;
using SIPS.Connect.Services;
using SIPS.PostgreSQL.Enums;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using SIPS.XMLDsig.Xades.Options;
using SIPS.XMLDsig.Xades.Services;
using Xunit;
using static SIPS.XMLDsig.Xades.Helpers.XmlSecurityHelpers;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;

namespace SIPS.Connect.Tests;

/// <summary>
/// The literal, unmodified bytes the Gateway POSTed for the S3 RECALL_OUTCOME_UNRESOLVED signal (real, gateway-synthesized
/// pacs.002.001.12; not hand-written), paired with the Gateway's own public signer certificate (no private key) captured
/// from the same test run. See art/papss/evidence/recall-outcome-unresolved-callback-20260926.md. The Gateway confirmed the
/// certificate's DER SHA-256 matches the signature's XAdES CertDigest byte-for-byte and that the signature value is a
/// genuine 2048-bit RSA-SHA256 signature.
///
/// What this proves and what it does not: the certificate's issuer ("WP-HOST SIPS Test CA") is an ephemeral test CA that
/// existed only in that one Gateway test run; we have the leaf ("CN=PAPSS") but not that CA's own certificate, so this
/// participant's own configured trust chain (SIPS.XMLDsig.Xades.Services.CertificateService.CheckValidity, which walks the
/// signing certificate up to a configured root) cannot complete for this specific ephemeral test authority - in a real
/// deployment the configured chain holds the actual PAPSS/Guevara-issued root, not a one-off test CA. That is a
/// chain-of-trust-to-root question, separate from whether the signature bytes are a genuine RSA-SHA256 signature produced
/// by the private key matching this real certificate: that is exactly what
/// <see cref="Captured_payload_has_a_genuine_rsa_sha256_signature_verified_against_the_real_certificate"/> proves, calling
/// the verifier's own signature-value check (NativeVerifier.VerifySignatureValue) with the real public key. The domain-level
/// correlation/outcome/idempotency/operator-close tests (SIPS.Connect.PostgresTests.PapssRecallOperationTests) exercise the
/// same literal bytes through the real parsing and state-machine path below the signature check.
/// </summary>
public sealed class PapssRecallCapturedFixtureTests
{
    public const string FixtureSha256 = "aede643226bbc9b3cc6213c5a61ab1abdaf141e04619b7ac01862916edda4660";
    public const string CertificateSha256 = "20306b94c7ca54327dc4bd2c1df7c60bcd49960b3846222ffbc9e2411c16e3e7";
    public const string RemoteWpSipsIdentity = "PAPSS";
    private const string RequiredEku = "1.3.6.1.5.5.7.3.2"; // TLS Web Client Authentication, matching the certificate's own EKU.

    public static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "recall-outcome-unresolved-callback-20260926.xml");
    public static string CertificatePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "recall-outcome-unresolved-callback-signer-20260926.pem");

    public static string LoadFixture()
    {
        var xml = File.ReadAllText(FixturePath);
        Assert.Equal(FixtureSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))).ToLowerInvariant());
        return xml;
    }

    public static string LoadCertificatePem()
    {
        var pem = File.ReadAllText(CertificatePath);
        using var reader = new StringReader(pem);
        var certificate = (BcX509Certificate)new PemReader(reader).ReadObject();
        Assert.Equal(CertificateSha256, Convert.ToHexString(SHA256.HashData(certificate.GetEncoded())).ToLowerInvariant());
        return pem;
    }

    [Fact]
    public void Fixture_bytes_match_the_documented_gateway_capture_hash()
        => LoadFixture(); // the SHA-256 assertion is inside LoadFixture(); this proves the copied file was not retyped.

    [Fact]
    public void Certificate_bytes_match_the_documented_gateway_fingerprint()
        => LoadCertificatePem(); // the SHA-256 assertion is inside LoadCertificatePem().

    /// <summary>
    /// Genuine cryptographic verification: calls the same RSA-SHA256 signature-value check NativeVerifier uses internally
    /// (NativeVerifier.VerifySignatureValue, private - invoked via reflection since it is not part of the public
    /// interface) with the real certificate's real public key, over the exact canonicalized SignedInfo the Gateway signed.
    /// This is not a structural/shape check: it is the actual RSA-SHA256 mathematics, and it must return true only if the
    /// signature bytes in the captured XML were produced by the private key matching this certificate.
    /// </summary>
    [Fact]
    public void Captured_payload_has_a_genuine_rsa_sha256_signature_verified_against_the_real_certificate()
    {
        var xml = LoadFixture();
        var pem = LoadCertificatePem();
        using var reader = new StringReader(pem);
        var certificate = (BcX509Certificate)new PemReader(reader).ReadObject();

        var envelope = GetAsXmlDocument(xml);
        var signatureElement = GetFirstOfXmlElementsByTagOrNull(envelope.DocumentElement!, "ds:Signature")!;
        var signedInfoElement = GetFirstOfXmlElementsByTagWithPrefix(signatureElement, "ds:SignedInfo");
        var signatureMethod = GetFirstOfXmlElementsByTagWithPrefix(signedInfoElement, "ds:SignatureMethod");

        var method = typeof(NativeVerifier).GetMethod("VerifySignatureValue", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NativeVerifier.VerifySignatureValue was not found; the verifier's internals changed.");
        var isValid = (bool)method.Invoke(null, [signatureElement, signedInfoElement, certificate, signatureMethod, XadesProfile.WpSipsPapss])!;

        Assert.True(isValid, "The captured signature must verify as a genuine RSA-SHA256 signature against the Gateway's real public key.");

        // Negative control: flipping one byte of the signature value must fail verification (proves this is real crypto,
        // not a check that trivially returns true).
        var tampered = new XmlDocument { PreserveWhitespace = true };
        tampered.LoadXml(xml);
        var tamperedSignature = (XmlElement)GetFirstOfXmlElementsByTagOrNull(tampered.DocumentElement!, "ds:Signature")!;
        var tamperedSignedInfo = GetFirstOfXmlElementsByTagWithPrefix(tamperedSignature, "ds:SignedInfo");
        var tamperedMethod = GetFirstOfXmlElementsByTagWithPrefix(tamperedSignedInfo, "ds:SignatureMethod");
        var signatureValueElement = GetFirstOfXmlElementsByTagWithPrefix(tamperedSignature, "ds:SignatureValue");
        var bytes = Convert.FromBase64String(signatureValueElement.InnerText);
        bytes[0] ^= 0xFF;
        signatureValueElement.InnerText = Convert.ToBase64String(bytes);
        var tamperedValid = (bool)method.Invoke(null, [tamperedSignature, tamperedSignedInfo, certificate, tamperedMethod, XadesProfile.WpSipsPapss])!;
        Assert.False(tamperedValid);
    }

    /// <summary>
    /// The full production path (NativeVerifier.VerifySignature, WpSipsPapss profile) with the real certificate supplied
    /// through the certificate-download step: it now progresses past "no certificate available" (the previous fixture's
    /// result) through certificate parsing, owner/EKU/fingerprint pinning (all pass, using this real certificate) and stops
    /// at chain-of-trust-to-root, because our test CertificateService is not configured with the Gateway's ephemeral
    /// "WP-HOST SIPS Test CA" as a trusted root (see class summary). This documents precisely where full end-to-end
    /// verification would need one more artifact (that CA's own certificate) to close completely.
    /// </summary>
    [Fact]
    public async Task Captured_payload_passes_certificate_pinning_and_stops_only_at_chain_of_trust_to_the_ephemeral_test_ca()
    {
        var xml = LoadFixture();
        var pem = LoadCertificatePem();
        var fingerprint = CertificateSha256;

        var download = new Mock<ICertificateDownloadService>();
        download.Setup(x => x.GetCertificatesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync((new CertificateDownloadResponse(pem, RemoteWpSipsIdentity, false, "SPS-AUTH", "TEST", RemoteWpSipsIdentity, fingerprint, "v1", RequiredEku), (string?)null));

        using var pki = new ThrowawayCertificateService();
        var messages = new List<string>();
        var logger = new CapturingLogger<NativeVerifier>(messages);
        var options = new XadesOptions { VerificationWindowMinutes = 100 };
        var timeProvider = new FixedTimeProvider(DateTimeOffset.Parse("2026-09-26T08:39:00Z")); // shortly after the fixture's SigningTime, inside its validity window
        var verifier = new NativeVerifier(options, logger, pki.Service, download.Object, timeProvider);

        var (result, verbose) = await verifier.VerifySignature(xml, checkOwnerShip: true, XadesProfile.WpSipsPapss, CancellationToken.None);

        Assert.False(result);
        Assert.Equal("Invalid", verbose.CertificateStatus);
        // Confirms the failure is specifically chain-of-trust (the ephemeral test CA is not in our configured chain), not
        // "certificate not found" (the previous fixture's failure mode) and not a rejection of the certificate's own
        // owner/EKU/fingerprint (all of which this real certificate satisfies).
        Assert.Contains(messages, m => m.Contains("Issuer not found in configured SPS chain", StringComparison.Ordinal));
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

    /// <summary>A throwaway, currently-valid self-signed certificate chain, only to satisfy CertificateService's own eager
    /// file loading at construction; it is never the certificate being verified in these tests.</summary>
    private sealed class ThrowawayCertificateService : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "papss-recall-fixture-pki-" + Guid.NewGuid().ToString("N"));
        public CertificateService Service { get; }

        public ThrowawayCertificateService()
        {
            Directory.CreateDirectory(directory);
            using var rootKey = RSA.Create(2048);
            var rootRequest = new CertificateRequest("CN=Throwaway Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            using var leafKey = RSA.Create(2048);
            var leafRequest = new CertificateRequest("CN=Throwaway Leaf", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using var unsigned = leafRequest.Create(root, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
            using var leaf = unsigned.CopyWithPrivateKey(leafKey);
            File.WriteAllText(Path.Combine(directory, "leaf.pem"), leaf.ExportCertificatePem());
            File.WriteAllText(Path.Combine(directory, "key.pem"), leafKey.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(Path.Combine(directory, "root.pem"), root.ExportCertificatePem());
            Service = new CertificateService(new XadesOptions
            {
                CertificatePath = Path.Combine(directory, "leaf.pem"),
                PrivateKeyPath = Path.Combine(directory, "key.pem"),
                ChainPath = Path.Combine(directory, "root.pem"),
                BaseDN = leaf.Issuer,
                Algorithms = ["SHA256withRSA"],
                DefaultSignatureMethod = "SHA256withRSA",
                VerificationWindowMinutes = 100
            });
        }

        public void Dispose() => Directory.Delete(directory, true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class CapturingLogger<T>(List<string> messages) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            messages.Add(exception is null ? text : text + " " + exception.Message);
        }
    }
}
