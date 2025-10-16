using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Core.Interfaces;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.ISO20022.Models.DTOs;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class CallbackClientTests
{
    [Fact]
    public async Task SendAsync_Forwards_Request_And_Returns_Response()
    {
        var http = new Mock<IInterfaceHttpClient>();
        var headers = new Dictionary<string, string>{{"h1","v1"}};
        var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var rsp = new Response<JsonObject?>(new JsonObject{{"ok", true}}) { StatusCode = HttpStatusCode.OK };
        http.Setup(h => h.Send("https://cb", headers, content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rsp)
            .Verifiable();
        var logger = Mock.Of<ILogger<CallbackClient>>();
        var correlation = new CorrelationService();
        var sut = new CallbackClient(http.Object, logger, correlation);

        var outRsp = await sut.SendAsync("https://cb", headers, content, CancellationToken.None, "cid-1");

        outRsp.Should().BeSameAs(rsp);
        http.Verify();
    }
}
