using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.ISO20022.Models.DTOs;
using Xunit;

namespace SIPS.Core.Tests.Tests
{
    public sealed class InterfaceHttpClient_Tests
    {
        private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler = handler;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => _handler(request, cancellationToken);
        }

        private static InterfaceHttpClient CreateClient(HttpMessageHandler handler, int httpTimeoutSeconds = 2)
        {
            var logger = new NullLogger<InterfaceHttpClient>();
            var httpClient = new HttpClient(handler);
            var options = Microsoft.Extensions.Options.Options.Create(new CoreOptions { HttpTimeoutSeconds = httpTimeoutSeconds });
            return new InterfaceHttpClient(logger, httpClient, options);
        }

        [Fact]
        public async Task Send_Should_Timeout_When_Server_Hangs()
        {
            var handler = new StubHandler(async (req, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });
            var client = CreateClient(handler, httpTimeoutSeconds: 1);
            var headers = new Dictionary<string, string>
            {
                {"X-Idempotency-Key", "idem"},
                {"X-Transaction-Id", "tx123"},
                {"X-Correlation-Id", "corr123"}
            };
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            var rsp = await client.Send("http://unit.test/timeout", headers, content, CancellationToken.None);

            Assert.False(rsp.IsSuccess);
            Assert.Equal(HttpStatusCode.RequestTimeout, rsp.StatusCode);
        }

        [Fact]
        public async Task Send_Should_Return_Success_For_200_Json()
        {
            var handler = new StubHandler((req, ct) =>
            {
                var json = new JsonObject { ["ok"] = true };
                var http = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json")
                };
                return Task.FromResult(http);
            });
            var client = CreateClient(handler, httpTimeoutSeconds: 5);
            var headers = new Dictionary<string, string>();
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            var rsp = await client.Send("http://unit.test/success", headers, content, CancellationToken.None);

            Assert.True(rsp.IsSuccess);
            Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
            Assert.NotNull(rsp.Data);
            Assert.Equal("true", rsp.Data?["ok"]?.ToString());
        }

        [Fact]
        public async Task Send4XML_Should_Succeed_For_200_Xml()
        {
            var handler = new StubHandler((req, ct) =>
            {
                var http = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<ok>true</ok>", Encoding.UTF8, "application/xml")
                };
                return Task.FromResult(http);
            });
            var client = CreateClient(handler, httpTimeoutSeconds: 5);
            var content = new StringContent("<req/>", Encoding.UTF8, "application/xml");

            var rsp = await client.Send4XML("http://unit.test/xml", content, CancellationToken.None);

            Assert.True(rsp.IsSuccess);
            Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
            Assert.NotNull(rsp.Data);
        }
    }
}
