using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Connect.Services;
using SIPS.Adapter;
using SIPS.Adapter.Models;
using SIPS.Core.Interfaces;
using SIPS.Core.Models;
using LoginResponse = SIPS.Core.Models.LoginResponse;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.XMLDsig.Xades.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Options;
using SIPS.XMLDsig.Xades.Services;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class SensitiveLoggingSecurityTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Balance_payloads_names_and_amounts_do_not_enter_logs(HttpStatusCode status)
    {
        const string sensitiveName = "PRIVATE-CUSTOMER-789";
        const string payload = "{\"data\":{\"participantName\":\"PRIVATE-CUSTOMER-789\",\"lastKnownBalance\":987654.32}}";
        using var client = new HttpClient(new ResponseHandler(status, payload));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        var auth = new Mock<ICoreAuthService>();
        auth.Setup(x => x.GetAuthTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("private-test-token");
        var logs = new CaptureLogger<BalanceMonitoringService>();
        var service = new BalanceMonitoringService(factory.Object, logs, new ConfigurationBuilder().Build(), auth.Object);
        var result = await service.GetBalanceStatusAsync();
        if (status == HttpStatusCode.OK) Assert.Equal(sensitiveName, result!.ParticipantName);
        else Assert.Null(result);
        var output = string.Join("\n", logs.Messages);
        Assert.NotEmpty(logs.Messages);
        Assert.DoesNotContain(sensitiveName, output);
        Assert.DoesNotContain("987654.32", output);
        Assert.DoesNotContain("private-test-token", output);
    }

    [Fact]
    public void Adapter_failure_does_not_log_sensitive_conversion_exception_text()
    {
        var logs = new CaptureLogger<JsonAdapter>();
        var options = new JsonAdapterOptions { Endpoints = new() { ["test"] = new EndpointMapping {
            FieldMappings = [new FieldMapping { InternalField = "Value", UserField = "value", Type = "string" }] } } };
        var adapter = new JsonAdapter(options, logs);
        var result = adapter.Transform(new SensitivePayload(), "test");
        Assert.Null(result["value"]);
        Assert.NotEmpty(logs.Messages);
        Assert.DoesNotContain("PRIVATE-ACCOUNT-123456", string.Join("\n", logs.Messages));
    }

    [Fact]
    public async Task Login_failures_do_not_log_server_error_payloads()
    {
        var logs = new CaptureLogger<AuthService>();
        var http = new Mock<IRepositoryHttpClient>();
        http.Setup(x => x.PostAsync<LoginRequest, LoginResponse>(It.IsAny<string>(), It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RepositoryResponse<LoginResponse>.BadRequest("PRIVATE-ACCOUNT-123456"));
        var service = new AuthService(new CoreOptions { LoginEndpoint = "/login", Username = "test", Password = "test-only" },
            logs, http.Object, Mock.Of<ICacheService>());
        var result = await service.LoginAsync();
        Assert.Null(result.Token);
        Assert.NotEmpty(logs.Messages);
        Assert.DoesNotContain("PRIVATE-ACCOUNT-123456", string.Join("\n", logs.Messages));
    }

    [Fact]
    public async Task Certificate_lookup_failures_do_not_log_remote_error_payloads()
    {
        var logs = new CaptureLogger<NativeVerifier>();
        var download = new Mock<ICertificateDownloadService>();
        download.Setup(x => x.GetCertificatesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(((CertificateDownloadResponse?)null, (string?)"PRIVATE-ACCOUNT-123456"));
        var verifier = new NativeVerifier(new XadesOptions(), logs, Mock.Of<ICertificateService>(MockBehavior.Strict), download.Object);
        var fixture = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "recall-outcome-unresolved-callback-20260926.xml"));
        var result = await verifier.VerifySignature(fixture, true, XadesProfile.WpSipsPapss, default);
        Assert.False(result.result);
        Assert.NotEmpty(logs.Messages);
        download.Verify(x => x.GetCertificatesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), true), Times.Once);
        Assert.DoesNotContain("PRIVATE-ACCOUNT-123456", string.Join("\n", logs.Messages));
    }

    public sealed class SensitivePayload { public object Value { get; } = new SensitiveValue(); }
    public sealed class SensitiveValue
    {
        public override string ToString() => throw new FormatException("PRIVATE-ACCOUNT-123456");
    }

    sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
    sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception) + (exception is null ? "" : exception.ToString()));
    }
}
