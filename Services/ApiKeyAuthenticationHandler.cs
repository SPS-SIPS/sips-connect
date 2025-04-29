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
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderNameKey, out var keyHdr) ||
            !Request.Headers.TryGetValue(ApiKeyDefaults.HeaderNameSecret, out var secHdr))
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

            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.Name, cfgName),
                new Claim("Type", "ApiKey"),
                new Claim(ClaimTypes.Role, Gateway),
                new Claim(ClaimTypes.Role, QR)
                ], ApiKeyDefaults.AuthenticationScheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyDefaults.AuthenticationScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API key or secret"));
    }
}