using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Verification;

namespace SIPS.Core.Tests.Helpers;

public class InboundMessageService_Tests
{
    [Fact]
    public async Task VerifyAndParseAsync_Fails_On_Bad_Signature()
    {
        var sig = new Mock<ISignatureService>();
        sig.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((false, "bad"));
        var svc = new InboundMessageService(sig.Object);

        var (ok, req) = await svc.VerifyAndParseAsync("<x/>", xml => (true, new object()), CancellationToken.None, "cid");
        Assert.False(ok);
        Assert.Null(req);
    }

    [Fact]
    public async Task VerifyAndParseAsync_Succeeds_On_Good_Signature_And_Parse()
    {
        var sig = new Mock<ISignatureService>();
        sig.Setup(s => s.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((true, "ok"));
        var svc = new InboundMessageService(sig.Object);

        var (ok, req) = await svc.VerifyAndParseAsync("<x/>", xml => (true, 42), CancellationToken.None, "cid");
        Assert.True(ok);
        Assert.Equal(42, req);
    }
}
