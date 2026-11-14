using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class ApiKeySecurityTests
{
    [Fact]
    public void Unavailable_data_protection_certificate_cannot_fall_back_to_unencrypted_keys()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Xades:WithoutPKI"] = "true",
            ["DataProtection:CertificatePath"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pfx")
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        Assert.Throws<InvalidOperationException>(() => DI.Register(services, configuration));
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    public void Jwt_discovery_requires_https_outside_development(string name, bool expected)
    {
        using var provider = JwtServices(name, validateIssuer: true);
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(expected, options.RequireHttpsMetadata);
    }

    [Fact]
    public void Production_cannot_disable_jwt_issuer_validation()
    {
        using var provider = JwtServices("Production", validateIssuer: false);
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme));
    }

    [Fact]
    public void Unreadable_encrypted_secret_disables_only_the_affected_key()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiKeys:0:Key"] = "broken-key",
            ["ApiKeys:0:Secret"] = "ENCRYPTED:unreadable",
            ["ApiKeys:1:Key"] = "working-key",
            ["ApiKeys:1:Secret"] = "working-secret"
        }).Build();
        var secrets = new Mock<ISecretManagementService>();
        secrets.Setup(x => x.IsEncrypted("ENCRYPTED:unreadable")).Returns(true);
        secrets.Setup(x => x.Decrypt("ENCRYPTED:unreadable")).Throws<CryptographicException>();

        var keys = new ApiKeyProvider(configuration, secrets.Object).GetApiKeys();

        Assert.Equal("working-key", Assert.Single(keys).Key);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    [InlineData("ENCRYPTED:unreadable", "ENCRYPTED:unreadable")]
    [InlineData("working-secret", "incorrect-secret")]
    public async Task Empty_ciphertext_and_incorrect_secrets_cannot_authenticate(string configured, string supplied)
    {
        var result = await Authenticate(configured, supplied);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Multiple_secret_headers_are_rejected()
    {
        var result = await Authenticate("working-secret", "working-secret", "extra-secret");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Valid_key_keeps_its_baseline_and_explicit_roles()
    {
        var result = await Authenticate("working-secret", "working-secret");
        Assert.True(result.Succeeded);
        Assert.Equal("test-client", result.Principal!.Identity!.Name);
        Assert.True(result.Principal.IsInRole(KnownRoles.Gateway));
        Assert.True(result.Principal.IsInRole(KnownRoles.RecallClose));
        Assert.DoesNotContain(result.Principal.Claims, c => c.Type == ClaimTypes.Role && c.Value == KnownRoles.Admin);
    }

    private static async Task<AuthenticateResult> Authenticate(string configured, params string[] supplied)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ApiKeys([new ApiKey
        {
            Name = "test-client", Key = "test-key", Secret = configured, Roles = [KnownRoles.RecallClose]
        }]));
        services.AddAuthentication(ApiKeyDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.AuthenticationScheme, _ => { });
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers[ApiKeyDefaults.HeaderNameKey] = "test-key";
        context.Request.Headers[ApiKeyDefaults.HeaderNameSecret] = supplied;
        return await context.AuthenticateAsync(ApiKeyDefaults.AuthenticationScheme);
    }

    private static ServiceProvider JwtServices(string environmentName, bool validateIssuer)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Xades:WithoutPKI"] = "true",
            ["Core:SAFExpression"] = "*/5 * * * *",
            ["Core:SAFTimeZoneInfo"] = "Utc",
            ["Keycloak:Realm:Audience"] = "test-api",
            ["Keycloak:Realm:Host"] = "identity.example.test",
            ["Keycloak:Realm:Protocol"] = "https",
            ["Keycloak:Realm:Name"] = "test",
            ["Keycloak:Realm:ValidateIssuer"] = validateIssuer.ToString()
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(environmentName);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(environment.Object);
        DI.Register(services, configuration);
        return services.BuildServiceProvider();
    }
}
