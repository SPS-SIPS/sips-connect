namespace SIPS.Example.Consumer.Models.CB;
public class StatusRequest
{
    public string EndToEndId { get; set; } = string.Empty;
    public string TxId { get; set; } = string.Empty;
    public string Agent { get; set; } = string.Empty;
}
