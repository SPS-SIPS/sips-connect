using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace SIPS.Connect.Services;

/// <summary>
/// Service for managing encrypted secrets in configuration files
/// </summary>
public interface ISecretManagementService
{
    string GetSecret(string key);
    void SetSecret(string key, string value);
    bool IsEncrypted(string value);
    string Encrypt(string plainText);
    string Decrypt(string encryptedText);
}

public class SecretManagementService : ISecretManagementService
{
    private const string EncryptedPrefix = "ENCRYPTED:";
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecretManagementService> _logger;

    public SecretManagementService(
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        ILogger<SecretManagementService> logger)
    {
        _protector = dataProtectionProvider.CreateProtector("SIPS.Connect.Secrets");
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Get a secret value, automatically decrypting if needed
    /// </summary>
    public string GetSecret(string key)
    {
        var value = _configuration[key];
        
        if (string.IsNullOrEmpty(value))
        {
            _logger.LogWarning("Secret not found: {Key}", key);
            return string.Empty;
        }

        if (IsEncrypted(value))
        {
            try
            {
                return Decrypt(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt secret: {Key}", key);
                throw new InvalidOperationException($"Failed to decrypt secret: {key}", ex);
            }
        }

        // Return plain text value (for backwards compatibility)
        _logger.LogWarning("Secret is not encrypted: {Key}", key);
        return value;
    }

    /// <summary>
    /// Set a secret value (encrypts automatically)
    /// </summary>
    public void SetSecret(string key, string value)
    {
        var encryptedValue = Encrypt(value);
        _configuration[key] = encryptedValue;
        // File persistence is handled by SecretManagementTool.
        _logger.LogInformation("Secret encrypted for key: {Key}", key);
    }

    /// <summary>
    /// Check if a value is encrypted
    /// </summary>
    public bool IsEncrypted(string value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix);
    }

    /// <summary>
    /// Encrypt a plain text value
    /// </summary>
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        var encrypted = _protector.Protect(plainText);
        return $"{EncryptedPrefix}{encrypted}";
    }

    /// <summary>
    /// Decrypt an encrypted value
    /// </summary>
    public string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
        {
            return encryptedText;
        }

        if (!IsEncrypted(encryptedText))
        {
            throw new ArgumentException("Value is not encrypted", nameof(encryptedText));
        }

        var cipherText = encryptedText.Substring(EncryptedPrefix.Length);
        return _protector.Unprotect(cipherText);
    }
}

/// <summary>Updates recognized secret fields without plaintext backups or string substitution.</summary>
public class SecretManagementTool(
    ISecretManagementService secretService,
    IWebHostEnvironment environment,
    ILogger<SecretManagementTool> logger)
{
    private readonly SemaphoreSlim _updates = new(1, 1);
    private readonly string _appSettingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");

    public Task<EncryptionResult> EncryptSecretsAsync() => TransformAsync(decrypt: false);

    public Task<EncryptionResult> DecryptSecretsAsync()
    {
        if (!environment.IsDevelopment())
            return Task.FromResult(new EncryptionResult { Message = "Plaintext export is available only in Development." });
        return TransformAsync(decrypt: true);
    }

    private async Task<EncryptionResult> TransformAsync(bool decrypt)
    {
        await _updates.WaitAsync();
        string? temporary = null;
        try
        {
            var json = await File.ReadAllTextAsync(_appSettingsPath);
            var root = System.Text.Json.Nodes.JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
            }) ?? throw new JsonException("Configuration must contain a JSON object.");
            if (root is not System.Text.Json.Nodes.JsonObject) throw new JsonException("Configuration must contain a JSON object.");
            var result = new EncryptionResult();
            Visit(root, "", decrypt, result);
            if (result.EncryptedKeys.Count > 0)
            {
                temporary = _appSettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var stream = new FileStream(temporary, options))
                {
                    await JsonSerializer.SerializeAsync(stream, root, new JsonSerializerOptions { WriteIndented = true });
                    await stream.FlushAsync();
                }
                File.Move(temporary, _appSettingsPath, overwrite: true);
            }
            result.Success = true;
            result.Message = $"{(decrypt ? "Decrypted" : "Encrypted")} {result.EncryptedKeys.Count} fields. " +
                (result.SkippedKeys.Count > 0 ? "Bootstrap secrets and connection strings must use deployment-managed secrets; see skippedKeys." : "No plaintext backup was created.");
            return result;
        }
        catch (Exception error)
        {
            // Avoid copying parser exception text or input values into responses or logs.
            logger.LogError("Configuration secret update failed ({ErrorType})", error.GetType().Name);
            return new EncryptionResult { Message = "Configuration secret update failed; the original configuration was retained." };
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            _updates.Release();
        }
    }

    private void Visit(System.Text.Json.Nodes.JsonNode node, string path, bool decrypt, EncryptionResult result)
    {
        if (node is System.Text.Json.Nodes.JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
                if (array[index] is { } child) Visit(child, $"{path}:{index}", decrypt, result);
        }
        else if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                var currentPath = string.IsNullOrEmpty(path) ? property.Key : $"{path}:{property.Key}";
                if (property.Value is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text))
                {
                    var encrypted = secretService.IsEncrypted(text);
                    var bootstrap = currentPath.StartsWith("DataProtection:", StringComparison.OrdinalIgnoreCase) ||
                        currentPath.StartsWith("Kestrel:", StringComparison.OrdinalIgnoreCase) ||
                        currentPath.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase);
                    if (bootstrap)
                    {
                        if (!encrypted && (IsSecretField(property.Key) || currentPath.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)))
                            result.SkippedKeys.Add(currentPath);
                        continue;
                    }
                    // API-key identifiers are not protected values: the authenticator must look them up as sent.
                    var apiKeyIdentifier = path.StartsWith("ApiKeys:", StringComparison.OrdinalIgnoreCase) && property.Key.Equals("Key", StringComparison.OrdinalIgnoreCase);
                    if (!apiKeyIdentifier && (decrypt ? encrypted : IsSecretField(property.Key) && !encrypted))
                    {
                        obj[property.Key] = decrypt ? secretService.Decrypt(text) : secretService.Encrypt(text);
                        result.EncryptedKeys.Add(currentPath);
                    }
                }
                else if (property.Value is { } child) Visit(child, currentPath, decrypt, result);
            }
        }
    }

    private static bool IsSecretField(string name)
    {
        var field = name.ToLowerInvariant();
        if (new[] { "path", "url", "endpoint", "host" }.Any(field.Contains)) return false;
        return new[] { "password", "passphrase", "secret", "key", "token" }.Any(field.Contains);
    }
}

public class EncryptionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> EncryptedKeys { get; set; } = new();
    public List<string> SkippedKeys { get; set; } = new();
    public string? BackupPath { get; set; }
}
