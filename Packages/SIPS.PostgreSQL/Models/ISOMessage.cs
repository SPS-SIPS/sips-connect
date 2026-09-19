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
    public string? BusinessService { get; set; }
    public string MsgDefIdr { get; set; } = string.Empty;
    public int Round { get; set; } = 1;
    public int CoreBankRetryCount { get; set; }
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
    public byte[]? PapssDecision { get; set; }
    public string? PapssDecisionAdmissionCode { get; set; }
    public DateTimeOffset? PapssDecisionPublishedAt { get; set; }
    public string? PapssDecisionFailureCode { get; set; }
    public DateTimeOffset? PapssDecisionFailedAt { get; set; }
    public string? CoreBankResponse { get; set; }
    public string? ReturnId { get; set; }
    public string? ReturnDedupKey { get; set; }
    public SIPS.ISO20022.Enums.Pacs002Role Pacs002Role { get; set; }
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

        builder.Property(e => e.Pacs002Role)
            .HasConversion<string>();

        builder.Property(e => e.Date)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.Message)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(e => e.Response)
            .HasColumnType("bytea");

        builder.Property(e => e.PapssDecision)
            .HasColumnType("bytea");

        builder.Property(e => e.PapssDecisionPublishedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.PapssDecisionFailedAt)
            .HasColumnType("timestamp with time zone");

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

        // [SAFETY INVARIANT C]: Return de-duplication
        // RtrId = primary key if present; ReturnDedupKey = fallback deterministic key (SmartVista spec)
        builder.HasIndex(e => new { e.MessageType, e.ReturnId })
            .IsUnique()
            .HasDatabaseName("ux_iso_msg_type_rtrid")
            .HasFilter("\"returnid\" IS NOT NULL AND \"returnid\" <> ''");

        builder.HasIndex(e => new { e.MessageType, e.ReturnDedupKey })
            .IsUnique()
            .HasDatabaseName("ux_iso_msg_type_rtr_dedup")
            .HasFilter("\"returndedupkey\" IS NOT NULL AND \"returndedupkey\" <> ''");

        // [SAFETY INVARIANT D]: OrgnlTxId anchor
        builder.HasIndex(e => new { e.TxId })
            .HasDatabaseName("ix_iso_msg_txid_anchor");

        // [PERFORMANCE]: Composite index for TxId + MessageType used heavily by IncomingRecorder Status lookups
        builder.HasIndex(e => new { e.TxId, e.MessageType })
            .HasDatabaseName("ix_iso_msg_txid_msgtype");

        // Secondary index for UETR correlation and audit
        builder.HasIndex(e => e.UETR)
            .HasDatabaseName("ix_iso_msg_uetr");

        builder.HasIndex(e => new { e.BusinessService, e.PapssDecisionPublishedAt, e.PapssDecisionFailedAt, e.Id })
            .HasDatabaseName("ix_iso_msg_papss_decision_pending");
    }
}
