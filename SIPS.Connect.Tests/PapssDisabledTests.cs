using Moq;
using SIPS.Adapter.Models;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.XMLDsig.Xades.Interfaces;
using Xunit;
using SIPS.XMLDsig.Xades.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIPS.XMLDsig.Xades.Options;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SIPS.Connect.Controllers;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Helpers;
using SIPS.XMLDsig.Xades.Services;

namespace SIPS.Connect.Tests;

public sealed class PapssDisabledTests
{
    [Fact]
    public async Task Disabled_callback_guard_does_not_touch_trust_verifier()
    {
        var verifier = new Mock<INativeVerifier>(MockBehavior.Strict);
        var guard = new PapssCallbackGuard(new() { Enabled = false }, new JsonAdapterOptions(), verifier.Object);
        Assert.Null(await guard.ValidateAsync("not-even-xml", CancellationToken.None));
        verifier.VerifyNoOtherCalls();
    }

    [Fact]
    public void Disabled_production_di_has_no_papss_configuration_or_wp_sips_profile_requirement()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PapssFacing:Enabled"]="false", ["Xades:WithoutPKI"]="true", ["Xades:DefaultSignatureMethod"]="LEGACY",
            ["Xades:Algorithms:0"]="LEGACY", ["Xades:VerificationWindowMinutes"]="1", ["Core:SAFExpression"]="*/5 * * * *",
            ["Core:SAFTimeZoneInfo"]="Utc", ["Core:TimeoutWorkerSchedule"]="*/15 * * * *", ["Jwt:Authority"]="https://identity.test",
            ["Jwt:Audience"]="sips", ["Keycloak:Realm:Protocol"]="https", ["Keycloak:Realm:Host"]="identity.test", ["Keycloak:Realm:Name"]="sips"
        }).Build();
        var services = new ServiceCollection(); services.AddLogging();
        SIPS.Connect.Config.DI.Register(services, config);
        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<PapssFacingOptions>().Enabled);
        Assert.True(provider.GetRequiredService<XadesOptions>().WithoutPKI);
        Assert.Equal(DownstreamRail.Sips, provider.GetRequiredService<IParticipantOperationRouter>().Select(ParticipantOperation.Payment, null));
    }
}

public sealed class PapssCallbackGuardTests
{
    [Fact]
    public async Task Signed_callback_resolves_bah_destination_to_participant_profile()
    {
        var options = new PapssFacingOptions { Enabled=true, RemoteWpSipsIdentity="PAPSS", SecurityProfile="SPS.PAPSS.FINANCIAL.001", Participants=new(StringComparer.OrdinalIgnoreCase)
        { ["bank-a"] = new() { Enabled=true, Bic="BANKSOSIXXX", CallbackMappingProfile="bank-a", CallbackUrl="https://bank.test/callback" } } };
        var mappings = new JsonAdapterOptions { Endpoints = new() { ["bank-a.CB_PaymentRequest"] = new() } };
        var verifier = new Mock<INativeVerifier>();
        verifier.Setup(x => x.VerifyWithProvenance(It.IsAny<string>(), XadesProfile.WpSipsPapss, It.IsAny<CancellationToken>())).ReturnsAsync(new SignatureVerificationResult(true, new VerboseResult(),
            new("PAPSS","CA","test","PAPSS","issuer","1","hash",true,"v1","pacs.008.001.10",options.SecurityProfile,"hash",DateTimeOffset.UtcNow)));
        var xml = $"<FPEnvelope xmlns:h='urn:iso:std:iso:20022:tech:xsd:head.001.001.03'><h:AppHdr><h:Fr><h:FIId><h:FinInstnId><h:Othr><h:Id>PAPSS</h:Id></h:Othr></h:FinInstnId></h:FIId></h:Fr><h:To><h:FIId><h:FinInstnId><h:Othr><h:Id>BANKSOSIXXX</h:Id></h:Othr></h:FinInstnId></h:FIId></h:To><h:BizMsgIdr>M1</h:BizMsgIdr><h:MsgDefIdr>pacs.008.001.10</h:MsgDefIdr><h:BizSvc>{options.SecurityProfile}</h:BizSvc><h:CreDt>2026-01-01T00:00:00Z</h:CreDt></h:AppHdr></FPEnvelope>";
        var route = await new PapssCallbackGuard(options, mappings, verifier.Object).ValidateAsync(xml, CancellationToken.None);
        Assert.Equal("bank-a", route?.Principal);
        Assert.Equal("bank-a", route?.CallbackMappingProfile);
    }

    [Fact]
    public async Task Financial_callback_without_original_references_is_rejected_before_dispatch()
    {
        var options = new PapssFacingOptions { Enabled=true, RemoteWpSipsIdentity="PAPSS", SecurityProfile="SPS.PAPSS.FINANCIAL.001" };
        var xml = $"<FPEnvelope xmlns:h='urn:iso:std:iso:20022:tech:xsd:head.001.001.03'><h:AppHdr><h:Fr><h:FIId><h:FinInstnId><h:Othr><h:Id>PAPSS</h:Id></h:Othr></h:FinInstnId></h:FIId></h:Fr><h:To><h:FIId><h:FinInstnId><h:Othr><h:Id>BANKSOSIXXX</h:Id></h:Othr></h:FinInstnId></h:FIId></h:To><h:BizMsgIdr>M1</h:BizMsgIdr><h:MsgDefIdr>pacs.002.001.12</h:MsgDefIdr><h:BizSvc>{options.SecurityProfile}</h:BizSvc><h:CreDt>2026-01-01T00:00:00Z</h:CreDt></h:AppHdr></FPEnvelope>";
        await Assert.ThrowsAsync<InvalidDataException>(() => new PapssCallbackGuard(options, new(), Mock.Of<INativeVerifier>()).ValidateAsync(xml, CancellationToken.None));
    }

    [Fact]
    public async Task Signed_acmt024_verification_outcome_resolves_exact_PAPSS_participant()
    {
        using var pki = new CallbackPki();
        var options = Options();
        var original = new PayeeVerificationBuilder.Request { From="BANKSOSIXXX", To="ZKBASOS0", BizMsgIdr="VERIFY-BIZ-1", MsgDefIdr="acmt.023.001.03", MsgId="VERIFY-MSG-1", SIPSRequestId="VERIFY-REQ-1", CreDt=DateTime.UtcNow, Alias="ACC-1", Type="BBAN" };
        var unsigned = PayeeVerificationResponseBuilder.Build(new() { From="PAPSS", To="BANKSOSIXXX", Original=original, Verified=true, VerificationId="RESULT-1", Id="ACC-1", Type="BBAN", Name="Account Holder", Currency="SOS" });
        var document = XDocument.Parse(unsigned);
        var header = document.Descendants().Single(x => x.Name.LocalName == "AppHdr");
        header.Elements().First(x => x.Name.LocalName == "MsgDefIdr").AddAfterSelf(new XElement(header.Name.Namespace + "BizSvc", options.SecurityProfile));
        document.Descendants().First(x => x.Name.LocalName == "Assgne").Descendants().First(x => x.Name.LocalName == "Id").Value = "PAPSS";
        var signed = pki.Signer.SignEnvelope(document.ToString(SaveOptions.DisableFormatting), XadesProfile.WpSipsPapss);
        var verified = await pki.Verifier.VerifyWithProvenance(signed, XadesProfile.WpSipsPapss, CancellationToken.None);
        Assert.True(verified.Result, $"certificate={verified.Verbose.CertificateStatus}; signature={verified.Verbose.SignatureStatus}; references={verified.Verbose.ReferencesStatus}; ownership={verified.Verbose.OwnershSIPStatus}");

        var route = await new PapssCallbackGuard(options, Mappings(), pki.Verifier).ValidateAsync(signed, CancellationToken.None);

        Assert.Equal("bank-a", route?.Principal);
        Assert.Equal("BANKSOSIXXX", route?.Bic);
    }

    [Fact]
    public async Task PAPSS_identity_with_missing_or_wrong_profile_fails_closed_before_legacy_dispatch()
    {
        foreach (var service in new string?[] { null, "WRONG.PROFILE" })
        {
            var incoming = new Mock<IIncoming>(MockBehavior.Strict);
            var verifier = new Mock<INativeVerifier>(MockBehavior.Strict);
            var guard = new PapssCallbackGuard(Options(), Mappings(), verifier.Object);
            var controller = new IncomingController(incoming.Object, guard, new ParticipantCallbackContext(), NullLogger<IncomingController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            var bizSvc = service is null ? string.Empty : $"<h:BizSvc>{service}</h:BizSvc>";
            var xml = $"<FPEnvelope xmlns:h='urn:iso:std:iso:20022:tech:xsd:head.001.001.03'><h:AppHdr><h:Fr><h:FIId><h:FinInstnId><h:Othr><h:Id>PAPSS</h:Id></h:Othr></h:FinInstnId></h:FIId></h:Fr><h:To><h:FIId><h:FinInstnId><h:Othr><h:Id>BANKSOSIXXX</h:Id></h:Othr></h:FinInstnId></h:FIId></h:To><h:BizMsgIdr>M1</h:BizMsgIdr><h:MsgDefIdr>acmt.024.001.03</h:MsgDefIdr>{bizSvc}<h:CreDt>2026-01-01T00:00:00Z</h:CreDt></h:AppHdr></FPEnvelope>";
            controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));

            var result = await controller.Post(CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            incoming.VerifyNoOtherCalls();
            verifier.VerifyNoOtherCalls();
        }
    }

    private static PapssFacingOptions Options() => new() { Enabled=true, RemoteWpSipsIdentity="PAPSS", SecurityProfile="SPS.PAPSS.FINANCIAL.001", Participants=new(StringComparer.OrdinalIgnoreCase) { ["bank-a"] = new() { Enabled=true, Bic="BANKSOSIXXX", CallbackMappingProfile="bank-a", CallbackUrl="https://bank.test/callback" } } };
    private static JsonAdapterOptions Mappings() => new() { Endpoints = new() { ["bank-a.CB_VerificationResponse"] = new() } };

    private sealed class CallbackPki : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "papss-callback-pki-" + Guid.NewGuid().ToString("N"));
        public NativeSigner Signer { get; }
        public NativeVerifier Verifier { get; }

        public CallbackPki()
        {
            Directory.CreateDirectory(directory);
            using var rootKey=RSA.Create(2048);var rootRequest=new CertificateRequest("CN=PAPSS Test Root",rootKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign|X509KeyUsageFlags.CrlSign,true));using var root=rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(30));
            using var leafKey=RSA.Create(2048);var leafRequest=new CertificateRequest("CN=PAPSS",leafKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,true));leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.2")},true));using var unsigned=leafRequest.Create(root,DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(10),RandomNumberGenerator.GetBytes(16));using var leaf=unsigned.CopyWithPrivateKey(leafKey);
            var certPem=new string(PemEncoding.Write("CERTIFICATE",leaf.Export(X509ContentType.Cert)));var rootPem=new string(PemEncoding.Write("CERTIFICATE",root.Export(X509ContentType.Cert)));File.WriteAllText(Path.Combine(directory,"leaf.pem"),certPem);File.WriteAllText(Path.Combine(directory,"root.pem"),rootPem);File.WriteAllText(Path.Combine(directory,"key.pem"),leafKey.ExportPkcs8PrivateKeyPem());
            var xades=new XadesOptions{CertificatePath=Path.Combine(directory,"leaf.pem"),ChainPath=Path.Combine(directory,"root.pem"),PrivateKeyPath=Path.Combine(directory,"key.pem"),BaseDN=leaf.Issuer,Algorithms=["SHA256withRSA"],DefaultSignatureMethod="SHA256withRSA",VerificationWindowMinutes=100};var certificates=new CertificateService(xades);Signer=new(xades,NullLogger<NativeSigner>.Instance,certificates);var record=new CertificateDownloadResponse(certPem,"PAPSS",false,"SPS","UAT","PAPSS",Convert.ToHexString(SHA256.HashData(leaf.Export(X509ContentType.Cert))).ToLowerInvariant(),"SPS.XADES.BES.001@1.0.0","1.3.6.1.5.5.7.3.2");Verifier=new(xades,NullLogger<NativeVerifier>.Instance,certificates,new Download(record));
        }

        public void Dispose() => Directory.Delete(directory,true);
        private sealed class Download(CertificateDownloadResponse value):ICertificateDownloadService{public Task<(CertificateDownloadResponse? Certificates,string? Error)> GetCertificatesAsync(string serialNumber,string issuerDN,CancellationToken cancellationToken=default)=>Task.FromResult<(CertificateDownloadResponse?,string?)>((value,null));}
    }
}
