using SIPS.PostgreSQL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace SIPS.PostgreSQL.Gateway;
public class IncomingRecorder(ILogger<IncomingRecorder> logger, IStorageBroker storage) : IIncomingRecorder
{
    private readonly IStorageBroker _storage = storage;
    private readonly ILogger<IncomingRecorder> _logger = logger;
    public async Task<ISOMessage> ISOMessageAsync(ISOMessage message, CancellationToken ct)
    {
        await _storage.ISOMessages.AddAsync(message, ct);

        await _storage.SaveChangesAsync(ct);

        return message;
    }
    public async Task<Transaction> TransactionAsync(Transaction message, CancellationToken ct)
    {
        await _storage.Transactions.AddAsync(message, ct);

        await _storage.SaveChangesAsync(ct);

        return message;
    }
    public async Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct)
    {
        return await _storage.ISOMessages
        .Where(x => x.TxId == txId && x.MessageType == ISOMessageType.TransactionRequest)
        .FirstOrDefaultAsync(ct);
    }

    public async Task<ISOMessage?> GetISOMessageWithTransactionsByTxIdAsync(string txId, CancellationToken ct)
    {
        return await _storage.ISOMessages
        .Where(x => x.TxId == txId && x.MessageType == ISOMessageType.TransactionRequest)
        .Include(x => x.Transactions)
        .FirstOrDefaultAsync(ct);
    }
    public async Task<ISOMessageStatus> ISOMessageStatusAsync(ISOMessageStatus message, CancellationToken ct)
    {
        await _storage.ISOMessageStatuses.AddAsync(message, ct);

        await _storage.SaveChangesAsync(ct);

        return message;
    }
    public async Task<ISOMessage> ISOMessageResponseAsync(ISOMessage message, CancellationToken ct)
    {
        var entity = await _storage.ISOMessages.FindAsync([message.Id], ct);

        if (entity == null)
        {
            _logger.LogCritical("Unknown Message : {message}", message);
            return message;
        }

        entity.Response = message.Response;

        await _storage.SaveChangesAsync(ct);

        return entity;
    }

    public async Task<ISOMessageStatus> ISOMessageStatusResponseAsync(ISOMessageStatus message, CancellationToken ct)
    {
        var entity = await _storage.ISOMessageStatuses.FindAsync([message.Id], ct);

        if (entity == null)
        {
            _logger.LogCritical("Unknown Message : {message}", message);
            return message;
        }

        entity.Response = message.Response;

        await _storage.SaveChangesAsync(ct);

        return entity;
    }
    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return await _storage.SaveChangesAsync(ct);
    }
}