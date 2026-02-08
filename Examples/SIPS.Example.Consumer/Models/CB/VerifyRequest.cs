namespace SIPS.Example.Consumer.Models.CB;

public class VerifyRequest
{
    public string AccountNo { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string Agent { get; set; } = string.Empty;
    public string VerificationId { get; set; } = string.Empty;
}
