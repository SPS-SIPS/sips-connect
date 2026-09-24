using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SIPS.PostgreSQL.Models;

/// <summary>
/// One PAPSS operation in either direction. The three state dimensions (gateway admission,
/// PAPSS business outcome and bank delivery) are deliberately independent.
/// </summary>
public class PapssOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PapssDirection Direction { get; set; }
    public PapssOperationType Operation { get; set; }
    /// <summary>OUTBOUND: the requestMessageId returned to the bank. INBOUND: the PAPSS source message id (AppHdr BizMsgIdr).</summary>
    public string RequestMessageId { get; set; } = string.Empty;
    public string? VerificationId { get; set; }
    public string? EndToEndId { get; set; }
    public string? TxId { get; set; }
    public string? CounterpartyBic { get; set; }
    public string? AccountId { get; set; }
    public string? AccountType { get; set; }

    public PapssGatewayState GatewayState { get; set; }
    public PapssOutcome PapssOutcome { get; set; }
    public PapssDeliveryState BankDeliveryState { get; set; }

    public bool? Verified { get; set; }
    public string? AccountName { get; set; }
    public string? Currency { get; set; }
    public string? Reason { get; set; }
    public string? AdditionalInfo { get; set; }
    /// <summary>WP-SIPS admi.002 admission reason code (e.g. RECEIVED_AND_DURABLY_ADMITTED, DUPLICATE_CONFLICT).</summary>
    public string? AdmissionCode { get; set; }
    /// <summary>PAPSS / gateway / core-bank reason code for a rejection or failure.</summary>
    public string? ReasonCode { get; set; }

    public byte[]? SignedRequest { get; set; }
    public byte[]? SignedResponse { get; set; }

    /// <summary>Creation time carried by the source message (AppHdr CreDt).</summary>
    public DateTimeOffset? SourceCreatedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Only computed when a deadline is configured (PAPSS timeout rules are unresolved).</summary>
    public DateTimeOffset? DeadlineAt { get; set; }

    public uint xmin { get; private set; }
}

public class PapssOperationEvent
{
    public long Id { get; set; }
    public Guid? OperationId { get; set; }
    public PapssOperation? Operation { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    /// <summary>AppHdr BizMsgIdr of the received message: the de-duplication key per event type.</summary>
    public string SourceMessageId { get; set; } = string.Empty;
    public byte[] RawXml { get; set; } = [];
    public DateTimeOffset ReceivedAt { get; set; }

    // Bank push outbox.
    public PapssDeliveryState PushState { get; set; }
    public int PushAttempts { get; set; }
    public DateTimeOffset? PushNextAttemptAt { get; set; }
    public string? PushLastError { get; set; }
    public DateTimeOffset? PushDeliveredAt { get; set; }
}

/// <summary>Gateway-bound signed reply (e.g. acmt.024 answering an inbound acmt.023), re-submitted byte-identical on retry.</summary>
public class PapssOutboundResponse
{
    public long Id { get; set; }
    public Guid OperationId { get; set; }
    public PapssOperation? Operation { get; set; }
    public string BizMsgIdr { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public byte[] SignedXml { get; set; } = [];
    public PapssResponseState State { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string? AdmissionCode { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? AdmittedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Stores enum values as UPPER_SNAKE_CASE strings (SubmissionUnknown -> SUBMISSION_UNKNOWN).</summary>
public sealed class UpperSnakeEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => ToUpperSnake(value.ToString()),
    value => (TEnum)Enum.Parse(typeof(TEnum), value.Replace("_", string.Empty), true))
    where TEnum : struct, Enum
{
    public static string ToUpperSnake(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) builder.Append('_');
            builder.Append(char.ToUpperInvariant(name[i]));
        }
        return builder.ToString();
    }

    public static string Of(TEnum value) => ToUpperSnake(value.ToString());
}

public sealed class PapssOperationConfiguration : IEntityTypeConfiguration<PapssOperation>
{
    public void Configure(EntityTypeBuilder<PapssOperation> builder)
    {
        builder.ToTable("papss_operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Direction).HasConversion(new UpperSnakeEnumConverter<PapssDirection>()).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Operation).HasConversion(new UpperSnakeEnumConverter<PapssOperationType>()).HasMaxLength(32).IsRequired();
        builder.Property(x => x.GatewayState).HasConversion(new UpperSnakeEnumConverter<PapssGatewayState>()).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PapssOutcome).HasConversion(new UpperSnakeEnumConverter<PapssOutcome>()).HasMaxLength(32).IsRequired();
        builder.Property(x => x.BankDeliveryState).HasConversion(new UpperSnakeEnumConverter<PapssDeliveryState>()).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestMessageId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SignedRequest).HasColumnType("bytea");
        builder.Property(x => x.SignedResponse).HasColumnType("bytea");
        builder.Property(x => x.SourceCreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ReceivedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeadlineAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.xmin).IsRowVersion();

        // One operation per direction and request message id. INBOUND keys on the PAPSS source
        // message id, OUTBOUND on the id we generated, so the two namespaces must not collide.
        builder.HasIndex(x => new { x.Direction, x.RequestMessageId }).IsUnique().HasDatabaseName("ux_papss_op_direction_request_msg");
        builder.HasIndex(x => x.VerificationId).HasDatabaseName("ix_papss_op_verification_id");
        builder.HasIndex(x => x.CompletedAt).HasDatabaseName("ix_papss_op_completed_at");
    }
}

public sealed class PapssOperationEventConfiguration : IEntityTypeConfiguration<PapssOperationEvent>
{
    public void Configure(EntityTypeBuilder<PapssOperationEvent> builder)
    {
        builder.ToTable("papss_operation_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.MessageType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceMessageId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RawXml).HasColumnType("bytea").IsRequired();
        builder.Property(x => x.ReceivedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.PushState).HasConversion(new UpperSnakeEnumConverter<PapssDeliveryState>()).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PushNextAttemptAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.PushDeliveredAt).HasColumnType("timestamp with time zone");
        builder.HasOne(x => x.Operation).WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.SetNull);

        // [SAFETY INVARIANT]: a redelivered callback (same AppHdr BizMsgIdr) is stored and pushed once.
        builder.HasIndex(x => new { x.EventType, x.SourceMessageId }).IsUnique().HasDatabaseName("ux_papss_event_type_source_msg");
        builder.HasIndex(x => new { x.PushState, x.PushNextAttemptAt }).HasDatabaseName("ix_papss_event_push_due");
        builder.HasIndex(x => x.OperationId).HasDatabaseName("ix_papss_event_operation");
    }
}

public sealed class PapssOutboundResponseConfiguration : IEntityTypeConfiguration<PapssOutboundResponse>
{
    public void Configure(EntityTypeBuilder<PapssOutboundResponse> builder)
    {
        builder.ToTable("papss_outbound_responses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.BizMsgIdr).HasMaxLength(64).IsRequired();
        builder.Property(x => x.MessageType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SignedXml).HasColumnType("bytea").IsRequired();
        builder.Property(x => x.State).HasConversion(new UpperSnakeEnumConverter<PapssResponseState>()).HasMaxLength(32).IsRequired();
        builder.Property(x => x.NextAttemptAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.SubmittedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.AdmittedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.HasOne(x => x.Operation).WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.BizMsgIdr).IsUnique().HasDatabaseName("ux_papss_response_biz_msg_idr");
        // One reply per operation: concurrent redeliveries cannot queue two different replies.
        builder.HasIndex(x => x.OperationId).IsUnique().HasDatabaseName("ux_papss_response_operation");
        builder.HasIndex(x => new { x.State, x.NextAttemptAt }).HasDatabaseName("ix_papss_response_due");
    }
}
