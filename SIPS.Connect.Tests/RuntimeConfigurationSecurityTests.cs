using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SIPS.Connect.Config;
using SIPS.Connect.Controllers;
using SIPS.Connect.Services;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class RuntimeConfigurationSecurityTests
{
    [Fact]
    public async Task Production_denies_all_configuration_mutations_even_for_configuration_administrators()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Core:CallbackClientSecret"] = "PRIVATE-OAUTH-SECRET" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddControllers().AddApplicationPart(typeof(ConfigurationsController).Assembly);
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(new ApiKeys([
            new() { Name = "configuration-admin", Key = "admin-test-key", Secret = "test-secret", IncludeDefaultRoles = false, Roles = [KnownRoles.Admin] }
        ]));
        builder.Services.AddSingleton(Mock.Of<ISecretManagementService>(MockBehavior.Strict));
        builder.Services.AddAuthentication(ApiKeyDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.AuthenticationScheme, _ => { });
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        foreach (var (method, path, body) in new[] {
            (HttpMethod.Put, "Core", "{}"), (HttpMethod.Put, "Xades", "{}"), (HttpMethod.Put, "ISO20022", "{}"),
            (HttpMethod.Put, "Emv", "{}"), (HttpMethod.Put, "Origins", "{}"), (HttpMethod.Put, "Hosts", "[]"),
            (HttpMethod.Put, "APIKeys", "{}"), (HttpMethod.Delete, "APIKeys?key=test", "{}") })
        {
            using var request = new HttpRequestMessage(method, "/api/v1/Configurations/" + path) {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            request.Headers.Add(ApiKeyDefaults.HeaderNameKey, "admin-test-key");
            request.Headers.Add(ApiKeyDefaults.HeaderNameSecret, "test-secret");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Configurations/Core");
        read.Headers.Add(ApiKeyDefaults.HeaderNameKey, "admin-test-key");
        read.Headers.Add(ApiKeyDefaults.HeaderNameSecret, "test-secret");
        using var readResponse = await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.DoesNotContain("PRIVATE-OAUTH-SECRET", await readResponse.Content.ReadAsStringAsync());
        await app.StopAsync();
    }
}
