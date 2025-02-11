using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SIPS.PostgreSQL.Models;
public class ISOMessageStatus
{
    public int Id { get; set; }
    public TransactionStatus Status { get; set; }
    public string? Reason { get; set; }
    public string? AdditionalInfo { get; set; }
    public DateTimeOffset Date { get; set; }
    public byte[] Message { get; set; } = null!;
    public byte[]? Response { get; set; }
    public ISOMessage ISOMessage { get; set; } = null!;
    public int ISOMessageId { get; set; }
}

public sealed class ISOMessageStatusConfiguration : IEntityTypeConfiguration<ISOMessageStatus>
{
    public void Configure(EntityTypeBuilder<ISOMessageStatus> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Date)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>();

        builder.Property(e => e.Message)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(e => e.Response)
            .HasColumnType("bytea");

        builder.HasOne(e => e.ISOMessage)
            .WithMany(e => e.Statuses)
            .HasForeignKey(e => e.ISOMessageId)
            .IsRequired();
    }
}
