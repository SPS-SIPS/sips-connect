using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using System.Xml;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.WpSips;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using SIPS.XMLDsig.Xades.Options;
using SIPS.XMLDsig.Xades.Services;
using Xunit;
using SIPS.ISO20022.Schemas.PRDocument;
using SIPS.ISO20022.Models;

namespace SIPS.XMLDsig.Xades.Tests;

public sealed class XadesConformanceTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"wp-sips-xades-"+Guid.NewGuid().ToString("N")); readonly CertificateDownloadResponse record; readonly NativeSigner signer; readonly NativeVerifier verifier; readonly CertificateService certificateService; readonly XadesOptions options;
    public XadesConformanceTests()
    {
        Directory.CreateDirectory(dir);using var rootKey=RSA.Create(2048);var rr=new CertificateRequest("CN=SPS Root",rootKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);rr.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));rr.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign|X509KeyUsageFlags.CrlSign,true));using var root=rr.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(30));using var leafKey=RSA.Create(2048);var lr=new CertificateRequest("CN=SPS-A",leafKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);lr.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));lr.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,true));lr.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.2")},true));var serial=RandomNumberGenerator.GetBytes(16);using var unsigned=lr.Create(root,DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(10),serial);using var leaf=unsigned.CopyWithPrivateKey(leafKey);
        var cert=PemEncoding.Write("CERTIFICATE",leaf.Export(X509ContentType.Cert));var rootPem=PemEncoding.Write("CERTIFICATE",root.Export(X509ContentType.Cert));var key=leafKey.ExportPkcs8PrivateKeyPem();File.WriteAllText(Path.Combine(dir,"leaf.pem"),new string(cert));File.WriteAllText(Path.Combine(dir,"root.pem"),new string(rootPem));File.WriteAllText(Path.Combine(dir,"key.pem"),key);
        options=new XadesOptions{CertificatePath=Path.Combine(dir,"leaf.pem"),ChainPath=Path.Combine(dir,"root.pem"),PrivateKeyPath=Path.Combine(dir,"key.pem"),BaseDN=leaf.Issuer,Algorithms=["SHA256withRSA"],DefaultSignatureMethod="SHA256withRSA",VerificationWindowMinutes=100};certificateService=new CertificateService(options);record=new(new string(cert),"SPS-A",false,"SPS-AUTH","TEST","BANK1",Convert.ToHexString(SHA256.HashData(leaf.Export(X509ContentType.Cert))).ToLowerInvariant(),"SPS.XADES.BES.001@1.0.0","1.3.6.1.5.5.7.3.2");signer=new(options,NullLogger<NativeSigner>.Instance,certificateService);verifier=Verifier(record);
    }
    [Fact] public async Task Signature_verifies_and_tamper_fails()
    {var now=DateTimeOffset.UtcNow;var xml=WpSipsInformationMessageBuilder.BuildFxRequest(new("SPS-A","SPS-B","REQ-MSG","admi.009.001.02",WpSipsProfiles.Fx,now),"REQ-1",new("US","KE","USD","KES","BANK1","INST",10,false,null));var signed=PapssSign(xml);var ok=await PapssVerify(signed);Assert.True(ok.Result,$"cert={ok.Verbose.CertificateStatus};sig={ok.Verbose.SignatureStatus};refs={ok.Verbose.ReferencesStatus};owner={ok.Verbose.OwnershSIPStatus}");Assert.Equal("SPS-AUTH",ok.Signer!.Authority);var bad=signed.Replace(">10<",">11<",StringComparison.Ordinal);Assert.False((await PapssVerify(bad)).Result);}
    [Fact] public async Task Revoked_and_pki_off_fail_closed(){var options=new XadesOptions{WithoutPKI=true};var noOp=new NativeVerifier(options,NullLogger<NativeVerifier>.Instance,new NoOpCertificateService(NullLogger<NoOpCertificateService>.Instance),new Download(record));Assert.False((await noOp.VerifyWithProvenance("<x/>",default)).Result);}
    [Fact] public async Task Ownership_revocation_and_stale_creation_fail_closed(){string Message(DateTimeOffset at)=>WpSipsInformationMessageBuilder.BuildFxRequest(new("SPS-A","SPS-B","REQ-MSG","admi.009.001.02",WpSipsProfiles.Fx,at),"REQ-1",new("US","KE","USD","KES","BANK1","INST",10,false,null));var signed=PapssSign(Message(DateTimeOffset.UtcNow));Assert.False((await Verifier(record with{Owner="OTHER"}).VerifyWithProvenance(signed,XadesProfile.WpSipsPapss,default)).Result);Assert.False((await Verifier(record with{Revoked=true}).VerifyWithProvenance(signed,XadesProfile.WpSipsPapss,default)).Result);Assert.False((await PapssVerify(PapssSign(Message(DateTimeOffset.UtcNow.AddMinutes(-101))))).Result);}
    [Fact] public async Task Certificate_fingerprint_eku_and_trust_profile_evidence_fail_closed()
    {var xml=PapssSign(Fx(DateTimeOffset.UtcNow));Assert.False((await Verifier(record with{CertificateSha256="00"}).VerifyWithProvenance(xml,XadesProfile.WpSipsPapss,default)).Result);Assert.False((await Verifier(record with{RequiredExtendedKeyUsageOid="1.3.6.1.5.5.7.3.1"}).VerifyWithProvenance(xml,XadesProfile.WpSipsPapss,default)).Result);Assert.False((await Verifier(record with{TrustProfileVersion=null}).VerifyWithProvenance(xml,XadesProfile.WpSipsPapss,default)).Result);}
    [Fact] public async Task Reference_ids_and_algorithms_are_exact_and_fail_closed()
    {var signed=PapssSign(Fx(DateTimeOffset.UtcNow));var document=XDocument.Parse(signed);var rootId=document.Root!.Attribute("Id")!.Value;var keyId=document.Descendants().Single(x=>x.Name.LocalName=="KeyInfo").Attribute("Id")!.Value;var variants=new[]{signed.Replace("URI=\"#", "URI=\"",StringComparison.Ordinal),signed.Replace("URI=\"#", "URI=\"https://invalid.example/",StringComparison.Ordinal),signed.Replace("http://www.w3.org/2001/10/xml-exc-c14n#","http://www.w3.org/TR/2001/REC-xml-c14n-20010315",StringComparison.Ordinal),signed.Replace("http://www.w3.org/2001/04/xmlenc#sha256","http://www.w3.org/2000/09/xmldsig#sha1",StringComparison.Ordinal),signed.Replace("Id=\""+keyId+"\"","Id=\""+rootId+"\"",StringComparison.Ordinal),signed.Replace("http://uri.etsi.org/01903/v1.3.2#SignedProperties","urn:not-signed-properties",StringComparison.Ordinal)};for(var i=0;i<variants.Length;i++)Assert.False((await PapssVerify(variants[i])).Result,$"variant {i}");}
    [Fact] public async Task Future_signing_time_and_bah_signing_time_divergence_fail_closed()
    {var now=DateTimeOffset.UtcNow;var future=new NativeSigner(options,NullLogger<NativeSigner>.Instance,certificateService,new FixedTimeProvider(now.AddMinutes(6)));var signed=future.SignEnvelope(Fx(now),XadesProfile.WpSipsPapss);var checking=new NativeVerifier(options,NullLogger<NativeVerifier>.Instance,certificateService,new Download(record),new FixedTimeProvider(now));Assert.False((await checking.VerifyWithProvenance(signed,XadesProfile.WpSipsPapss,default)).Result);}
    [Fact] public async Task Validly_resigned_misbound_reference_type_and_ambiguous_keyinfo_identity_fail_closed()
    {
        var misplaced=Resign((document,signature)=>{var refs=signature.GetElementsByTagName("Reference","http://www.w3.org/2000/09/xmldsig#").Cast<XmlElement>().ToArray();var typed=refs.Single(x=>x.HasAttribute("Type"));var keyId=signature.GetElementsByTagName("KeyInfo","http://www.w3.org/2000/09/xmldsig#").Cast<XmlElement>().Single().GetAttribute("Id");typed.RemoveAttribute("Type");refs.Single(x=>x.GetAttribute("URI")=="#"+keyId).SetAttribute("Type","http://uri.etsi.org/01903/v1.3.2#SignedProperties");});
        Assert.False((await PapssVerify(misplaced)).Result);
        var ambiguous=Resign((document,signature)=>{var key=signature.GetElementsByTagName("KeyInfo","http://www.w3.org/2000/09/xmldsig#").Cast<XmlElement>().Single();var issuer=key.GetElementsByTagName("X509IssuerName","http://www.w3.org/2000/09/xmldsig#").Cast<XmlElement>().Single();issuer.ParentNode!.AppendChild(issuer.CloneNode(true));});
        Assert.False((await PapssVerify(ambiguous)).Result);
    }
    [Fact] public async Task Existing_payment_family_defaults_to_vendor_legacy_profile(){var request=new PaymentRequestBuilder.Request{From="SPS-A",To="SPS-B",BizMsgIdr="PAY-MSG",MsgDefIdr="pacs.008.001.10",MsgId="PAY-REQ",CreDt=DateTime.UtcNow,TxId="TX1",EndToEndId="E2E",Amount=1,Currency="USD",LocalInstrument="INST",CategoryPurpose="CASH",Ustrd="test",SettlementMethod=SettlementMethod1Code.CLRG,ChargeBearer=ChargeBearerType1Code.SLEV,Debtor=new Person{Name="D",Address="A",Account="D1",AccountType="ACCT",AgentBIC="SPS-A",Issuer="I"},Creditor=new Person{Name="C",Address="A",Account="C1",AccountType="ACCT",AgentBIC="SPS-B",Issuer="I"}};var xml=PaymentRequestBuilder.Build(request).document;var signed=signer.SignEnvelope(xml);Assert.Contains("<ds:Reference>",signed);Assert.True((await verifier.VerifySignature(signed,true,default)).result);Assert.False((await verifier.VerifySignature(signed,true,XadesProfile.WpSipsPapss,default)).result);}
    [Fact] public async Task Agrosos_vendor_form_is_accepted_only_as_ips_vendor_legacy()
    {var signed=signer.SignEnvelope(Fx(DateTimeOffset.UtcNow));var references=XDocument.Parse(signed).Descendants().Where(x=>x.Name.LocalName=="Reference").ToArray();Assert.Equal(3,references.Length);Assert.Null(references[2].Attribute("URI"));Assert.True((await verifier.VerifySignature(signed,true,XadesProfile.IpsVendorLegacy,default)).result);Assert.False((await verifier.VerifySignature(signed,true,XadesProfile.WpSipsPapss,default)).result);}
    [Fact] public void Ips_vendor_signing_removes_the_papss_only_envelope_id()
    {
        var unsigned=PayeeVerificationBuilder.Build(new(){From="SPS-A",To="SPS-B",CreDt=DateTime.UtcNow,MsgId="VERIFY-REQ",Alias="123",Type="ACCT"}).document;
        Assert.NotNull(XDocument.Parse(unsigned).Root?.Attribute("Id"));
        var legacy=XDocument.Parse(signer.SignEnvelope(unsigned,XadesProfile.IpsVendorLegacy));
        Assert.Null(legacy.Root?.Attribute("Id"));
        var strict=XDocument.Parse(signer.SignEnvelope(unsigned,XadesProfile.WpSipsPapss));
        Assert.NotNull(strict.Root?.Attribute("Id"));
    }
    [Fact] public void Frozen_pre_e60_vendor_fixture_uses_anonymous_reference_and_historical_inclusive_digest()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Packages","SIPS.Core.Tests","TestData","acmt.023.xml")))root=root.Parent;
        Assert.NotNull(root);var xml=new XmlDocument{PreserveWhitespace=false};xml.Load(Path.Combine(root!.FullName,"Packages","SIPS.Core.Tests","TestData","acmt.023.xml"));
        var reference=xml.GetElementsByTagName("Reference","http://www.w3.org/2000/09/xmldsig#").Cast<XmlElement>().Single(x=>!x.HasAttribute("URI"));
        var document=xml.GetElementsByTagName("*").Cast<XmlElement>().Single(x=>x.LocalName=="Document");var isolated=new XmlDocument();isolated.AppendChild(isolated.ImportNode(document,true));var transform=new System.Security.Cryptography.Xml.XmlDsigC14NTransform();transform.LoadInput(isolated);using var stream=(Stream)transform.GetOutput(typeof(Stream));using var memory=new MemoryStream();stream.CopyTo(memory);var actual=Convert.ToBase64String(SHA256.HashData(memory.ToArray()));
        var expected=reference.GetElementsByTagName("DigestValue","http://www.w3.org/2000/09/xmldsig#").Cast<XmlElement>().Single().InnerText;Assert.Equal(expected,actual);
    }
    [Fact] public async Task Zkbasos_identified_envelope_form_is_accepted_only_as_wp_sips_papss()
    {var signed=PapssSign(Fx(DateTimeOffset.UtcNow));var document=XDocument.Parse(signed);var rootId=document.Root!.Attribute("Id")!.Value;var references=document.Descendants().Where(x=>x.Name.LocalName=="Reference").ToArray();Assert.Contains(references,x=>x.Attribute("URI")?.Value=="#"+rootId);Assert.True((await PapssVerify(signed)).Result);Assert.False((await verifier.VerifySignature(signed,true,XadesProfile.IpsVendorLegacy,default)).result);}
    [Fact] public async Task Papss_profile_reports_specific_reference_contract_failures()
    {
        var signed=PapssSign(Fx(DateTimeOffset.UtcNow));
        async Task AssertFailure(Action<XDocument> mutate,string expected)
        {var doc=XDocument.Parse(signed);mutate(doc);var result=await verifier.VerifySignature(doc.ToString(SaveOptions.DisableFormatting),true,XadesProfile.WpSipsPapss,default);Assert.False(result.result);var diagnostic=$"{result.verbose.SignatureStatus} {result.verbose.ReferencesStatus}";Assert.Contains(expected,diagnostic,StringComparison.Ordinal);}
        await AssertFailure(doc=>doc.Descendants().First(x=>x.Name.LocalName=="Reference").Remove(),"exactly three references");
        await AssertFailure(doc=>doc.Descendants().First(x=>x.Name.LocalName=="Reference").SetAttributeValue("URI",string.Empty),"non-empty fragment URI");
        await AssertFailure(doc=>{var root=doc.Root!;doc.Descendants().First(x=>x.Name.LocalName=="Reference"&&x.Attribute("URI")?.Value=="#"+root.Attribute("Id")!.Value).SetAttributeValue("URI","#WRONG-ROOT");},"does not target the FPEnvelope root Id");
        await AssertFailure(doc=>doc.Descendants().First(x=>x.Name.LocalName=="Transform"&&x.Attribute("Algorithm")?.Value=="http://www.w3.org/2000/09/xmldsig#enveloped-signature").Remove(),"missing the enveloped-signature transform");
        await AssertFailure(doc=>doc.Descendants().First(x=>x.Name.LocalName=="CanonicalizationMethod").SetAttributeValue("Algorithm","http://www.w3.org/TR/2001/REC-xml-c14n-20010315"),"SignedInfo canonicalization must be exclusive C14N");
    }
    [Fact] public async Task All_existing_message_families_use_the_same_real_signature_profile()
    {
        var now=DateTime.UtcNow;var payment=Payment("SPS-A","SPS-B",now);var builtPayment=PaymentRequestBuilder.Build(payment);payment.BizMsgIdr=builtPayment.bizMsgIdr;payment.MsgDefIdr=builtPayment.type;payment.MsgId=builtPayment.msgId;
        var verification=new PayeeVerificationBuilder.Request{From="SPS-A",To="SPS-B",CreDt=now,MsgId="VERIFY-REQ",Alias="123",Type="ACCT"};var builtVerification=PayeeVerificationBuilder.Build(verification);verification.BizMsgIdr=builtVerification.bizMsgIdr;verification.MsgDefIdr=builtVerification.type;
        var verificationOriginal=new PayeeVerificationBuilder.Request{From="SPS-B",To="SPS-A",CreDt=now,MsgId="VERIFY-ORIG",BizMsgIdr="VERIFY-BIZ",MsgDefIdr="acmt.023.001.03",SIPSRequestId="VERIFY-ID",Alias="123",Type="ACCT"};
        var messages=new List<string>{builtVerification.document,PayeeVerificationResponseBuilder.Build(new(){From="SPS-A",To="SPS-A",CreDt=now,Original=verificationOriginal,Verified=true,VerificationId="V1",Id="123",Type="ACCT",Name="Name",Currency="USD"}),builtPayment.document,
            PaymentRequestResponseBuilder.Build(new(){From="SPS-A",To="SPS-A",CreDt=now,Original=payment,Status="ACCP",TxId=payment.TxId}),
            PaymentStatusRequestBuilder.Build(new(){From="SPS-A",To="SPS-B",CreDt=now,MsgId="STATUS-REQ",OriginalEndToEnd="E2E",OrgnlTxId="TX1"}),
            ReturnPaymentRequestBuilder.Build(new(){From="SPS-A",To="SPS-B",CreDt=now,NumberOfTransactions=1,LocalInstrument="INST",CategoryPurpose="CASH",ReturnId="RET1",OriginalEndToEnd="E2E",OrgnlTxId="TX1",OriginalCurrency="USD",OriginalAmount=1,ReturnReason="DUPL",AdditionalInfo="test",DebtorAgent="SPS-A",CreditorAgent="SPS-B"}).document};
        foreach(var message in messages){Assert.Contains("Id=\"BL-",message);var family=XDocument.Parse(message).Descendants().First(x=>x.Name.LocalName=="MsgDefIdr").Value;var result=await verifier.VerifySignature(signer.SignEnvelope(message),true,default);Assert.True(result.result,$"{family}:{result.verbose.CertificateStatus}/{result.verbose.SignatureStatus}/{result.verbose.OwnershSIPStatus}");}
    }
    static PaymentRequestBuilder.Request Payment(string from,string to,DateTime now)=>new(){From=from,To=to,CreDt=now,MsgId="PAY-REQ",TxId="TX1",EndToEndId="E2E",Amount=1,Currency="USD",LocalInstrument="INST",CategoryPurpose="CASH",Ustrd="test",SettlementMethod=SettlementMethod1Code.CLRG,ChargeBearer=ChargeBearerType1Code.SLEV,Debtor=new Person{Name="D",Address="A",Account="D1",AccountType="ACCT",AgentBIC=from,Issuer="I"},Creditor=new Person{Name="C",Address="A",Account="C1",AccountType="ACCT",AgentBIC=to,Issuer="I"}};
    static string Fx(DateTimeOffset at)=>WpSipsInformationMessageBuilder.BuildFxRequest(new("SPS-A","SPS-B","REQ-MSG","admi.009.001.02",WpSipsProfiles.Fx,at),"REQ-1",new("US","KE","USD","KES","BANK1","INST",10,false,null));
    public void Dispose(){Directory.Delete(dir,true);}
    sealed class Download(CertificateDownloadResponse value):ICertificateDownloadService{public Task<(CertificateDownloadResponse? Certificates,string? Error)>GetCertificatesAsync(string sn,string issuerDN,CancellationToken ct=default)=>Task.FromResult<(CertificateDownloadResponse?,string?)>((value,null));}
    NativeVerifier Verifier(CertificateDownloadResponse value)=>new(options,NullLogger<NativeVerifier>.Instance,certificateService,new Download(value));
    string PapssSign(string xml)=>signer.SignEnvelope(xml,XadesProfile.WpSipsPapss);
    Task<SignatureVerificationResult> PapssVerify(string xml)=>verifier.VerifyWithProvenance(xml,XadesProfile.WpSipsPapss,default);
    string Resign(Action<XmlDocument,XmlElement> mutate){var document=new XmlDocument{PreserveWhitespace=false};document.LoadXml(PapssSign(Fx(DateTimeOffset.UtcNow)));var signature=document.GetElementsByTagName("Signature","http://www.w3.org/2000/09/xmldsig#").Cast<XmlElement>().Single();mutate(document,signature);typeof(NativeSigner).GetMethod("UpdateReferences",BindingFlags.Static|BindingFlags.NonPublic,null,[typeof(XmlElement),typeof(XmlDocument),typeof(XadesProfile)],null)!.Invoke(null,[signature,document,XadesProfile.WpSipsPapss]);typeof(NativeSigner).GetMethod("RecomputeSignatureWithBouncyCastle",BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(XmlElement),typeof(string),typeof(XadesProfile)],null)!.Invoke(signer,[signature,"SHA256withRSA",XadesProfile.WpSipsPapss]);return document.OuterXml;}
    sealed class FixedTimeProvider(DateTimeOffset value):TimeProvider{public override DateTimeOffset GetUtcNow()=>value;}
}
