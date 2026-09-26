namespace SIPS.Connect.Config;

public sealed class ApiKey
{
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    /// <summary>
    /// Additional roles granted only to this key, on top of the fixed baseline every API key gets
    /// (ApiKeyAuthenticationHandler: Gateway, QR, ManageTransactions, ManageMassages). Empty by default: an API key
    /// gets nothing beyond the baseline unless explicitly listed here. Use this for a narrow, per-party machine
    /// capability (for example KnownRoles.RecallClose) rather than granting a broad human role such as Recon.
    /// </summary>
    public List<string> Roles { get; set; } = [];
}