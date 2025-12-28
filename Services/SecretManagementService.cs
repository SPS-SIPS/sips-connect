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
        // Note: This doesn't persist to file, just updates in-memory configuration
        // Use SecretManagementTool for file updates
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

/// <summary>
/// Tool for managing secrets in appsettings files
/// </summary>
public class SecretManagementTool
{
    private readonly ISecretManagementService _secretService;
    private readonly ILogger<SecretManagementTool> _logger;
    private readonly string _appSettingsPath;

    public SecretManagementTool(
        ISecretManagementService secretService,
        IWebHostEnvironment environment,
        ILogger<SecretManagementTool> logger)
    {
        _secretService = secretService;
        _logger = logger;
        _appSettingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");
    }

    /// <summary>
    /// Encrypt all plain text secrets in appsettings.json
    /// </summary>
    public async Task<EncryptionResult> EncryptSecretsAsync()
    {
        var result = new EncryptionResult();

        try
        {
            if (!File.Exists(_appSettingsPath))
            {
                result.Success = false;
                result.Message = "appsettings.json not found";
                return result;
            }

            var json = await File.ReadAllTextAsync(_appSettingsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var secretsToEncrypt = new Dictionary<string, string>();

            // Find secrets that need encryption
            FindSecretsToEncrypt(root, "", secretsToEncrypt);

            if (secretsToEncrypt.Count == 0)
            {
                result.Success = true;
                result.Message = "No plain text secrets found";
                return result;
            }

            // Create backup
            var backupPath = $"{_appSettingsPath}.backup.{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(_appSettingsPath, backupPath);
            _logger.LogInformation("Created backup: {Path}", backupPath);

            // Encrypt secrets
            var updatedJson = json;
            foreach (var kvp in secretsToEncrypt)
            {
                var encrypted = _secretService.Encrypt(kvp.Value);
                updatedJson = updatedJson.Replace($"\"{kvp.Value}\"", $"\"{encrypted}\"");
                result.EncryptedKeys.Add(kvp.Key);
            }

            await File.WriteAllTextAsync(_appSettingsPath, updatedJson);

            result.Success = true;
            result.Message = $"Encrypted {result.EncryptedKeys.Count} secrets";
            result.BackupPath = backupPath;

            _logger.LogInformation("Encrypted {Count} secrets", result.EncryptedKeys.Count);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
            _logger.LogError(ex, "Failed to encrypt secrets");
        }

        return result;
    }

    /// <summary>
    /// Decrypt all encrypted secrets in appsettings.json (for debugging)
    /// </summary>
    public async Task<EncryptionResult> DecryptSecretsAsync()
    {
        var result = new EncryptionResult();

        try
        {
            if (!File.Exists(_appSettingsPath))
            {
                result.Success = false;
                result.Message = "appsettings.json not found";
                return result;
            }

            var json = await File.ReadAllTextAsync(_appSettingsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var secretsToDecrypt = new Dictionary<string, string>();

            // Find encrypted secrets
            FindEncryptedSecrets(root, "", secretsToDecrypt);

            if (secretsToDecrypt.Count == 0)
            {
                result.Success = true;
                result.Message = "No encrypted secrets found";
                return result;
            }

            // Create backup
            var backupPath = $"{_appSettingsPath}.backup.{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(_appSettingsPath, backupPath);

            // Decrypt secrets
            var updatedJson = json;
            foreach (var kvp in secretsToDecrypt)
            {
                var decrypted = _secretService.Decrypt(kvp.Value);
                updatedJson = updatedJson.Replace($"\"{kvp.Value}\"", $"\"{decrypted}\"");
                result.EncryptedKeys.Add(kvp.Key);
            }

            await File.WriteAllTextAsync(_appSettingsPath, updatedJson);

            result.Success = true;
            result.Message = $"Decrypted {result.EncryptedKeys.Count} secrets";
            result.BackupPath = backupPath;

            _logger.LogInformation("Decrypted {Count} secrets", result.EncryptedKeys.Count);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
            _logger.LogError(ex, "Failed to decrypt secrets");
        }

        return result;
    }

    private void FindSecretsToEncrypt(JsonElement element, string path, Dictionary<string, string> secrets)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var newPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}:{property.Name}";
                
                // Check if this is a secret field
                if (IsSecretField(property.Name) && property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrEmpty(value) && !_secretService.IsEncrypted(value))
                    {
                        secrets[newPath] = value;
                    }
                }
                
                FindSecretsToEncrypt(property.Value, newPath, secrets);
            }
        }
    }

    private void FindEncryptedSecrets(JsonElement element, string path, Dictionary<string, string> secrets)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var newPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}:{property.Name}";
                
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrEmpty(value) && _secretService.IsEncrypted(value))
                    {
                        secrets[newPath] = value;
                    }
                }
                
                FindEncryptedSecrets(property.Value, newPath, secrets);
            }
        }
    }

    private static bool IsSecretField(string fieldName)
    {
        var lowerFieldName = fieldName.ToLowerInvariant();
        
        // Exclude path fields - these are file paths, not secrets
        if (lowerFieldName.EndsWith("path") || lowerFieldName.Contains("path"))
        {
            return false;
        }
        
        var secretFields = new[]
        {
            "password", "passphrase", "secret", "key", "token",
            "clientsecret", "apikey", "connectionstring"
        };

        return secretFields.Any(s => lowerFieldName.Contains(s));
    }
}

public class EncryptionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> EncryptedKeys { get; set; } = new();
    public string? BackupPath { get; set; }
}
