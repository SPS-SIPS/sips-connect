using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Adapter;
using SIPS.ISO20022.Models.DTOs;

namespace SIPS.Core.Tests.Helpers;

public class CallbackOrchestrator_Tests
{
    [Fact]
    public async Task SendJsonAsync_Sends_With_Headers_And_Transforms_DTO()
    {
        var jsonAdapter = new Mock<IJsonAdapter>();
        jsonAdapter.Setup(j => j.Transform(It.IsAny<object>(), It.IsAny<string>()))
            .Returns(new JsonObject());
        var correlation = new Mock<ICorrelationService>();
        var callback = new Mock<ICallbackClient>();
        callback.Setup(c => c.SendAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(Response<JsonObject?>.Success(new JsonObject()));

        var orchestrator = new CallbackOrchestrator();
        var headers = new Dictionary<string, string> { { "k", "v" } };
        var rsp = await orchestrator.SendJsonAsync("http://cb", headers, new { }, "key", jsonAdapter.Object, correlation.Object, new JsonSerializerOptions(), callback.Object, CancellationToken.None, "cid");

        Assert.True(rsp.IsSuccess);
        callback.Verify(c => c.SendAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SendJsonAsync_Propagates_Failure()
    {
        var jsonAdapter = new Mock<IJsonAdapter>();
        jsonAdapter.Setup(j => j.Transform(It.IsAny<object>(), It.IsAny<string>()))
            .Returns(new JsonObject());
        var correlation = new Mock<ICorrelationService>();
        var callback = new Mock<ICallbackClient>();
        callback.Setup(c => c.SendAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(Response<JsonObject?>.Fail("bad", System.Net.HttpStatusCode.BadRequest));

        var orchestrator = new CallbackOrchestrator();
        var headers = new Dictionary<string, string> { { "k", "v" } };
        var rsp = await orchestrator.SendJsonAsync("http://cb", headers, new { }, "key", jsonAdapter.Object, correlation.Object, new JsonSerializerOptions(), callback.Object, CancellationToken.None, "cid");

        Assert.False(rsp.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, rsp.StatusCode);
        callback.Verify(c => c.SendAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<System.Net.Http.StringContent>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Once);
    }
}
