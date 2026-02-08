namespace SIPS.ISO20022.Models.DTOs.CB;

public sealed class CBCompletionNotificationResponse
{
    public string Status { get; set; } = default!;
    public string Reason { get; set; } = default!;
    public string AdditionalInfo { get; set; } = default!;
}