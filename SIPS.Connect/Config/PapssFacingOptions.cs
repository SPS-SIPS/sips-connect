namespace SIPS.Connect.Config;

public sealed class PapssFacingOptions
{
    public const string SectionName = "PapssFacing";
    public bool Enabled { get; set; }
    public string IsoIngressUrl { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string RemoteWpSipsIdentity { get; set; } = string.Empty;
    public string SecurityProfile { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int MaximumResponseBytes { get; set; } = 2_000_000;
    public string[] AllowedHosts { get; set; } = [];
    public PapssSpsPolicy SpsPolicy { get; set; } = new();
    public int ReadinessStaleSeconds { get; set; } = 300;
    public string LocalCountry { get; set; } = string.Empty;
    public string[] SendingCurrencies { get; set; } = [];
    public string? CallbackMappingProfile { get; set; }
    public string? CallbackUrl { get; set; }
}

public sealed class PapssSpsPolicy
{
    public string[] AllowedLocalInstruments { get; set; } = [];
}
