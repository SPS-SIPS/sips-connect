using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SIPS.Core.Interfaces;
using SIPS.Core.Options;
using SIPS.Core.Services.Callback;
using SIPS.ISO20022.Models.DTOs;
using Xunit;

namespace SIPS.Core.Tests.Tests
{
    public sealed class CallbackClient_Tests
    {
        private sealed class FakeHttpClient : IInterfaceHttpClient
        {
            public Dictionary<string, string>? LastHeaders { get; private set; }
            public Task<Response<JsonObject?>> Send(string completeUrl, Dictionary<string, string>? headers, StringContent content, CancellationToken cancellationToken = default)
            {
                LastHeaders = headers;
                return Task.FromResult(Response<JsonObject?>.Success(new JsonObject()));
            }
            public Task<Response<string>> Send4XML(string url, StringContent content, CancellationToken cancellationToken = default)
                => Task.FromResult(Response<string>.Success("ok"));
            public void AddAuthHeaders(string accessToken) { }
        }

        [Fact]
        public async Task SendAsync_Should_Add_Correlation_Header()
        {
            var fake = new FakeHttpClient();
            var logger = new NullLogger<CallbackClient>();
            var options = Microsoft.Extensions.Options.Options.Create(new CoreOptions { IncludeIdempotencyHeaders = true });
            var correlation = new Services.Correlation.CorrelationService();
            var client = new CallbackClient(fake, logger, correlation, options);

            var headers = new Dictionary<string, string>();
            var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            await client.SendAsync("http://unit.test/cb", headers, content, CancellationToken.None);

            Assert.NotNull(fake.LastHeaders);
            Assert.True(fake.LastHeaders!.ContainsKey("X-Correlation-Id"));
        }

        [Fact]
        public async Task SendAsync_Should_Remove_Idempotency_When_Flag_Off()
        {
            var fake = new FakeHttpClient();
            var logger = new NullLogger<CallbackClient>();
            var options = Microsoft.Extensions.Options.Options.Create(new CoreOptions { IncludeIdempotencyHeaders = false });
            var correlation = new Services.Correlation.CorrelationService();
            var client = new CallbackClient(fake, logger, correlation, options);

            var headers = new Dictionary<string, string> { { "X-Idempotency-Key", "idem-key" } };
            var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            await client.SendAsync("http://unit.test/cb", headers, content, CancellationToken.None);

            Assert.NotNull(fake.LastHeaders);
            Assert.False(fake.LastHeaders!.ContainsKey("X-Idempotency-Key"));
        }

        [Fact]
        public async Task SendAsync_Should_Keep_Idempotency_When_Flag_On()
        {
            var fake = new FakeHttpClient();
            var logger = new NullLogger<CallbackClient>();
            var options = Microsoft.Extensions.Options.Options.Create(new CoreOptions { IncludeIdempotencyHeaders = true });
            var correlation = new Services.Correlation.CorrelationService();
            var client = new CallbackClient(fake, logger, correlation, options);

            var headers = new Dictionary<string, string> { { "X-Idempotency-Key", "idem-key" } };
            var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            await client.SendAsync("http://unit.test/cb", headers, content, CancellationToken.None);

            Assert.NotNull(fake.LastHeaders);
            Assert.True(fake.LastHeaders!.TryGetValue("X-Idempotency-Key", out var value));
            Assert.Equal("idem-key", value);
        }
    }
}
