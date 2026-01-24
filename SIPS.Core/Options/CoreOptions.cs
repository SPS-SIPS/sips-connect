namespace SIPS.Core.Options;
public class CoreOptions
{
    public string BaseUrl { get; set; } = "";
    public string PublicKeysRepUrl { get; set; } = "";
    public string LoginEndpoint { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string BIC { get; set; } = "";
    public string SAFExpression { get; set; } = "";
    public int SAFPage { get; set; } = 40;
    public string SAFTimeZoneInfo { get; set; } = "";
    public int SAFMaxRetries { get; set; } = 10;
    public bool IncludeIdempotencyHeaders { get; set; } = false;
    public int HttpTimeoutSeconds { get; set; } = 15;
    public int DbPersistTimeoutSeconds { get; set; } = 10;
    public bool IncludeCoreBankOnListing { get; set; } = false;
}