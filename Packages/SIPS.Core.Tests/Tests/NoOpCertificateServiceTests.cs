using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Options;
using SIPS.XMLDsig.Xades.Services;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class NoOpCertificateServiceTests
{
    [Fact]
    public void ShouldResolveNoOpCertificateService_WhenWithoutPKI_IsTrue()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new XadesOptions { WithoutPKI = true };
        services.AddSingleton(options);
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        
        services.AddSingleton<ICertificateService>(sp =>
        {
            var opts = sp.GetRequiredService<XadesOptions>();
            return opts.WithoutPKI
                ? new NoOpCertificateService(sp.GetRequiredService<ILogger<NoOpCertificateService>>())
                : new CertificateService(opts);
        });

        // Act
        var provider = services.BuildServiceProvider();
        var certificateService = provider.GetService<ICertificateService>();

        // Assert
        Assert.NotNull(certificateService);
        Assert.IsType<NoOpCertificateService>(certificateService);
    }

    [Fact]
    public void NativeSigner_ShouldFunction_WhenWithoutPKI_IsTrue_AndFilesMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new XadesOptions 
        { 
            WithoutPKI = true,
            PrivateKeyPath = "non-existent.key",
            CertificatePath = "non-existent.crt",
            ChainPath = "non-existent.crt"
        };
        services.AddSingleton(options);
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        
        services.AddSingleton<ICertificateService>(sp =>
        {
            var opts = sp.GetRequiredService<XadesOptions>();
            return opts.WithoutPKI
                ? new NoOpCertificateService(sp.GetRequiredService<ILogger<NoOpCertificateService>>())
                : new CertificateService(opts);
        });
        services.AddSingleton<INativeSigner, NativeSigner>();

        var provider = services.BuildServiceProvider();
        var signer = provider.GetRequiredService<INativeSigner>();
        var inputMessage = "<Message>Hello</Message>";

        // Act
        Assert.Throws<InvalidOperationException>(() => signer.SignEnvelope(inputMessage));
    }

    [Fact]
    public async Task NativeVerifier_ShouldFunction_WhenWithoutPKI_IsTrue_AndFilesMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new XadesOptions 
        { 
            WithoutPKI = true,
            PrivateKeyPath = "non-existent.key",
            CertificatePath = "non-existent.crt",
            ChainPath = "non-existent.crt"
        };
        services.AddSingleton(options);
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        
        services.AddSingleton<ICertificateService>(sp =>
        {
            var opts = sp.GetRequiredService<XadesOptions>();
            return opts.WithoutPKI
                ? new NoOpCertificateService(sp.GetRequiredService<ILogger<NoOpCertificateService>>())
                : new CertificateService(opts);
        });
        services.AddSingleton<ICertificateDownloadService, MockCertificateDownloadService>();
        services.AddSingleton<INativeVerifier, NativeVerifier>();

        var provider = services.BuildServiceProvider();
        var verifier = provider.GetRequiredService<INativeVerifier>();
        var inputMessage = "<Message>Hello</Message>";

        // Act
        var (result, verbose) = await verifier.VerifySignature(inputMessage, false, default);

        // Assert
        Assert.False(result);
        Assert.Equal("Not Verified", verbose.SignatureStatus);
    }

    private class MockCertificateDownloadService : ICertificateDownloadService
    {
        public Task<(SIPS.XMLDsig.Xades.Models.CertificateDownloadResponse? Certificates, string? Error)> GetCertificatesAsync(string serialNumber, string issuerName, System.Threading.CancellationToken ct = default)
            => Task.FromResult<(SIPS.XMLDsig.Xades.Models.CertificateDownloadResponse?, string?)>((null, "Not implemented in mock"));
    }

    [Fact]
    public void NoOpCertificateService_ShouldThrow_OnPKIDependentMembers()
    {
        // Arrange
        var logger = NullLogger<NoOpCertificateService>.Instance;
        var service = new NoOpCertificateService(logger);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => service.AsymmetricKey);
        Assert.Throws<InvalidOperationException>(() => service.Certificate);
        Assert.Throws<InvalidOperationException>(() => service.GetCertificatePem());
        Assert.Throws<InvalidOperationException>(() => service.GetSignatureElement("a", "b", "business-layer", "c", "d"));
    }
}
