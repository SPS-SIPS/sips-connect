namespace SIPS.Connect.Models;

public class BalanceStatus
{
    public string Bic { get; set; } = string.Empty;
    public string ParticipantName { get; set; } = string.Empty;
    public decimal? LastKnownBalance { get; set; }
    public string CurrentZone { get; set; } = string.Empty;
    public DateTimeOffset? LastZoneChangeAt { get; set; }
    public DateTimeOffset? LastAlertSentAt { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public string Currency { get; set; } = string.Empty;
}
