using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Models.DTOs;

namespace SIPS.Core.Tests.Helpers;

public class SipsRequestSender_Tests
{
    [Fact]
    public async Task SendAsync_Calls_Http_Send4XML()
    {
        var logger = new Mock<ILogger<SipsRequestSender>>();
        var http = new Mock<IInterfaceHttpClient>();
        http.Setup(h => h.Send4XML(It.IsAny<string>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response<string>.Success("ok"));

        var sender = new SipsRequestSender(logger.Object, http.Object);
        var rsp = await sender.SendAsync("http://sips", "<x/>", CancellationToken.None, "cid");

        Assert.True(rsp.IsSuccess);
        http.Verify(h => h.Send4XML(It.IsAny<string>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_Propagates_Failure()
    {
        var logger = new Mock<ILogger<SipsRequestSender>>();
        var http = new Mock<IInterfaceHttpClient>();
        http.Setup(h => h.Send4XML(It.IsAny<string>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response<string>.Fail("bad", System.Net.HttpStatusCode.BadGateway));

        var sender = new SipsRequestSender(logger.Object, http.Object);
        var rsp = await sender.SendAsync("http://sips", "<x/>", CancellationToken.None, "cid");

        Assert.False(rsp.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadGateway, rsp.StatusCode);
        http.Verify(h => h.Send4XML(It.IsAny<string>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
