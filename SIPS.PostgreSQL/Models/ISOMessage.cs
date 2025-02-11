using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SIPS.PostgreSQL.Models;
public class ISOMessage
{
    public int Id { get; set; }
    public ISOMessageType MessageType { get; set; }
    public TransactionStatus Status { get; set; }
    public int Round { get; set; } = 1;
    public string? TxId { get; set; }
    public string? EndToEndId { get; set; }
    public string? Reason { get; set; }
    public string? AdditionalInfo { get; set; }
    public DateTimeOffset Date { get; set; }
    public string FromBIC { get; set; } = null!;
    public string ToBIC { get; set; } = null!;
    public byte[] Message { get; set; } = null!;
    public byte[]? Response { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = [];
    public ICollection<ISOMessageStatus> Statuses { get; set; } = [];
}

public sealed class ISOMessageConfiguration : IEntityTypeConfiguration<ISOMessage>
{
    public void Configure(EntityTypeBuilder<ISOMessage> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.MessageType)
            .HasConversion<string>();

        builder.Property(e => e.Status)
            .HasConversion<string>();

        builder.Property(e => e.Date)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.Message)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(e => e.Response)
            .HasColumnType("bytea");

    }
}
