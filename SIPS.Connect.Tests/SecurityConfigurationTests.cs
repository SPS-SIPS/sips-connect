using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using SIPS.Connect.Config;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class SecurityConfigurationTests
{
    [Fact]
    public void Production_rejects_unsigned_mode()
    {
        var config = Configuration(new() { ["Xades:WithoutPKI"] = "true" });
        Assert.Throws<InvalidOperationException>(() => SecurityConfiguration.Validate(config, Environment("Production")));
    }

    [Fact]
    public void Nonwindows_production_requires_key_encryption_certificate()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Throws<InvalidOperationException>(() => SecurityConfiguration.Validate(Configuration(new()), Environment("Production")));
    }

    [Fact]
    public void Existing_plaintext_key_ring_is_rejected_even_after_configuring_a_certificate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sips-ring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "key-test.xml"), "<key><descriptor><masterKey><value>test-value</value></masterKey></descriptor></key>");
            var config = Configuration(new() { ["DataProtection:CertificatePath"] = "deployment.pfx", ["DataProtection:KeyDirectory"] = directory,
                ["Keycloak:Realm:Protocol"] = "https", ["ISO20022:Key"] = "working-key", ["ISO20022:Secret"] = "working-secret" });
            Assert.Throws<InvalidOperationException>(() => SecurityConfiguration.Validate(config, Environment("Staging")));
            File.WriteAllText(Path.Combine(directory, "key-test.xml"), "<key><descriptor><encryptedSecret><encryptedKey>opaque</encryptedKey></encryptedSecret></descriptor></key>");
            SecurityConfiguration.Validate(config, Environment("Staging"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Development_can_use_unsigned_local_configuration()
    {
        SecurityConfiguration.Validate(Configuration(new() { ["Xades:WithoutPKI"] = "true" }), Environment("Development"));
    }

    [Theory]
    [InlineData("ISO20022:Transfer", "http://bank.example/transfer")]
    [InlineData("Core:CallbackTokenEndpoint", "http://identity.example/token")]
    [InlineData("Core:BaseUrl", "https://user:password@core.example")]
    public void Production_rejects_cleartext_or_embedded_credentials(string field, string value)
    {
        var config = Configuration(new()
        {
            ["DataProtection:CertificatePath"] = "deployment.pfx", ["Keycloak:Realm:Protocol"] = "https", [field] = value
        });
        Assert.Throws<InvalidOperationException>(() => SecurityConfiguration.Validate(config, Environment("Production")));
    }

    [Theory]
    [InlineData("ISO20022:Secret", "123", "callbacks")]
    [InlineData("ISO20022:Key", "api_key", "callbacks")]
    [InlineData("ISO20022:Secret", "api_secret", "callbacks")]
    [InlineData("ConnectionStrings:db", "Host=localhost;Username=app;Password=postgres", "database")]
    public void Production_rejects_public_callback_and_database_defaults(string field, string value, string message)
    {
        var values = new Dictionary<string, string?>
        {
            ["DataProtection:CertificatePath"] = "deployment.pfx", ["Keycloak:Realm:Protocol"] = "https",
            ["ISO20022:Key"] = "working-key", ["ISO20022:Secret"] = "working-secret"
        };
        values[field] = value;
        var error = Assert.Throws<InvalidOperationException>(() => SecurityConfiguration.Validate(Configuration(values), Environment("Production")));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-fingerprint")]
    public void Production_requires_valid_domestic_switch_pins(string pin)
    {
        var config = Configuration(new() { ["DataProtection:CertificatePath"] = "deployment.pfx", ["Keycloak:Realm:Protocol"] = "https",
            ["ISO20022:Key"] = "working-key", ["ISO20022:Secret"] = "working-secret", ["Xades:DomesticSwitchCertificateSha256:0"] = pin });
        Assert.Contains("DomesticSwitchCertificateSha256", Assert.Throws<InvalidOperationException>(() => SecurityConfiguration.Validate(config, Environment("Production"))).Message);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Xades:DomesticSwitchCertificateSha256:0"] = new string('a', 64) }).AddInMemoryCollection(values).Build();
    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(name);
        return environment.Object;
    }
}
