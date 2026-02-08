namespace SIPS.Example.Consumer.Models;

public sealed class AccountDto {
    public long Id { get; set; }
    public string? IBAN { get; set; }
    public string? CustomerName { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Currency { get; set; }
    public decimal Balance { get; set; }
    public bool Active { get; set; } = true;
    public string? WalletId { get; set; }
}