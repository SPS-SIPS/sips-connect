using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Core.Services.Verification;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class SignatureServiceTests
{
    [Fact]
    public async Task VerifyAsync_ReturnsFalse_AndLogs_WhenVerifierFails()
    {
        var verifier = new Mock<INativeVerifier>();
        verifier.Setup(v => v.VerifySignature(
                    It.IsAny<string>(),
                    false,
                    XadesProfile.IpsVendorLegacy,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((false, new VerboseResult { SignatureStatus = "bad" }));
        var logger = Mock.Of<ILogger<SignatureService>>();
        var sut = new SignatureService(verifier.Object, logger);

        var (ok, verbose) = await sut.VerifyAsync("payload", CancellationToken.None);

        ok.Should().BeFalse();
        verbose.Should().Be("bad");
        verifier.Verify(v => v.VerifySignature(
            "payload",
            false,
            XadesProfile.IpsVendorLegacy,
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsTrue_WhenVerifierPasses()
    {
        var verifier = new Mock<INativeVerifier>();
        verifier.Setup(v => v.VerifySignature(
                    It.IsAny<string>(),
                    false,
                    XadesProfile.IpsVendorLegacy,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, new VerboseResult { SignatureStatus = "ok" }));
        var logger = Mock.Of<ILogger<SignatureService>>();
        var sut = new SignatureService(verifier.Object, logger);

        var (ok, verbose) = await sut.VerifyAsync("payload", CancellationToken.None);

        ok.Should().BeTrue();
        verbose.Should().Be("ok");
    }
}
