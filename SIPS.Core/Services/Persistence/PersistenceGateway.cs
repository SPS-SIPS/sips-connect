using System.Threading;
using System.Threading.Tasks;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;

namespace SIPS.Core.Services.Persistence;

public interface IPersistenceGateway
{
    Task<ISOMessage> RecordISOMessageAsync(ISOMessage message, CancellationToken ct);
    Task<ISOMessageStatus> RecordISOMessageStatusAsync(ISOMessageStatus status, CancellationToken ct);
    Task ISOMessageResponseAsync(ISOMessage message, CancellationToken ct);
    Task ISOMessageStatusResponseAsync(ISOMessageStatus status, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageWithTransactionsByTxIdAsync(string txId, CancellationToken ct);
}

public sealed class PersistenceGateway(IIncomingRecorder record) : IPersistenceGateway
{
    private readonly IIncomingRecorder _record = record;

    public Task<ISOMessage> RecordISOMessageAsync(ISOMessage message, CancellationToken ct)
        => _record.ISOMessageAsync(message, ct);

    public Task<ISOMessageStatus> RecordISOMessageStatusAsync(ISOMessageStatus status, CancellationToken ct)
        => _record.ISOMessageStatusAsync(status, ct);

    public Task ISOMessageResponseAsync(ISOMessage message, CancellationToken ct)
        => _record.ISOMessageResponseAsync(message, ct);

    public Task ISOMessageStatusResponseAsync(ISOMessageStatus status, CancellationToken ct)
        => _record.ISOMessageStatusResponseAsync(status, ct);

    public Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct)
        => _record.GetISOMessageByTxIdAsync(txId, ct);

    public Task<ISOMessage?> GetISOMessageWithTransactionsByTxIdAsync(string txId, CancellationToken ct)
        => _record.GetISOMessageWithTransactionsByTxIdAsync(txId, ct);
}
