using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Prometheus;
using SIPS.Connect.Config;
using SIPS.Connect.Controllers;
using SIPS.Connect.Models;
using SIPS.Connect.Services;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class DiagnosticsSecurityTests
{
    [Fact]
    public async Task Diagnostics_require_monitoring_permission_and_monitoring_does_not_grant_payment_access()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(new ApiKeys([
            new() { Name = "monitor", Key = "monitor-key", Secret = "working-secret", IncludeDefaultRoles = false, Roles = [KnownRoles.Monitoring] },
            new() { Name = "bank", Key = "bank-key", Secret = "working-secret" }
        ]));
        builder.Services.AddAuthentication(ApiKeyDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.AuthenticationScheme, _ => { });
        var health = new Mock<IHealthCheckService>();
        health.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new HealthCheckResponse());
        builder.Services.AddSingleton(health.Object);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapMetrics().RequireAuthorization(policy => policy.RequireRole(KnownRoles.Monitoring));
        app.MapGet("/payment-permission-probe", () => Results.Ok()).RequireAuthorization(policy => policy.RequireRole(KnownRoles.Gateway));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        foreach (var path in new[] { "/health", "/metrics" })
        {
            using var anonymous = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var bankRequest = Request(path, "bank-key");
            using var bankResponse = await client.SendAsync(bankRequest);
            Assert.Equal(HttpStatusCode.Forbidden, bankResponse.StatusCode);
            using var monitorRequest = Request(path, "monitor-key");
            using var monitorResponse = await client.SendAsync(monitorRequest);
            Assert.Equal(HttpStatusCode.OK, monitorResponse.StatusCode);
            if (path == "/health") Assert.True(monitorResponse.Headers.CacheControl?.NoStore);
        }
        using var paymentRequest = Request("/payment-permission-probe", "monitor-key");
        using var paymentResponse = await client.SendAsync(paymentRequest);
        Assert.Equal(HttpStatusCode.Forbidden, paymentResponse.StatusCode);
        await app.StopAsync();
    }

    private static HttpRequestMessage Request(string path, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, path);
        message.Headers.Add(ApiKeyDefaults.HeaderNameKey, key);
        message.Headers.Add(ApiKeyDefaults.HeaderNameSecret, "working-secret");
        return message;
    }
}
