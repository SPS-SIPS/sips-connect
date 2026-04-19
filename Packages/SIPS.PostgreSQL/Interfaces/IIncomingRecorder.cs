namespace SIPS.PostgreSQL.Interfaces;

public interface IIncomingRecorder
{
    Task<ISOMessage> ISOMessageAsync(ISOMessage message, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageByIdAsync(int id, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageByReturnIdAsync(string returnId, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageWithTransactionsByTxIdAsync(string txId, CancellationToken ct);
    Task<List<ISOMessage>> GetISOMessagesByStatusAsync(TransactionStatus status, CancellationToken ct);
    Task<Transaction> TransactionAsync(Transaction message, CancellationToken ct);
    Task<ISOMessage> ISOMessageResponseAsync(ISOMessage message, CancellationToken ct);
    Task<ISOMessageStatus> ISOMessageStatusAsync(ISOMessageStatus message, CancellationToken ct);
    Task<ISOMessageStatus> ISOMessageStatusResponseAsync(ISOMessageStatus message, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageByTxIdAndTypeAsync(string txId, ISOMessageType type, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageByMsgIdAndTypeAsync(string msgId, ISOMessageType type, CancellationToken ct);
    Task<List<ISOMessage>> GetISOMessagesByUETRAndTypeAsync(string uetr, ISOMessageType type, CancellationToken ct);
    Task<List<ISOMessage>> GetISOMessagesByOriginalTxIdAndTypeAsync(string orgnlTxId, ISOMessageType type, CancellationToken ct);
    Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingTransactionAsync(ISOMessage entity, CancellationToken ct);
    Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingReturnAsync(ISOMessage entity, CancellationToken ct);
    Task<(ISOMessage record, SIPS.PostgreSQL.Enums.DedupOutcome outcome, string? duplicateBy)> TryRecordIncomingVerificationAsync(ISOMessage entity, CancellationToken ct);
    Task<int> AppendAuditLedgerEventAsync(int isoMessageId, object ledgerEvent, uint xmin, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}