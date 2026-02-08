namespace SIPS.Example.Consumer.Models.CB;

public class CompletionNotification
{
    public string TxId { get; set; } = string.Empty;
    public string? EndToEndId { get; set; } = string.Empty;
    public string? Reason { get; set; } = string.Empty;
    public string? AdditionalInfo { get; set; }
    public string? Status { get; set; }
}