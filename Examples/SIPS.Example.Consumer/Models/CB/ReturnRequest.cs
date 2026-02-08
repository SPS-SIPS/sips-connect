namespace SIPS.Example.Consumer.Models.CB;

public class ReturnRequest
{
    public string TxId { get; set; } = string.Empty;
    public string EndToEndId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? AdditionalInfo { get; set; }
    public string? ReturnId { get; set; }
    public string? Agent { get; set; }
}
