namespace SIPS.Example.Consumer.Models.CB;

public class VerifyResponse
{
    public bool IsVerified { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string AccountNo { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
}
