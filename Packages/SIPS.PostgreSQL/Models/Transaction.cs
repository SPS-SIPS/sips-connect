using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SIPS.PostgreSQL.Models;

public class Transaction
{
    public int Id { get; set; }
    public TransactionType Type { get; set; }
    public int ISOMessageId { get; set; }
    public ISOMessage ISOMessage { get; set; } = null!;

    public string FromBIC { get; set; } = default!;
    public string LocalInstrument { get; set; } = default!;
    public string CategoryPurpose { get; set; } = default!;
    public string EndToEndId { get; set; } = default!;
    public string TxId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    // Debtor
    public string DebtorName { get; set; } = default!;
    public string DebtorAccount { get; set; } = default!;
    public string DebtorAddress { get; set; } = string.Empty;
    public string DebtorAccountType { get; set; } = default!;
    public string DebtorAgentBIC { get; set; } = default!;
    public string DebtorIssuer { get; set; } = "C";

    // Creditor
    public string CreditorName { get; set; } = default!;
    public string CreditorAccount { get; set; } = default!;
    public string CreditorAddress { get; set; } = string.Empty;
    public string CreditorAccountType { get; set; } = default!;
    public string CreditorAgentBIC { get; set; } = default!;
    public string CreditorIssuer { get; set; } = "C";

    public string RemittanceInformation { get; set; } = default!;
}



public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Type)
            .HasConversion<string>();

        builder.Property(e => e.Amount)
                .HasPrecision(18, 2);

        builder.HasIndex(e => e.TxId);
        builder.HasIndex(e => e.EndToEndId);

        builder.HasOne(e => e.ISOMessage)
            .WithMany(e => e.Transactions)
            .HasForeignKey(e => e.ISOMessageId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}