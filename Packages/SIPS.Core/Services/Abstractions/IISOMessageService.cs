using System.Threading;
using System.Threading.Tasks;
using SIPS.PostgreSQL.Models;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Helpers;
using SIPS.PostgreSQL.Enums;

namespace SIPS.Core.Services.Abstractions;

public interface IISOMessageService
{
    Task<ISOMessage> RecordIncomingVerificationAsync(
        PayeeVerificationBuilder.Request request,
        string rawXml,
        CancellationToken ct);

    Task<(ISOMessage record, SIPS.PostgreSQL.Enums.DedupOutcome outcome, string? duplicateBy)>
    TryRecordIncomingVerificationAsync(
        ISOMessage entity,
        CancellationToken ct);

    Task PersistResponseAsync(
        ISOMessage isoMessage,
        TransactionStatus status,
        string reason,
        string? additionalInfo,
        string response,
        CancellationToken ct);

    Task<ISOMessageStatus> RecordIncomingStatusAsync(
        ISOMessage isoMessage,
        string rawXml,
        SIPS.ISO20022.Enums.Pacs002Role role,
        string? msgId,
        CancellationToken ct);

    Task PersistStatusResponseAsync(
        ISOMessageStatus isoMessageStatus,
        TransactionStatus status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct);

    // Transactions (pacs.008)
    Task<ISOMessage> RecordIncomingTransactionAsync(
        PaymentRequestBuilder.Request request,
        string rawXml,
        CancellationToken ct);

    Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingTransactionAsync(
        PaymentRequestBuilder.Request request,
        string rawXml,
        CancellationToken ct);

    Task<ISOMessage?> GetInboundMessageByTxIdAsync(
        string txId,
        CancellationToken ct);

    Task PersistTransactionResponseAsync(
        ISOMessage isoMessage,
        TransactionStatus status,
        string reason,
        string? additionalInfo,
        string responseXml,
        string txId,
        string endToEndId,
        CancellationToken ct);

    // Returns (pacs.004)
    Task<ISOMessage> RecordIncomingReturnAsync(
        ReturnPaymentRequestBuilder.Request request,
        string rawXml,
        CancellationToken ct);

    Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingReturnAsync(
        ReturnPaymentRequestBuilder.Request request,
        string rawXml,
        string? returnDedupKey,
        CancellationToken ct);

    Task PersistReturnResponseAsync(
        ISOMessage isoMessage,
        string status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct,
        TransactionStatus? internalStatus = null);

    /// <summary>
    /// Marks an ISOMessage as CheckStatus for SAF processing.
    /// Used when pacs.002 is not received within SLA or CoreBank callback fails.
    /// </summary>
    Task MarkForCheckStatusAsync(
        ISOMessage isoMessage,
        string reason,
        CancellationToken ct);

    /// <summary>
    /// Finalizes an ISOMessage that has exceeded max SAF retries.
    /// Marks it as Failed with appropriate reason.
    /// </summary>
    Task FinalizeAfterMaxRetriesAsync(
        ISOMessage isoMessage,
        string reason,
        CancellationToken ct);

    /// <summary>
    /// Appends a structured audit event to the CoreBankResponse (auditLedger).
    /// Uses optimistic concurrency (xmin) to ensure no lost updates.
    /// </summary>
    Task<bool> AppendAuditLedgerEventAsync(
        int isoMessageId,
        object ledgerEvent,
        CancellationToken ct);
}
