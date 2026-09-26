using System.Security.Claims;
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

        var providedKey = keyHdr.First();
        var providedSecret = secHdr.First();
        var key = _keys.Keys.FirstOrDefault(k => k.Key == providedKey);

        if (key != null && key.Secret == providedSecret)
        {
            var cfgName = key.Name;
            var cfgSecret = key.Secret;
            var cfgKey = key.Key;

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, cfgName),
                new("Type", "ApiKey"),
                new(ClaimTypes.Role, Gateway),
                new(ClaimTypes.Role, QR),
                new(ClaimTypes.Role, ManageTransactions),
                new(ClaimTypes.Role, ManageMassages)
            };
            // Narrow, per-key additional roles (e.g. RecallClose) on top of the fixed baseline above; empty by
            // default, so a key gets nothing beyond the baseline unless explicitly configured with ApiKey.Roles.
            claims.AddRange(key.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
            var identity = new ClaimsIdentity(claims, ApiKeyDefaults.AuthenticationScheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyDefaults.AuthenticationScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API key or secret"));
    }
}