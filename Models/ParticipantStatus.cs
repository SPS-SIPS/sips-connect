namespace SIPS.Connect.Models;

public class ParticipantStatus
{
    public string InstitutionBic { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public bool IsLive { get; set; }
    public DateTimeOffset LastCheckedAt { get; set; }
    public DateTimeOffset LastStatusChangeAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public string? LastError { get; set; }
}
