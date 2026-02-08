using System.Threading;
using System.Threading.Tasks;
using SIPS.PostgreSQL.Interfaces;
using Microsoft.EntityFrameworkCore;
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Enums;

namespace SIPS.Core.Services.Persistence;

public interface IPersistenceGateway
{
    Task<ISOMessage> RecordISOMessageAsync(ISOMessage message, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageByIdAsync(int id, CancellationToken ct);
    Task<ISOMessageStatus> RecordISOMessageStatusAsync(ISOMessageStatus status, CancellationToken ct);
    Task<ISOMessage> ISOMessageResponseAsync(ISOMessage message, CancellationToken ct);
    Task<ISOMessageStatus> ISOMessageStatusResponseAsync(ISOMessageStatus status, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageWithTransactionsByTxIdAsync(string txId, CancellationToken ct);
    Task<ISOMessage?> GetISOMessageByTxIdAndTypeAsync(string txId, ISOMessageType type, CancellationToken ct);
    Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingTransactionAsync(ISOMessage entity, CancellationToken ct);
    Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingReturnAsync(ISOMessage entity, CancellationToken ct);
    Task<(ISOMessage record, SIPS.PostgreSQL.Enums.DedupOutcome outcome, string? duplicateBy)> TryRecordIncomingVerificationAsync(ISOMessage entity, CancellationToken ct);
    Task<List<ISOMessage>> GetISOMessagesByStatusAsync(TransactionStatus status, CancellationToken ct);
    Task<int> AppendAuditLedgerEventAsync(int isoMessageId, object ledgerEvent, uint xmin, CancellationToken ct);
    Task<List<ISOMessage>> GetISOMessagesByUETRAndTypeAsync(string uetr, ISOMessageType type, CancellationToken ct);
    Task<List<ISOMessage>> GetISOMessagesByOriginalTxIdAndTypeAsync(string orgnlTxId, ISOMessageType type, CancellationToken ct);
    Task<Transaction?> GetTransactionByTxIdAsync(string txId, CancellationToken ct);
}

public sealed class PersistenceGateway(IIncomingRecorder record) : IPersistenceGateway
{
    private readonly IIncomingRecorder _record = record;

    public Task<ISOMessage> RecordISOMessageAsync(ISOMessage message, CancellationToken ct)
        => _record.ISOMessageAsync(message, ct);

    public Task<ISOMessage?> GetISOMessageByIdAsync(int id, CancellationToken ct)
        => _record.GetISOMessageByIdAsync(id, ct);

    public Task<ISOMessageStatus> RecordISOMessageStatusAsync(ISOMessageStatus status, CancellationToken ct)
        => _record.ISOMessageStatusAsync(status, ct);

    public Task<ISOMessage> ISOMessageResponseAsync(ISOMessage message, CancellationToken ct)
        => _record.ISOMessageResponseAsync(message, ct);

    public Task<ISOMessageStatus> ISOMessageStatusResponseAsync(ISOMessageStatus status, CancellationToken ct)
        => _record.ISOMessageStatusResponseAsync(status, ct);

    public Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct)
        => _record.GetISOMessageByTxIdAsync(txId, ct);

    public Task<ISOMessage?> GetISOMessageWithTransactionsByTxIdAsync(string txId, CancellationToken ct)
        => _record.GetISOMessageWithTransactionsByTxIdAsync(txId, ct);

    public Task<ISOMessage?> GetISOMessageByTxIdAndTypeAsync(string txId, ISOMessageType type, CancellationToken ct)
        => _record.GetISOMessageByTxIdAndTypeAsync(txId, type, ct);

    public Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingTransactionAsync(ISOMessage entity, CancellationToken ct)
        => _record.TryRecordIncomingTransactionAsync(entity, ct);

    public Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingReturnAsync(ISOMessage entity, CancellationToken ct)
        => _record.TryRecordIncomingReturnAsync(entity, ct);

    public Task<(ISOMessage record, SIPS.PostgreSQL.Enums.DedupOutcome outcome, string? duplicateBy)> TryRecordIncomingVerificationAsync(ISOMessage entity, CancellationToken ct)
        => _record.TryRecordIncomingVerificationAsync(entity, ct);

    public Task<List<ISOMessage>> GetISOMessagesByStatusAsync(TransactionStatus status, CancellationToken ct)
        => _record.GetISOMessagesByStatusAsync(status, ct);

    public Task<int> AppendAuditLedgerEventAsync(int isoMessageId, object ledgerEvent, uint xmin, CancellationToken ct)
        => _record.AppendAuditLedgerEventAsync(isoMessageId, ledgerEvent, xmin, ct);

    public Task<List<ISOMessage>> GetISOMessagesByUETRAndTypeAsync(string uetr, ISOMessageType type, CancellationToken ct)
        => _record.GetISOMessagesByUETRAndTypeAsync(uetr, type, ct);

    public Task<List<ISOMessage>> GetISOMessagesByOriginalTxIdAndTypeAsync(string orgnlTxId, ISOMessageType type, CancellationToken ct)
        => _record.GetISOMessagesByOriginalTxIdAndTypeAsync(orgnlTxId, type, ct);

    public async Task<Transaction?> GetTransactionByTxIdAsync(string txId, CancellationToken ct)
    {
        // This is a direct storage call because it's simpler here
        var ctx = (IStorageBroker)_record.GetType().GetField("_storage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(_record)!;
        return await ctx.Transactions.Where(t => t.TxId == txId).FirstOrDefaultAsync(ct);
    }
}
