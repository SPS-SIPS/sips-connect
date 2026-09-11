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
    public string[] AllowedLocalInstruments { get; set; } = [];
    public PapssCorridorCapability[] AllowedCorridors { get; set; } = [];
    public int ReadinessStaleSeconds { get; set; } = 300;
    public Dictionary<string, PapssParticipantCapability> Participants { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PapssCorridorCapability
{
    public string SenderCountry { get; set; } = string.Empty;
    public string ReceiverCountry { get; set; } = string.Empty;
    public string SenderCurrency { get; set; } = string.Empty;
    public string ReceiverCurrency { get; set; } = string.Empty;
    public string DestinationBic { get; set; } = string.Empty;
    public string[] LocalInstruments { get; set; } = [];
}

public sealed class PapssParticipantCapability
{
    public bool Enabled { get; set; }
    public string Bic { get; set; } = string.Empty;
    public string[] AllowedOperations { get; set; } = [];
    public string? CallbackMappingProfile { get; set; }
    public string? CallbackUrl { get; set; }
}
