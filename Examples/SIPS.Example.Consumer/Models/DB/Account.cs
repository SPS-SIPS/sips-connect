using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SIPS.Example.Consumer.Models.DB;
public sealed class Account
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }
    public string? IBAN { get; set; }
    public string? CustomerName { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Currency { get; set; }
    public decimal Balance { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedOn { get; set; }
    public string? LastModifiedBy { get; set; }
    public DateTimeOffset? LastModifiedOn { get; set; }
    public string? WalletId { get; set; }
    public bool Active { get; set; } = true;
}