using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace SIPS.Connect.Tools;

/// <summary>
/// Command-line tool for managing encrypted secrets
/// Usage: dotnet run --project SIPS.Connect -- secrets [command]
/// </summary>
public class SecretManager
{
    private readonly IDataProtector _protector;
    private const string EncryptedPrefix = "ENCRYPTED:";

    public SecretManager()
    {
        // Initialize Data Protection
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo("./keys"))
            .SetApplicationName("SIPS.Connect");

        var services = serviceCollection.BuildServiceProvider();
        var provider = services.GetRequiredService<IDataProtectionProvider>();
        _protector = provider.CreateProtector("SIPS.Connect.Secrets");
    }

    public void Run(string[] args)
    {
        if (args.Length < 2 || args[0] != "secrets")
        {
            ShowHelp();
            return;
        }

        var command = args[1].ToLower();

        try
        {
            switch (command)
            {
                case "encrypt":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: secrets encrypt <value>");
                        return;
                    }
                    EncryptValue(args[2]);
                    break;

                case "decrypt":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: secrets decrypt <encrypted-value>");
                        return;
                    }
                    DecryptValue(args[2]);
                    break;

                case "encrypt-file":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: secrets encrypt-file <path-to-appsettings.json>");
                        return;
                    }
                    EncryptFile(args[2]);
                    break;

                case "decrypt-file":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: secrets decrypt-file <path-to-appsettings.json>");
                        return;
                    }
                    DecryptFile(args[2]);
                    break;

                case "list":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: secrets list <path-to-appsettings.json>");
                        return;
                    }
                    ListSecrets(args[2]);
                    break;

                default:
                    ShowHelp();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }
    }

    private void EncryptValue(string plainText)
    {
        var encrypted = _protector.Protect(plainText);
        var result = $"{EncryptedPrefix}{encrypted}";

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Encrypted value:");
        Console.ResetColor();
        Console.WriteLine(result);
        Console.WriteLine();
        Console.WriteLine("Copy this value to your appsettings.json");
    }

    private void DecryptValue(string encryptedText)
    {
        if (!encryptedText.StartsWith(EncryptedPrefix))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: Value doesn't appear to be encrypted");
            Console.ResetColor();
            return;
        }

        var cipherText = encryptedText.Substring(EncryptedPrefix.Length);
        var decrypted = _protector.Unprotect(cipherText);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠️  SENSITIVE INFORMATION ⚠️");
        Console.ResetColor();
        Console.WriteLine("Decrypted value:");
        Console.WriteLine(decrypted);
    }

    private void EncryptFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return;
        }

        // Create backup
        var backupPath = $"{filePath}.backup.{DateTime.Now:yyyyMMddHHmmss}";
        File.Copy(filePath, backupPath);
        Console.WriteLine($"Created backup: {backupPath}");

        var json = File.ReadAllText(filePath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var secretsToEncrypt = new Dictionary<string, string>();
        FindSecretsToEncrypt(root, "", secretsToEncrypt);

        if (secretsToEncrypt.Count == 0)
        {
            Console.WriteLine("No plain text secrets found");
            return;
        }

        Console.WriteLine($"Found {secretsToEncrypt.Count} secrets to encrypt:");
        foreach (var key in secretsToEncrypt.Keys)
        {
            Console.WriteLine($"  - {key}");
        }

        // Encrypt secrets
        var updatedJson = json;
        foreach (var kvp in secretsToEncrypt)
        {
            var encrypted = _protector.Protect(kvp.Value);
            var encryptedValue = $"{EncryptedPrefix}{encrypted}";
            updatedJson = updatedJson.Replace($"\"{kvp.Value}\"", $"\"{encryptedValue}\"");
        }

        File.WriteAllText(filePath, updatedJson);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Encrypted {secretsToEncrypt.Count} secrets");
        Console.ResetColor();
    }

    private void DecryptFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return;
        }

        // Create backup
        var backupPath = $"{filePath}.backup.{DateTime.Now:yyyyMMddHHmmss}";
        File.Copy(filePath, backupPath);
        Console.WriteLine($"Created backup: {backupPath}");

        var json = File.ReadAllText(filePath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var secretsToDecrypt = new Dictionary<string, string>();
        FindEncryptedSecrets(root, "", secretsToDecrypt);

        if (secretsToDecrypt.Count == 0)
        {
            Console.WriteLine("No encrypted secrets found");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠️  WARNING: This will decrypt {secretsToDecrypt.Count} secrets");
        Console.ResetColor();
        Console.Write("Continue? (yes/no): ");
        var confirm = Console.ReadLine();

        if (confirm?.ToLower() != "yes")
        {
            Console.WriteLine("Cancelled");
            return;
        }

        // Decrypt secrets
        var updatedJson = json;
        foreach (var kvp in secretsToDecrypt)
        {
            var cipherText = kvp.Value.Substring(EncryptedPrefix.Length);
            var decrypted = _protector.Unprotect(cipherText);
            updatedJson = updatedJson.Replace($"\"{kvp.Value}\"", $"\"{decrypted}\"");
        }

        File.WriteAllText(filePath, updatedJson);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Decrypted {secretsToDecrypt.Count} secrets");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠️  Remember to re-encrypt before committing!");
        Console.ResetColor();
    }

    private void ListSecrets(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return;
        }

        var json = File.ReadAllText(filePath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var allSecrets = new Dictionary<string, (string Value, bool IsEncrypted)>();
        FindAllSecrets(root, "", allSecrets);

        if (allSecrets.Count == 0)
        {
            Console.WriteLine("No secrets found");
            return;
        }

        Console.WriteLine($"Found {allSecrets.Count} secrets:");
        Console.WriteLine();

        foreach (var kvp in allSecrets)
        {
            var status = kvp.Value.IsEncrypted ? "🔒 Encrypted" : "⚠️  Plain Text";
            Console.WriteLine($"{status,-15} {kvp.Key}");
        }
    }

    private void FindSecretsToEncrypt(JsonElement element, string path, Dictionary<string, string> secrets)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var newPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}:{property.Name}";

                if (IsSecretField(property.Name) && property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrEmpty(value) && !value.StartsWith(EncryptedPrefix))
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
                    if (!string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix))
                    {
                        secrets[newPath] = value;
                    }
                }

                FindEncryptedSecrets(property.Value, newPath, secrets);
            }
        }
    }

    private void FindAllSecrets(JsonElement element, string path, Dictionary<string, (string, bool)> secrets)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var newPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}:{property.Name}";

                if (IsSecretField(property.Name) && property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        var isEncrypted = value.StartsWith(EncryptedPrefix);
                        secrets[newPath] = (value, isEncrypted);
                    }
                }

                FindAllSecrets(property.Value, newPath, secrets);
            }
        }
    }

    private static bool IsSecretField(string fieldName)
    {
        var secretFields = new[]
        {
            "password", "passphrase", "secret", "key", "token",
            "clientsecret", "apikey", "connectionstring", "privatekey"
        };

        return secretFields.Any(s => fieldName.ToLowerInvariant().Contains(s));
    }

    private void ShowHelp()
    {
        Console.WriteLine("SIPS.Connect Secret Manager");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run --project SIPS.Connect -- secrets [command]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  encrypt <value>                    Encrypt a single value");
        Console.WriteLine("  decrypt <encrypted-value>          Decrypt a single value");
        Console.WriteLine("  encrypt-file <path>                Encrypt all secrets in a file");
        Console.WriteLine("  decrypt-file <path>                Decrypt all secrets in a file");
        Console.WriteLine("  list <path>                        List all secrets in a file");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- secrets encrypt \"MyPassword123\"");
        Console.WriteLine("  dotnet run -- secrets encrypt-file appsettings.json");
        Console.WriteLine("  dotnet run -- secrets list appsettings.json");
    }
}
