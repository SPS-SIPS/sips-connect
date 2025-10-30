namespace SIPS.Example.Consumer.Models.CB;

public class CompletionNotificationResponse
{
    public string? Reason { get; set; } = string.Empty;
    public string? AdditionalInfo { get; set; }
    public string Status { get; set; }
}