using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Connect.Controllers;
using SIPS.Connect.Services;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class SecretManagementSecurityTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sips-secrets-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IWebHostEnvironment> environment = new();
    private readonly SecretManagementService secrets;
    private readonly SecretManagementTool tool;
    private string Settings => Path.Combine(directory, "appsettings.json");

    public SecretManagementSecurityTests()
    {
        Directory.CreateDirectory(directory);
        environment.SetupGet(x => x.ContentRootPath).Returns(directory);
        environment.SetupGet(x => x.EnvironmentName).Returns("Development");
        secrets = new(new EphemeralDataProtectionProvider(), new ConfigurationBuilder().Build(), NullLogger<SecretManagementService>.Instance);
        tool = new(secrets, environment.Object, NullLogger<SecretManagementTool>.Instance);
    }

    [Fact]
    public async Task Bulk_encryption_handles_arrays_escaping_and_equal_nonsecret_values_without_plaintext_backup()
    {
        const string value = "quotes\" slash\\ newline\n unicode\u263a";
        var original = new JsonObject
        {
            ["Description"] = value,
            ["Core"] = new JsonObject { ["Password"] = value, ["Username"] = value },
            ["ApiKeys"] = new JsonArray(new JsonObject { ["Name"] = "client", ["Key"] = "public-id", ["Secret"] = value }),
            ["DataProtection"] = new JsonObject { ["CertificatePassword"] = value },
            ["ConnectionStrings"] = new JsonObject { ["db"] = "Host=example;Password=from-deployment" }
        };
        await File.WriteAllTextAsync(Settings, original.ToJsonString());

        var result = await tool.EncryptSecretsAsync();

        Assert.True(result.Success);
        Assert.Equal(2, result.EncryptedKeys.Count);
        Assert.Contains("DataProtection:CertificatePassword", result.SkippedKeys);
        Assert.Contains("ConnectionStrings:db", result.SkippedKeys);
        var encrypted = JsonNode.Parse(await File.ReadAllTextAsync(Settings))!;
        Assert.Equal(value, secrets.Decrypt(encrypted["Core"]!["Password"]!.GetValue<string>()));
        Assert.Equal(value, secrets.Decrypt(encrypted["ApiKeys"]![0]!["Secret"]!.GetValue<string>()));
        Assert.Equal("public-id", encrypted["ApiKeys"]![0]!["Key"]!.GetValue<string>());
        Assert.Equal(value, encrypted["Description"]!.GetValue<string>());
        Assert.Equal(value, encrypted["Core"]!["Username"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(directory));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Settings));
        var again = await tool.EncryptSecretsAsync();
        Assert.True(again.Success);
        Assert.Empty(again.EncryptedKeys);
        var decrypted = await tool.DecryptSecretsAsync();
        Assert.True(decrypted.Success);
        Assert.Equal(value, JsonNode.Parse(await File.ReadAllTextAsync(Settings))!["ApiKeys"]![0]!["Secret"]!.GetValue<string>());
    }

    [Fact]
    public async Task Failed_transformation_preserves_original_bytes_and_creates_no_backup()
    {
        var original = "{\"Core\":{\"Password\":\"value-from-test\"}}";
        await File.WriteAllTextAsync(Settings, original);
        var service = new Mock<ISecretManagementService>();
        service.Setup(x => x.Encrypt(It.IsAny<string>())).Throws(new InvalidOperationException("sensitive exception detail"));
        var failing = new SecretManagementTool(service.Object, environment.Object, NullLogger<SecretManagementTool>.Instance);

        var result = await failing.EncryptSecretsAsync();

        Assert.False(result.Success);
        Assert.DoesNotContain("sensitive exception detail", result.Message);
        Assert.Equal(original, await File.ReadAllTextAsync(Settings));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Production_cannot_export_or_persist_plaintext_secrets()
    {
        environment.SetupGet(x => x.EnvironmentName).Returns("Production");
        var service = new Mock<ISecretManagementService>(MockBehavior.Strict);
        var productionTool = new SecretManagementTool(service.Object, environment.Object, NullLogger<SecretManagementTool>.Instance);
        var controller = new SecretManagementController(service.Object, productionTool, NullLogger<SecretManagementController>.Instance, environment.Object);

        Assert.IsType<NotFoundResult>(controller.GetSecret("Core:Password"));
        Assert.IsType<NotFoundResult>(controller.DecryptValue(new SecretRequest { Value = "ENCRYPTED:test" }));
        Assert.IsType<NotFoundResult>(await controller.DecryptAllSecrets());
        Assert.False((await productionTool.DecryptSecretsAsync()).Success);
        service.VerifyNoOtherCalls();
        Assert.Empty(Directory.GetFiles(directory));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
