using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.Core.Interfaces;
using SIPS.Core.Models;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.XMLDsig.Xades.Models;
using Xunit;
using CoreCertificateRequest = SIPS.Core.Models.CertificateRequest;
using CoreLoginResponse = SIPS.Core.Models.LoginResponse;
using X509CertificateRequest = System.Security.Cryptography.X509Certificates.CertificateRequest;

namespace SIPS.Connect.Tests;

public sealed class PapssResponderCertificateDownloadServiceTests
{
    [Fact]
    public async Task Legacy_record_is_enriched_only_when_owner_and_public_certificate_pin_match()
    {
        using var key = RSA.Create(2048);
        var request = new X509CertificateRequest("CN=papss", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pem = new string(PemEncoding.Write("CERTIFICATE", certificate.Export(X509ContentType.Cert)));
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.Export(X509ContentType.Cert))).ToLowerInvariant();
        var options = Options(fingerprint);
        var service = Service(new CertificateDownloadResponse(pem, "papss"), options);

        var (result, error) = await service.GetCertificatesAsync("1", "CN=issuer", applyPapssTrustBinding: true);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("SPS", result.Authority);
        Assert.Equal("UAT", result.Environment);
        Assert.Equal("PAPSS", result.RepresentedParticipant);
        Assert.Equal(fingerprint, result.CertificateSha256);
        Assert.Equal("SPS.XADES.BES.001@1.0.0", result.TrustProfileVersion);
        Assert.Equal("1.3.6.1.5.5.7.3.2", result.RequiredExtendedKeyUsageOid);
    }

    [Fact]
    public async Task Legacy_record_with_wrong_pin_fails_closed()
    {
        using var key = RSA.Create(2048);
        var request = new X509CertificateRequest("CN=papss", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pem = new string(PemEncoding.Write("CERTIFICATE", certificate.Export(X509ContentType.Cert)));
        var service = Service(new CertificateDownloadResponse(pem, "papss"), Options(new string('0', 64)));

        var (result, error) = await service.GetCertificatesAsync("1", "CN=issuer", applyPapssTrustBinding: true);

        Assert.Null(result);
        Assert.Contains("SHA-256 pin", error);
    }

    /// <summary>
    /// TVR UAT 2026-10-08: SVIP (the domestic switch) signs IPS/SmartVista-legacy traffic with its own certificate,
    /// whose owner is never "PAPSS" and which the legacy certificate repository never enriches with PAPSS provenance.
    /// Before this fix, every certificate lookup in the process ran through this one PAPSS-wrapping decorator
    /// unconditionally, so every SVIP-signed message was rejected with "The PAPSS responder certificate owner does
    /// not match the configured WP-SIPS identity" before NativeVerifier's own (profile-correct) certificate checks
    /// ever ran. A lookup that does not ask for the PAPSS trust binding must get the raw legacy record back
    /// untouched, regardless of its owner.
    /// </summary>
    [Fact]
    public async Task Domestic_IPS_lookup_is_returned_unrestricted_regardless_of_owner()
    {
        using var key = RSA.Create(2048);
        var request = new X509CertificateRequest("CN=svip", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pem = new string(PemEncoding.Write("CERTIFICATE", certificate.Export(X509ContentType.Cert)));
        var service = Service(new CertificateDownloadResponse(pem, "svip"), Options(new string('0', 64)));

        var (result, error) = await service.GetCertificatesAsync("1", "CN=issuer", applyPapssTrustBinding: false);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("svip", result.Owner);
        Assert.Null(result.Authority);
        Assert.Null(result.RepresentedParticipant);
    }

    private static PapssFacingOptions Options(string fingerprint) => new()
    {
        Enabled = true,
        Environment = "UAT",
        RemoteWpSipsIdentity = "PAPSS",
        ResponderTrust = new()
        {
            CertificateSha256 = fingerprint,
            Authority = "SPS",
            TrustProfileVersion = "SPS.XADES.BES.001@1.0.0",
            RequiredExtendedKeyUsageOid = "1.3.6.1.5.5.7.3.2"
        }
    };

    private static PapssResponderCertificateDownloadService Service(CertificateDownloadResponse record, PapssFacingOptions options)
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(x => x.LoginAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((new CoreLoginResponse("token", Guid.NewGuid(), 3600, 7200), null));
        var http = new Mock<IRepositoryHttpClient>();
        http.Setup(x => x.PostAsync<CoreCertificateRequest, CertificateDownloadResponse>(
                "https://repo.test/certificates", It.IsAny<CoreCertificateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RepositoryResponse<CertificateDownloadResponse>.Success(record, 60));
        var inner = new CertificateDownloadService(
            new CoreOptions { PublicKeysRepUrl = "https://repo.test/certificates" },
            NullLogger<CertificateDownloadService>.Instance,
            http.Object,
            auth.Object,
            new NoCache());
        return new(inner, options, NullLogger<PapssResponderCertificateDownloadService>.Instance);
    }

    private sealed class NoCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken token = default) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken token = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }
}
