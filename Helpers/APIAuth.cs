namespace SIPS.Connect.Helpers;
public static class APIAuth
{
    public static bool IsApiAuthorized(this HttpRequest Request, IConfiguration configuration)
    {
        var apiKeyValue = configuration["API:Key"] ?? throw new ArgumentNullException("API:Key not found!");
        var apiSecretValue = configuration["API:Secret"] ?? throw new ArgumentNullException("API:Key not found!");

        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (auth == null) return false;
        var authValue = auth.ToString().Split(" ")[1];
        var authParts = authValue.Split(":");
        if (authParts.Length != 2) return false;
        return authParts[0] == apiKeyValue && authParts[1] == apiSecretValue;
    }
}
