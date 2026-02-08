using Microsoft.AspNetCore.DataProtection;

namespace SIPS.Connect.Services;

/// <summary>
/// Secure key storage service using ASP.NET Core Data Protection
/// </summary>
public interface ISecureKeyStorageService
{
    string GetPrivateKey();
    string GetCertificate();
    string? GetPassphrase();
}

public class SecureKeyStorageService : ISecureKeyStorageService
{
    private readonly IConfiguration _configuration;
    private readonly IDataProtector? _protector;
    private readonly ILogger<SecureKeyStorageService> _logger;
    private readonly bool _useEncryption;

    public SecureKeyStorageService(
        IConfiguration configuration,
        IDataProtectionProvider? dataProtectionProvider,
        ILogger<SecureKeyStorageService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        // Check if encryption is enabled
        _useEncryption = configuration.GetValue<bool>("Security:EncryptKeys", false);
        
        if (_useEncryption && dataProtectionProvider != null)
        {
            _protector = dataProtectionProvider.CreateProtector("XadesKeys");
            _logger.LogInformation("Secure key storage initialized with encryption");
        }
        else
        {
            _logger.LogWarning("Secure key storage initialized WITHOUT encryption - not recommended for production");
        }
    }

    public string GetPrivateKey()
    {
        // Priority: Environment Variable > Encrypted File > Plain File
        
        // 1. Try environment variable first (best for containers/cloud)
        var envKey = Environment.GetEnvironmentVariable("XADES_PRIVATE_KEY");
        if (!string.IsNullOrEmpty(envKey))
        {
            _logger.LogInformation("Loading private key from environment variable");
            return envKey;
        }

        // 2. Try Docker/Kubernetes secret file
        var secretFile = _configuration["XADES_PRIVATE_KEY_FILE"];
        if (!string.IsNullOrEmpty(secretFile) && File.Exists(secretFile))
        {
            _logger.LogInformation("Loading private key from secret file: {File}", secretFile);
            return File.ReadAllText(secretFile);
        }

        // 3. Load from configured path
        var keyPath = _configuration["Xades:PrivateKeyPath"];
        if (string.IsNullOrEmpty(keyPath))
        {
            throw new InvalidOperationException("Private key path not configured");
        }

        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException($"Private key file not found: {keyPath}");
        }

        var keyContent = File.ReadAllText(keyPath);

        // If encryption is enabled and file appears to be encrypted, decrypt it
        if (_useEncryption && _protector != null && IsEncrypted(keyContent))
        {
            _logger.LogInformation("Decrypting private key from: {Path}", keyPath);
            return _protector.Unprotect(keyContent);
        }

        _logger.LogWarning("Loading unencrypted private key from: {Path}", keyPath);
        return keyContent;
    }

    public string GetCertificate()
    {
        // Priority: Environment Variable > File
        
        var envCert = Environment.GetEnvironmentVariable("XADES_CERTIFICATE");
        if (!string.IsNullOrEmpty(envCert))
        {
            _logger.LogInformation("Loading certificate from environment variable");
            return envCert;
        }

        var secretFile = _configuration["XADES_CERTIFICATE_FILE"];
        if (!string.IsNullOrEmpty(secretFile) && File.Exists(secretFile))
        {
            _logger.LogInformation("Loading certificate from secret file: {File}", secretFile);
            return File.ReadAllText(secretFile);
        }

        var certPath = _configuration["Xades:CertificatePath"];
        if (string.IsNullOrEmpty(certPath))
        {
            throw new InvalidOperationException("Certificate path not configured");
        }

        if (!File.Exists(certPath))
        {
            throw new FileNotFoundException($"Certificate file not found: {certPath}");
        }

        _logger.LogInformation("Loading certificate from: {Path}", certPath);
        return File.ReadAllText(certPath);
    }

    public string? GetPassphrase()
    {
        // Priority: Environment Variable > Configuration
        
        var envPassphrase = Environment.GetEnvironmentVariable("XADES_PRIVATE_KEY_PASSPHRASE");
        if (!string.IsNullOrEmpty(envPassphrase))
        {
            _logger.LogInformation("Loading passphrase from environment variable");
            return envPassphrase;
        }

        var secretFile = _configuration["XADES_PASSPHRASE_FILE"];
        if (!string.IsNullOrEmpty(secretFile) && File.Exists(secretFile))
        {
            _logger.LogInformation("Loading passphrase from secret file");
            return File.ReadAllText(secretFile).Trim();
        }

        var passphrase = _configuration["Xades:PrivateKeyPassphrase"];
        if (!string.IsNullOrEmpty(passphrase))
        {
            _logger.LogWarning("Loading passphrase from configuration - not recommended for production");
        }

        return passphrase;
    }

    private static bool IsEncrypted(string content)
    {
        // Simple heuristic: encrypted content won't start with "-----BEGIN"
        return !content.TrimStart().StartsWith("-----BEGIN");
    }
}

/// <summary>
/// Utility service to encrypt existing key files
/// </summary>
public class KeyEncryptionUtility
{
    private readonly IDataProtector _protector;
    private readonly ILogger<KeyEncryptionUtility> _logger;

    public KeyEncryptionUtility(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<KeyEncryptionUtility> logger)
    {
        _protector = dataProtectionProvider.CreateProtector("XadesKeys");
        _logger = logger;
    }

    /// <summary>
    /// Encrypt a plain key file
    /// </summary>
    public void EncryptKeyFile(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input file not found: {inputPath}");
        }

        var plainContent = File.ReadAllText(inputPath);
        var encryptedContent = _protector.Protect(plainContent);
        
        File.WriteAllText(outputPath, encryptedContent);
        
        // Set restrictive permissions (Unix-like systems)
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(outputPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        _logger.LogInformation("Encrypted key file: {Input} -> {Output}", inputPath, outputPath);
    }

    /// <summary>
    /// Decrypt an encrypted key file
    /// </summary>
    public void DecryptKeyFile(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input file not found: {inputPath}");
        }

        var encryptedContent = File.ReadAllText(inputPath);
        var plainContent = _protector.Unprotect(encryptedContent);
        
        File.WriteAllText(outputPath, plainContent);
        
        // Set restrictive permissions
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(outputPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        _logger.LogInformation("Decrypted key file: {Input} -> {Output}", inputPath, outputPath);
    }
}
