using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using SIPS.PostgreSQL.Enums;

namespace SIPS.PostgreSQL.Models;
public class ISOMessage
{
    public int Id { get; set; }
    public ISOMessageType MessageType { get; set; }
    public TransactionStatus Status { get; set; }
    public string MsgId { get; set; } = string.Empty;
    public string BizMsgIdr { get; set; } = string.Empty;
    public string MsgDefIdr { get; set; } = string.Empty;
    public int Round { get; set; } = 1;
    public string? TxId { get; set; }
    public string? UETR { get; set; }
    public string? EndToEndId { get; set; }
    public string? Reason { get; set; }
    public string? AdditionalInfo { get; set; }
    public DateTimeOffset Date { get; set; }
    public string FromBIC { get; set; } = null!;
    public string ToBIC { get; set; } = null!;
    public byte[] Message { get; set; } = null!;
    public byte[]? Response { get; set; }
    public string? CoreBankResponse { get; set; }
    public string? ReturnId { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = [];
    public ICollection<ISOMessageStatus> Statuses { get; set; } = [];
    public uint xmin { get; private set; }
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

        builder.Property(e => e.CoreBankResponse)
            .HasColumnType("jsonb");

        builder.Property(e => e.xmin)
            .IsRowVersion();

        // [SAFETY INVARIANT A]: Transaction-level de-duplication
        // TxId = mandatory unique ID for transaction status requests (SmartVista spec)
        builder.HasIndex(e => new { e.MessageType, e.TxId })
            .IsUnique()
            .HasDatabaseName("ux_iso_msg_type_txid")
            // Fix: Use lowercase "txid" because UseLowerCaseNamingConvention() is active
            .HasFilter("\"txid\" IS NOT NULL AND \"txid\" <> ''");

        // [SAFETY INVARIANT B]: Message-level de-duplication
        // MsgId = unique message ID for message check (SmartVista spec)
        builder.HasIndex(e => new { e.MessageType, e.MsgId })
            .IsUnique()
            .HasDatabaseName("ux_iso_msg_type_msgid")
            // Fix: Use lowercase "msgid" because UseLowerCaseNamingConvention() is active
            .HasFilter("\"msgid\" IS NOT NULL AND \"msgid\" <> ''");

        // Secondary index for UETR correlation and audit
        builder.HasIndex(e => e.UETR)
            .HasDatabaseName("ix_iso_msg_uetr");
    }
}
