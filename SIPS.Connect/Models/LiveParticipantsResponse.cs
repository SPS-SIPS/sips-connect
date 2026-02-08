namespace SIPS.Connect.Models;

public class LiveParticipantsResponse
{
    public List<ParticipantStatus> Data { get; set; } = new();
    public bool Succeeded { get; set; }
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }
}
