using Microsoft.AspNetCore.DataProtection;
using System.Reflection;

namespace SIPS.Connect.Config;

/// <summary>
/// Helper to decrypt ENCRYPTED: prefixed values in configuration options
/// </summary>
public static class ConfigurationDecryptor
{
    private const string EncryptedPrefix = "ENCRYPTED:";

    /// <summary>
    /// Decrypt all string properties in an options object that have ENCRYPTED: prefix
    /// </summary>
    public static T DecryptOptions<T>(T options, IDataProtectionProvider dataProtectionProvider, ILogger logger) where T : class
    {
        var protector = dataProtectionProvider.CreateProtector("SIPS.Connect.Secrets");
        
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.CanRead && p.CanWrite);

        foreach (var property in properties)
        {
            var value = property.GetValue(options) as string;
            
            if (!string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix))
            {
                try
                {
                    var cipherText = value.Substring(EncryptedPrefix.Length);
                    var decrypted = protector.Unprotect(cipherText);
                    property.SetValue(options, decrypted);
                    logger.LogDebug("Decrypted configuration property: {Type}.{Property}", typeof(T).Name, property.Name);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to decrypt configuration property: {Type}.{Property}", typeof(T).Name, property.Name);
                    throw new InvalidOperationException($"Failed to decrypt {typeof(T).Name}.{property.Name}", ex);
                }
            }
        }

        return options;
    }

    /// <summary>
    /// Check if a value is encrypted
    /// </summary>
    public static bool IsEncrypted(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix);
    }
}
