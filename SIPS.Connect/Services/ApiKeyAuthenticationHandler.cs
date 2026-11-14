using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SIPS.Connect.Config;
using static SIPS.Connect.KnownRoles;

namespace SIPS.Connect.Services;
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeys keys
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private readonly ApiKeys _keys = keys;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderNameKey, out var keyHdr) && 
            !Request.Headers.TryGetValue("api-key", out keyHdr))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderNameSecret, out var secHdr) &&
            !Request.Headers.TryGetValue("api-secret", out secHdr))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (keyHdr.Count != 1 || secHdr.Count != 1 ||
            string.IsNullOrWhiteSpace(keyHdr[0]) || string.IsNullOrWhiteSpace(secHdr[0]))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key or secret"));

        var providedKey = keyHdr[0];
        var providedSecret = secHdr[0]!;
        var key = _keys.Keys.FirstOrDefault(k => k.Key == providedKey);

        if (key != null && !string.IsNullOrWhiteSpace(key.Secret) &&
            !key.Secret.StartsWith("ENCRYPTED:", StringComparison.Ordinal) &&
            CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(key.Secret)),
                SHA256.HashData(Encoding.UTF8.GetBytes(providedSecret))))
        {
            var cfgName = key.Name;

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, cfgName),
                new("Type", "ApiKey")
            };
            if (key.IncludeDefaultRoles != false)
                claims.AddRange(new[] { Gateway, QR, ManageTransactions, ManageMassages }
                    .Select(role => new Claim(ClaimTypes.Role, role)));
            // Existing keys keep their baseline; restricted identities can opt out explicitly.
            claims.AddRange(key.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
            var identity = new ClaimsIdentity(claims, ApiKeyDefaults.AuthenticationScheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyDefaults.AuthenticationScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API key or secret"));
    }
}
