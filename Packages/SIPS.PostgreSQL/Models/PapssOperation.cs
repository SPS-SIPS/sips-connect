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

    // ---- Payments / returns / status enquiries (Phase 2) ----------------------------------------
    /// <summary>GrpHdr MsgId of the pacs.008/pacs.004/pacs.028 this participant sent (OUTBOUND), or of the message received (INBOUND).
    /// For OUTBOUND payments and returns this is the value the gateway echoes as pacs.002 OrgnlMsgId.</summary>
    public string? MsgId { get; set; }
    public string? ReturnId { get; set; }
    /// <summary>RETURN -> the payment it returns; STATUS_ENQUIRY -> the payment it asks about; RECALL -> the payment it recalls.</summary>
    public Guid? OriginalOperationId { get; set; }
    public PapssOperation? OriginalOperation { get; set; }
    /// <summary>RETURN / STATUS_ENQUIRY: the original payment TxId / EndToEndId as sent or received.</summary>
    public string? OriginalTxId { get; set; }
    public string? OriginalEndToEndId { get; set; }
    public decimal? Amount { get; set; }
    public string? LocalInstrument { get; set; }
    /// <summary>Raw ISO status currently in effect (ACCP, ACSP, ACSC, PDNG, RJCT). Every received status is kept as an event.</summary>
    public string? PaymentStatus { get; set; }
    /// <summary>Reason code carried by the pacs.002 in effect (StsRsnInf/Rsn), kept apart from gateway/core-bank ReasonCode.</summary>
    public string? StatusReasonCode { get; set; }
    public DateTimeOffset? StatusAt { get; set; }
    /// <summary>A different final status arrived after a final one. Operator attention; the first final status is kept.</summary>
    public bool StatusConflict { get; set; }
    /// <summary>SHA-256 of the bank request's business content: same TxId/ReturnId + different content = DUPLICATE_CONFLICT.</summary>
    public string? RequestFingerprint { get; set; }
    /// <summary>INBOUND payment: the isomessages row holding the bank decision and the PAPSS decision outbox state.</summary>
    public int? IsoMessageId { get; set; }

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

    // Status history (pacs.002 / pacs.004 events). Null for verification events.
    public string? Status { get; set; }
    public string? ReasonCode { get; set; }
    /// <summary>MSG_ID, TX_ID, NONE or MISMATCH (see PapssCorrelation).</summary>
    public string? Correlation { get; set; }
    /// <summary>APPLIED, NOT_ADVANCING, DUPLICATE_FINAL, CONFLICT, UNKNOWN_STATUS or UNCORRELATED (see PapssEventDisposition).</summary>
    public string? Disposition { get; set; }
    public string? OriginalMessageId { get; set; }
    public string? OriginalMessageType { get; set; }
    public string? OriginalTxId { get; set; }
    public string? OriginalEndToEndId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Note { get; set; }

    // Provenance (gateway SplmtryData PapssProvenance). RawEvidenceReference links the event to the raw signed PAPSS message the
    // gateway keeps (SHA-256); FieldProvenance is the per-field origin map as JSON ({"path":"SOURCE"}).
    /// <summary>NETWORK_REPORTED, LOCAL_RECONSTRUCTION (not a PAPSS-reported amount) or UNSPECIFIED_LEGACY. Null for pacs.008/verification events.</summary>
    public string? AmountSource { get; set; }
    /// <summary>pacs.004 only: NETWORK_REPORTED, LOCAL_RECONSTRUCTION (gateway took it from the original payment) or UNSPECIFIED_LEGACY.</summary>
    public string? CategoryPurposeSource { get; set; }
    public string? RawEvidenceReference { get; set; }
    public string? FieldProvenance { get; set; }
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
    /// <summary>The partial-index predicate of ux_papss_op_open_recall (RECALL_PENDING and RECALL_ACCEPTED_BY_PAPSS are open).</summary>
    public const string OpenRecallFilter = "operation = 'RECALL' AND originaloperationid IS NOT NULL AND papssoutcome IN ('RECALL_PENDING', 'RECALL_ACCEPTED_BY_PAPSS')";

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

        builder.Property(x => x.MsgId).HasMaxLength(128);
        builder.Property(x => x.ReturnId).HasMaxLength(128);
        builder.Property(x => x.OriginalTxId).HasMaxLength(128);
        builder.Property(x => x.OriginalEndToEndId).HasMaxLength(128);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,5)");
        builder.Property(x => x.LocalInstrument).HasMaxLength(35);
        builder.Property(x => x.PaymentStatus).HasMaxLength(8);
        builder.Property(x => x.StatusReasonCode).HasMaxLength(64);
        builder.Property(x => x.StatusAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64);
        builder.HasOne(x => x.OriginalOperation).WithMany().HasForeignKey(x => x.OriginalOperationId).OnDelete(DeleteBehavior.SetNull);

        // [SAFETY INVARIANT]: one payment per direction and TxId (bank TxId idempotency for OUTBOUND,
        // redelivery de-duplication for INBOUND), one return per direction and ReturnId.
        builder.HasIndex(x => new { x.Direction, x.Operation, x.TxId }).IsUnique()
            .HasFilter("operation = 'PAYMENT' AND txid IS NOT NULL").HasDatabaseName("ux_papss_op_payment_txid");
        builder.HasIndex(x => new { x.Direction, x.ReturnId }).IsUnique()
            .HasFilter("operation = 'RETURN' AND returnid IS NOT NULL").HasDatabaseName("ux_papss_op_return_id");
        // [SAFETY INVARIANT]: at most one OPEN recall per payment. PAPSS answers to a camt.056 (pacs.002, camt.029, pacs.004) do
        // not all carry our recall id, so an answer can only be attributed when the payment has a single open recall.
        builder.HasIndex(x => x.OriginalOperationId, "ux_papss_op_open_recall").IsUnique()
            .HasFilter(OpenRecallFilter).HasDatabaseName("ux_papss_op_open_recall");
        builder.HasIndex(x => x.MsgId).HasDatabaseName("ix_papss_op_msg_id");
        builder.HasIndex(x => x.TxId).HasDatabaseName("ix_papss_op_tx_id");
        builder.HasIndex(x => x.EndToEndId).HasDatabaseName("ix_papss_op_end_to_end_id");
        builder.HasIndex(x => x.OriginalOperationId).HasDatabaseName("ix_papss_op_original");
        builder.HasIndex(x => x.IsoMessageId).HasDatabaseName("ix_papss_op_iso_message");
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

        builder.Property(x => x.Status).HasMaxLength(8);
        builder.Property(x => x.ReasonCode).HasMaxLength(64);
        builder.Property(x => x.Correlation).HasMaxLength(16);
        builder.Property(x => x.Disposition).HasMaxLength(32);
        builder.Property(x => x.OriginalMessageId).HasMaxLength(128);
        builder.Property(x => x.OriginalMessageType).HasMaxLength(32);
        builder.Property(x => x.OriginalTxId).HasMaxLength(128);
        builder.Property(x => x.OriginalEndToEndId).HasMaxLength(128);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,5)");
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.Note).HasMaxLength(512);
        builder.Property(x => x.AmountSource).HasMaxLength(32);
        builder.Property(x => x.CategoryPurposeSource).HasMaxLength(32);
        builder.Property(x => x.RawEvidenceReference).HasMaxLength(128);
        builder.Property(x => x.FieldProvenance).HasColumnType("text");
        // Operators list uncorrelated / conflicting status events.
        builder.HasIndex(x => new { x.EventType, x.Disposition }).HasDatabaseName("ix_papss_event_type_disposition");
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
