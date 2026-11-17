using System.Xml;
using System.Xml.Linq;

namespace SIPS.Connect.Config;

public static class SecurityConfiguration
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment()) return;

        if (configuration.GetValue<bool>("Xades:WithoutPKI") || configuration.GetValue<bool>("Xades__WithoutPKI"))
            throw new InvalidOperationException("PKI is required outside Development.");
        if (!OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(configuration["DataProtection:CertificatePath"]))
            throw new InvalidOperationException("DataProtection:CertificatePath is required outside Development on this platform.");

        foreach (var field in new[] { "Core:BaseUrl", "Core:PublicKeysRepUrl", "Core:CallbackTokenEndpoint", "ISO20022:SIPS",
            "ISO20022:Verification", "ISO20022:Transfer", "ISO20022:Return", "ISO20022:Status", "ISO20022:CompletionNotification", "PapssFacing:IsoIngressUrl", "PapssFacing:CallbackUrl" })
        {
            var value = configuration[field];
            if (!string.IsNullOrWhiteSpace(value) && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)))
                throw new InvalidOperationException($"{field} must be an HTTPS URL without embedded credentials outside Development.");
        }
        if (!string.Equals(configuration["Keycloak:Realm:Protocol"], "https", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The identity-provider protocol must be HTTPS outside Development.");

        static bool MissingOrDefault(string? value) => string.IsNullOrWhiteSpace(value) || value.Contains('<') ||
            new[] { "123", "admin", "postgres", "password", "changeme", "api_key", "api_secret", "change-me", "change_me", "replace-me", "replace_me" }.Contains(value, StringComparer.OrdinalIgnoreCase);
        if (configuration.GetValue<bool>("Core:CallbackAuthEnabled"))
        {
            if (string.IsNullOrWhiteSpace(configuration["Core:CallbackTokenEndpoint"]) ||
                MissingOrDefault(configuration["Core:CallbackClientId"]) || MissingOrDefault(configuration["Core:CallbackClientSecret"]))
                throw new InvalidOperationException("Production callback OAuth requires a token endpoint and nondefault client credentials.");
        }
        else if (MissingOrDefault(configuration["ISO20022:Key"]) || MissingOrDefault(configuration["ISO20022:Secret"]))
            throw new InvalidOperationException("Production bank callbacks require nondefault API credentials or configured OAuth authentication.");

        var connection = configuration.GetConnectionString("db");
        connection = connection?.Replace("{{PASSWORD}}", Environment.GetEnvironmentVariable("POSTGRESQL_PASSWORD"));
        if (!string.IsNullOrWhiteSpace(connection) && MissingOrDefault(new Npgsql.NpgsqlConnectionStringBuilder(connection).Password))
            throw new InvalidOperationException("Production requires a nondefault database password.");

        var switchPins = configuration.GetSection("Xades:DomesticSwitchCertificateSha256").Get<string[]>() ?? [];
        if (switchPins.Length == 0 || switchPins.Any(pin => pin.Length != 64 || !pin.All(Uri.IsHexDigit)))
            throw new InvalidOperationException("Xades:DomesticSwitchCertificateSha256 requires valid SHA-256 switch certificate fingerprints outside Development.");

        var directory = configuration["DataProtection:KeyDirectory"] ?? "./keys";
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "key-*.xml"))
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            if (XDocument.Load(reader).Descendants().Any(element => element.Name.LocalName == "masterKey"))
                throw new InvalidOperationException("The Data Protection directory contains unencrypted keys. Migrate the existing key ring securely before starting outside Development.");
        }
    }
}
