namespace SIPS.PostgreSQL.Interfaces;

public interface IIncomingRecorder
{
    Task<ISOMessage> ISOMessageAsync(ISOMessage message, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct);
    Task<Transaction> TransactionAsync(Transaction message, CancellationToken ct);
    Task<ISOMessage> ISOMessageResponseAsync(ISOMessage message, CancellationToken ct);
    Task<ISOMessageStatus> ISOMessageStatusAsync(ISOMessageStatus message, CancellationToken ct);
    Task<ISOMessageStatus> ISOMessageStatusResponseAsync(ISOMessageStatus message, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}