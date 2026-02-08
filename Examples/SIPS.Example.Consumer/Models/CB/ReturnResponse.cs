namespace SIPS.Example.Consumer.Models.CB;
public sealed class ReturnResponse : ReturnRequest
{
    public string Status { get; set; } = string.Empty;
}