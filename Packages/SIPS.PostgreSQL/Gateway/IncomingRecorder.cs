using SIPS.PostgreSQL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Npgsql;
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
    public async Task<ISOMessage?> GetISOMessageByIdAsync(int id, CancellationToken ct)
    {
        return await _storage.ISOMessages.FindAsync([id], ct);
    }
    public async Task<ISOMessage?> GetISOMessageByTxIdAsync(string txId, CancellationToken ct)
    {
        return await _storage.ISOMessages
        .Where(x => x.TxId == txId && x.MessageType == ISOMessageType.TransactionRequest)
        .FirstOrDefaultAsync(ct);
    }

    public async Task<ISOMessage?> GetISOMessageByTxIdAndTypeAsync(string txId, ISOMessageType type, CancellationToken ct)
    {
        return await _storage.ISOMessages
            .Where(x => x.TxId == txId && x.MessageType == type)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ISOMessage?> GetISOMessageByMsgIdAndTypeAsync(string msgId, ISOMessageType type, CancellationToken ct)
    {
        return await _storage.ISOMessages
            .Where(x => x.MsgId == msgId && x.MessageType == type)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ISOMessage?> GetISOMessageWithTransactionsByTxIdAsync(string txId, CancellationToken ct)
    {
        return await _storage.ISOMessages
        .Where(x => x.TxId == txId && x.MessageType == ISOMessageType.TransactionRequest)
        .Include(x => x.Transactions)
        .FirstOrDefaultAsync(ct);
    }
    public async Task<List<ISOMessage>> GetISOMessagesByUETRAndTypeAsync(string uetr, ISOMessageType type, CancellationToken ct)
    {
        return await _storage.ISOMessages
            .Where(x => x.UETR == uetr && x.MessageType == type)
            .OrderByDescending(x => x.Date)
            .ToListAsync(ct);
    }
    public async Task<List<ISOMessage>> GetISOMessagesByOriginalTxIdAndTypeAsync(string orgnlTxId, ISOMessageType type, CancellationToken ct)
    {
        return await _storage.ISOMessages
            .Where(x => x.TxId == orgnlTxId && x.MessageType == type)
            .OrderByDescending(x => x.Date)
            .ToListAsync(ct);
    }

    public async Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingTransactionAsync(ISOMessage entity, CancellationToken ct)
    {
        try
        {
            await _storage.ISOMessages.AddAsync(entity, ct);
            await _storage.SaveChangesAsync(ct);
            return (entity, true); // IsNew = true
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            // [SMARTVISTA COMPLIANCE]: Constraint-specific conflict handling
            
            // Case 1: Message-level duplication (ux_iso_msg_type_msgid)
            if (pgEx.ConstraintName == "ux_iso_msg_type_msgid")
            {
                _logger.LogInformation("Structural De-duplication trigger (MsgId) for MsgId: {MsgId}. Loading existing record.", entity.MsgId);
                var existing = await GetISOMessageByMsgIdAndTypeAsync(entity.MsgId, entity.MessageType, ct);
                return (existing, false); // IsNew = false
            }
            
            // Case 2: Transaction-level duplication (ux_iso_msg_type_txid)
            if (pgEx.ConstraintName == "ux_iso_msg_type_txid")
            {
                _logger.LogInformation("Structural De-duplication trigger (TxId) for TxId: {TxId}. Loading existing record.", entity.TxId);
                var existing = await GetISOMessageByTxIdAndTypeAsync(entity.TxId!, entity.MessageType, ct);
                return (existing, false); // IsNew = false
            }

            // Fallback: If constraint name is missing or unknown, prioritize TxId if present, else MsgId
            _logger.LogWarning("Structural De-duplication trigger (Unknown Constraint: {ConstraintName}). Attempting fallback lookup.", pgEx.ConstraintName);
            
            if (!string.IsNullOrEmpty(entity.TxId))
            {
                var byTx = await GetISOMessageByTxIdAndTypeAsync(entity.TxId, entity.MessageType, ct);
                if (byTx != null) return (byTx, false);
            }
            
            if (!string.IsNullOrEmpty(entity.MsgId))
            {
                var byMsg = await GetISOMessageByMsgIdAndTypeAsync(entity.MsgId, entity.MessageType, ct);
                if (byMsg != null) return (byMsg, false);
            }

            return (null, false);
        }
    }

    public async Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingReturnAsync(ISOMessage entity, CancellationToken ct)
    {
        try
        {
            await _storage.ISOMessages.AddAsync(entity, ct);
            await _storage.SaveChangesAsync(ct);
            return (entity, true); // IsNew = true
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            // Case 1: RtrId duplication (ux_iso_msg_type_rtrid)
            if (pgEx.ConstraintName == "ux_iso_msg_type_rtrid")
            {
                _logger.LogInformation("Structural De-duplication trigger (RtrId) for RtrId: {RtrId}. Loading existing record.", entity.ReturnId);
                var existing = await _storage.ISOMessages
                    .Where(x => x.ReturnId == entity.ReturnId && x.MessageType == entity.MessageType)
                    .FirstOrDefaultAsync(ct);
                return (existing, false);
            }

            // Case 2: Deterministic Key duplication (ux_iso_msg_type_rtr_dedup)
            if (pgEx.ConstraintName == "ux_iso_msg_type_rtr_dedup")
            {
                _logger.LogInformation("Structural De-duplication trigger (ReturnDedupKey) for Key: {Key}. Loading existing record.", entity.ReturnDedupKey);
                var existing = await _storage.ISOMessages
                    .Where(x => x.ReturnDedupKey == entity.ReturnDedupKey && x.MessageType == entity.MessageType)
                    .FirstOrDefaultAsync(ct);
                return (existing, false);
            }

            // Fallback lookup
            _logger.LogWarning("Structural De-duplication trigger (Return - Unknown Constraint: {ConstraintName}). Attempting fallback lookup.", pgEx.ConstraintName);
            
            if (!string.IsNullOrEmpty(entity.ReturnId))
            {
                var byRtrId = await _storage.ISOMessages
                    .Where(x => x.ReturnId == entity.ReturnId && x.MessageType == entity.MessageType)
                    .FirstOrDefaultAsync(ct);
                if (byRtrId != null) return (byRtrId, false);
            }

            if (!string.IsNullOrEmpty(entity.ReturnDedupKey))
            {
                var byKey = await _storage.ISOMessages
                    .Where(x => x.ReturnDedupKey == entity.ReturnDedupKey && x.MessageType == entity.MessageType)
                    .FirstOrDefaultAsync(ct);
                if (byKey != null) return (byKey, false);
            }

            return (null, false);
        }
    }
    
    public async Task<(ISOMessage record, SIPS.PostgreSQL.Enums.DedupOutcome outcome, string? duplicateBy)> TryRecordIncomingVerificationAsync(ISOMessage entity, CancellationToken ct)
    {
        try
        {
            await _storage.ISOMessages.AddAsync(entity, ct);
            await _storage.SaveChangesAsync(ct);
            return (entity, SIPS.PostgreSQL.Enums.DedupOutcome.Owner, null);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            // Prefer constraint-name routing
            if (pg.ConstraintName == "ux_iso_msg_type_msgid")
            {
                var existing = await _storage.ISOMessages
                    .AsNoTracking()
                    .Where(x => x.MessageType == entity.MessageType && x.MsgId == entity.MsgId)
                    .OrderByDescending(x => x.Id)
                    .FirstAsync(ct);
                return (existing, SIPS.PostgreSQL.Enums.DedupOutcome.Follower, "MsgId");
            }
            
            if (pg.ConstraintName == "ux_iso_msg_type_txid" && !string.IsNullOrWhiteSpace(entity.TxId))
            {
                 var existing = await _storage.ISOMessages
                    .AsNoTracking()
                    .Where(x => x.MessageType == entity.MessageType && x.TxId == entity.TxId)
                    .OrderByDescending(x => x.Id)
                    .FirstAsync(ct);
                return (existing, SIPS.PostgreSQL.Enums.DedupOutcome.Follower, "TxId");
            }

            // Fallback: best-effort lookup
            ISOMessage? fallback = null;
            if (!string.IsNullOrWhiteSpace(entity.MsgId))
                fallback = await _storage.ISOMessages
                    .AsNoTracking()
                    .Where(x => x.MessageType == entity.MessageType && x.MsgId == entity.MsgId)
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefaultAsync(ct);
            
            if (fallback == null && !string.IsNullOrWhiteSpace(entity.TxId))
                fallback = await _storage.ISOMessages
                    .AsNoTracking()
                    .Where(x => x.MessageType == entity.MessageType && x.TxId == entity.TxId)
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefaultAsync(ct);

            if (fallback != null)
                return (fallback, SIPS.PostgreSQL.Enums.DedupOutcome.Follower, "Unknown");

            // Unlikely: constraint fired but no record found
            throw;
        }
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
        entity.Status = message.Status;
        entity.Reason = message.Reason;
        entity.AdditionalInfo = message.AdditionalInfo;
        
        if (message.TxId != null)
        {
            entity.TxId = message.TxId;
        }
        
        if (message.EndToEndId != null)
        {
            entity.EndToEndId = message.EndToEndId;
        }
        
        if (message.CoreBankResponse != null)
        {
            entity.CoreBankResponse = message.CoreBankResponse;
        }

        await _storage.SaveChangesAsync(ct);

        return entity;
    }

    public async Task<List<ISOMessage>> GetISOMessagesByStatusAsync(TransactionStatus status, CancellationToken ct)
    {
        return await _storage.ISOMessages
            .Where(x => x.Status == status && x.MessageType == ISOMessageType.TransactionRequest)
            .Include(x => x.Transactions)
            .ToListAsync(ct);
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

    public async Task<int> AppendAuditLedgerEventAsync(int isoMessageId, object ledgerEvent, uint xmin, CancellationToken ct)
    {
        // Serialize as a SINGLE-ELEMENT ARRAY to guarantee jsonb array concatenation via ||
        var eventJson = JsonSerializer.Serialize(new[] { ledgerEvent });

        // Atomic append guarded by xmin (optimistic concurrency)
        // Using raw SQL to ensure atomicity at the DB level
        var sql = @"
UPDATE isomessages
SET corebankresponse = 
    jsonb_set(
        COALESCE(corebankresponse, '{{}}'::jsonb),
        '{{auditLedger}}',
        (
            COALESCE(corebankresponse->'auditLedger', '[]'::jsonb) 
            || @event::jsonb
        ),
        true
    )
WHERE id = @id 
  AND xmin = @expected_xmin;";

        var pId = new NpgsqlParameter("id", isoMessageId);
        var pXmin = new NpgsqlParameter("expected_xmin", (int)xmin); // xid fits int32 in practice
        var pEvent = new NpgsqlParameter("event", eventJson);

        return await _storage.Database.ExecuteSqlRawAsync(sql, [pEvent, pId, pXmin], ct);
    }
    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return await _storage.SaveChangesAsync(ct);
    }
}