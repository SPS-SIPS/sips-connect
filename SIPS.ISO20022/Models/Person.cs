namespace SIPS.ISO20022.Models;

public class Person
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string Issuer { get; set; } = "C";
    public string AgentBIC { get; set; } = string.Empty;
}