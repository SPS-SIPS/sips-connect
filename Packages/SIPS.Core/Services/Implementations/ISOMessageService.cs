using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Persistence;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using SIPS.PostgreSQL.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace SIPS.Core.Services.Implementations;

public sealed class ISOMessageService(IPersistenceGateway persistence, ILogger<ISOMessageService> logger) : IISOMessageService
{
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ILogger<ISOMessageService> _logger = logger;
    public ISOMessageService(IPersistenceGateway persistence) : this(persistence, NullLogger<ISOMessageService>.Instance) { }

    public async Task<ISOMessage> RecordIncomingVerificationAsync(
        PayeeVerificationBuilder.Request request,
        string rawXml,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = await _persistence.RecordISOMessageAsync(
            new ISOMessage
            {
                MessageType = ISOMessageType.VerificationRequest,
                Date = System.DateTimeOffset.Now.ToUniversalTime(),
                FromBIC = request.From,
                ToBIC = request.To,
                Message = Encoding.UTF8.GetBytes(rawXml),
                Status = TransactionStatus.Pending,
                BizMsgIdr = request.BizMsgIdr,
                MsgDefIdr = request.MsgDefIdr,
                MsgId = request.MsgId,
                TxId = request.MsgId,
                UETR = (request as PayeeVerificationBuilder.Request)?.MsgId // Verification usually uses MsgId as TxId/UETR proxy
            }, ct);
        sw.Stop();
        _logger.LogInformation("DB persist RecordIncomingVerificationAsync txId={TxId} durationMs={Duration}", request.SIPSRequestId, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task PersistResponseAsync(
        ISOMessage isoMessage,
        TransactionStatus status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        // update the original object (tests expect the original to be mutated)
        isoMessage.Response = Encoding.UTF8.GetBytes(responseXml);
        isoMessage.Status = status;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;

        // create a snapshot for persistence so later in-memory changes don't alter recorded args
        var snapshot = new ISOMessage
        {
            Id = isoMessage.Id,
            MessageType = isoMessage.MessageType,
            Date = isoMessage.Date,
            FromBIC = isoMessage.FromBIC,
            ToBIC = isoMessage.ToBIC,
            // Response should contain the persisted response payload. Use Response (not Message)
            Response = Encoding.UTF8.GetBytes(responseXml),
            Status = isoMessage.Status,
            Reason = isoMessage.Reason,
            AdditionalInfo = isoMessage.AdditionalInfo,
            BizMsgIdr = isoMessage.BizMsgIdr,
            MsgDefIdr = isoMessage.MsgDefIdr,
            MsgId = isoMessage.MsgId,
            TxId = isoMessage.TxId,
            UETR = isoMessage.UETR,
            EndToEndId = isoMessage.EndToEndId,
            CoreBankResponse = isoMessage.CoreBankResponse,
            ReturnId = isoMessage.ReturnId,
            Round = isoMessage.Round,
            CoreBankRetryCount = isoMessage.CoreBankRetryCount
        };
        // debug: log statuses to help unit-test diagnosis
        _logger.LogDebug("[ISOMessageService] PersistResponseAsync original.Status={OriginalStatus} snapshot.Status={SnapshotStatus}", isoMessage.Status, snapshot.Status);
        await _persistence.ISOMessageResponseAsync(snapshot, ct);
        sw.Stop();
        _logger.LogInformation("DB persist PersistResponseAsync txId={TxId} durationMs={Duration}", isoMessage.TxId, sw.ElapsedMilliseconds);
    }

    public async Task<ISOMessageStatus> RecordIncomingStatusAsync(
        ISOMessage isoMessage,
        string rawXml,
        SIPS.ISO20022.Enums.Pacs002Role role,
        string? msgId,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var entity = new ISOMessageStatus
        {
            ISOMessageId = isoMessage.Id,
            Date = System.DateTimeOffset.Now.ToUniversalTime(),
            Message = Encoding.UTF8.GetBytes(rawXml),
            Status = TransactionStatus.Pending,
            MessageRole = role,
            MsgId = msgId
        };
        var result = await _persistence.RecordISOMessageStatusAsync(entity, ct);
        sw.Stop();
        _logger.LogInformation("DB persist RecordIncomingStatusAsync parentId={ParentId} durationMs={Duration}", isoMessage.Id, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task PersistStatusResponseAsync(
        ISOMessageStatus isoMessageStatus,
        TransactionStatus status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        // update original status and parent so callers see the changes
        isoMessageStatus.Response = Encoding.UTF8.GetBytes(responseXml);
        isoMessageStatus.Status = status;
        isoMessageStatus.Reason = reason;
        isoMessageStatus.AdditionalInfo = additionalInfo;
        if (isoMessageStatus.ISOMessage != null)
        {
            isoMessageStatus.ISOMessage.Status = status;
        }
        // snapshot status entity for persistence
        var snapshotStatus = new ISOMessageStatus
        {
            Id = isoMessageStatus.Id,
            ISOMessageId = isoMessageStatus.ISOMessageId,
            Date = isoMessageStatus.Date,
            // Status response should be in Response property
            Response = Encoding.UTF8.GetBytes(responseXml),
            Status = isoMessageStatus.Status,
            Reason = isoMessageStatus.Reason,
            AdditionalInfo = isoMessageStatus.AdditionalInfo
        };
        await _persistence.ISOMessageStatusResponseAsync(snapshotStatus, ct);
        sw.Stop();
        _logger.LogInformation("DB persist PersistStatusResponseAsync parentId={ParentId} durationMs={Duration}", isoMessageStatus.ISOMessageId, sw.ElapsedMilliseconds);
    }

    public async Task<ISOMessage> RecordIncomingTransactionAsync(
        PaymentRequestBuilder.Request request,
        string rawXml,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var entity = new ISOMessage
        {
            MessageType = ISOMessageType.TransactionRequest,
            Date = System.DateTimeOffset.Now.ToUniversalTime(),
            FromBIC = request.From,
            ToBIC = request.To,
            Message = Encoding.UTF8.GetBytes(rawXml),
            Status = TransactionStatus.Pending,
            TxId = request.TxId,
            EndToEndId = request.EndToEndId,
            BizMsgIdr = request.BizMsgIdr,
            BusinessService = request.BusinessService,
            MsgDefIdr = request.MsgDefIdr,
            MsgId = request.MsgId,
            UETR = request.UETR,
        };
        entity.Transactions.Add(new Transaction
        {
            Type = TransactionType.Deposit,
            FromBIC = request.From,
            LocalInstrument = request.LocalInstrument,
            CategoryPurpose = request.CategoryPurpose,
            EndToEndId = request.EndToEndId,
            TxId = request.TxId,
            Amount = request.Amount,
            Currency = request.Currency,
            DebtorName = request.Debtor.Name,
            DebtorAccount = request.Debtor.Account,
            DebtorAddress = request.Debtor.Address ?? string.Empty,
            DebtorAccountType = request.Debtor.AccountType,
            DebtorAgentBIC = request.Debtor.AgentBIC,
            DebtorIssuer = request.Debtor.Issuer ?? "C",
            CreditorName = request.Creditor.Name,
            CreditorAccount = request.Creditor.Account,
            CreditorAddress = request.Creditor.Address ?? string.Empty,
            CreditorAccountType = request.Creditor.AccountType,
            CreditorAgentBIC = request.Creditor.AgentBIC,
            CreditorIssuer = request.Creditor.Issuer ?? "C",
            RemittanceInformation = request.Ustrd ?? string.Empty
        });
        var result = await _persistence.RecordISOMessageAsync(entity, ct);
        sw.Stop();
        _logger.LogInformation("DB persist RecordIncomingTransactionAsync txId={TxId} durationMs={Duration}", request.TxId, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingTransactionAsync(
        PaymentRequestBuilder.Request request,
        string rawXml,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var entity = new ISOMessage
        {
            MessageType = ISOMessageType.TransactionRequest,
            Date = System.DateTimeOffset.Now.ToUniversalTime(),
            FromBIC = request.From,
            ToBIC = request.To,
            Message = Encoding.UTF8.GetBytes(rawXml),
            Status = TransactionStatus.Pending,
            TxId = request.TxId,
            UETR = request.UETR,
            EndToEndId = request.EndToEndId,
            BizMsgIdr = request.BizMsgIdr,
            BusinessService = request.BusinessService,
            MsgDefIdr = request.MsgDefIdr,
            MsgId = request.MsgId,
        };
        // Add basic transaction details for tracking
        entity.Transactions.Add(new Transaction
        {
            Type = TransactionType.Deposit,
            FromBIC = request.From,
            LocalInstrument = request.LocalInstrument,
            CategoryPurpose = request.CategoryPurpose ?? string.Empty,
            TxId = request.TxId,
            Amount = request.Amount,
            Currency = request.Currency,
            CreditorAccount = request.Creditor.Account,
            CreditorAddress = request.Creditor.Address ?? string.Empty,
            CreditorAccountType = request.Creditor.AccountType ?? string.Empty,
            CreditorAgentBIC = request.Creditor.AgentBIC ?? string.Empty,
            CreditorIssuer = request.Creditor.Issuer ?? "C",
            CreditorName = request.Creditor.Name ?? string.Empty,
            DebtorAccount = request.Debtor.Account,
            DebtorAddress = request.Debtor.Address ?? string.Empty,
            DebtorAccountType = request.Debtor.AccountType ?? string.Empty,
            DebtorAgentBIC = request.Debtor.AgentBIC ?? string.Empty,
            DebtorIssuer = request.Debtor.Issuer ?? "C",
            DebtorName = request.Debtor.Name ?? string.Empty,
            EndToEndId = request.EndToEndId ?? string.Empty,
            RemittanceInformation = request.Ustrd ?? string.Empty

        });

        var result = await _persistence.TryRecordIncomingTransactionAsync(entity, ct);
        sw.Stop();
        
        string status = result.IsNew ? "INSERT-First" : (result.Message != null ? "Follower-Loaded" : "Failed");
        _logger.LogInformation("DB persist TryRecordIncomingTransactionAsync ({Status}) txId={TxId} durationMs={Duration}", status, request.TxId, sw.ElapsedMilliseconds);
        
        return result;
    }

    public async Task<(ISOMessage record, SIPS.PostgreSQL.Enums.DedupOutcome outcome, string? duplicateBy)> TryRecordIncomingVerificationAsync(ISOMessage entity, CancellationToken ct)
    {
        return await _persistence.TryRecordIncomingVerificationAsync(entity, ct);
    }

    public async Task<ISOMessage?> GetInboundMessageByTxIdAsync(string txId, CancellationToken ct)
    {
        return await _persistence.GetISOMessageByTxIdAndTypeAsync(txId, ISOMessageType.TransactionRequest, ct);
    }

    public async Task PersistTransactionResponseAsync(
        ISOMessage isoMessage,
        TransactionStatus status,
        string reason,
        string? additionalInfo,
        string responseXml,
        string txId,
        string endToEndId,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        // update original object (tests expect mutation)
        isoMessage.Response = Encoding.UTF8.GetBytes(responseXml);
        isoMessage.Status = status;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        isoMessage.TxId = txId;
        isoMessage.EndToEndId = endToEndId;
        // snapshot for persistence
        var snapshot = new ISOMessage
        {
            Id = isoMessage.Id,
            MessageType = isoMessage.MessageType,
            Date = isoMessage.Date,
            FromBIC = isoMessage.FromBIC,
            ToBIC = isoMessage.ToBIC,
            // Persist response payload in Response
            Response = Encoding.UTF8.GetBytes(responseXml),
            Status = isoMessage.Status,
            Reason = isoMessage.Reason,
            AdditionalInfo = isoMessage.AdditionalInfo,
            BizMsgIdr = isoMessage.BizMsgIdr,
            MsgDefIdr = isoMessage.MsgDefIdr,
            MsgId = isoMessage.MsgId,
            TxId = isoMessage.TxId,
            UETR = isoMessage.UETR,
            EndToEndId = isoMessage.EndToEndId,
            CoreBankResponse = isoMessage.CoreBankResponse,
            ReturnId = isoMessage.ReturnId,
            Round = isoMessage.Round,
            CoreBankRetryCount = isoMessage.CoreBankRetryCount
        };
        await _persistence.ISOMessageResponseAsync(snapshot, ct);
        sw.Stop();
        _logger.LogInformation("DB persist PersistTransactionResponseAsync txId={TxId} durationMs={Duration}", txId, sw.ElapsedMilliseconds);
    }

    public async Task<ISOMessage> RecordIncomingReturnAsync(
        ReturnPaymentRequestBuilder.Request request,
        string rawXml,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var entity = new ISOMessage
        {
            MessageType = ISOMessageType.ReturnRequest,
            Date = System.DateTimeOffset.Now.ToUniversalTime(),
            FromBIC = request.From,
            ToBIC = request.To,
            Message = Encoding.UTF8.GetBytes(rawXml),
            Status = TransactionStatus.Pending,
            TxId = request.OrgnlTxId,
            EndToEndId = request.OriginalEndToEnd,
            ReturnId = request.ReturnId,
            BizMsgIdr = request.BizMsgIdr,
            MsgDefIdr = request.MsgDefIdr,
            MsgId = request.MsgId
        };
        entity.Transactions.Add(new Transaction
        {
            Type = TransactionType.ReturnWithdrawal,
            FromBIC = request.From,
            LocalInstrument = request.LocalInstrument,
            CategoryPurpose = request.CategoryPurpose,
            EndToEndId = request.OriginalEndToEnd,
            TxId = request.OriginalEndToEnd,
            Amount = request.OriginalAmount,
            Currency = request.OriginalCurrency,
            DebtorAccount = string.Empty,
            CreditorAccount = string.Empty,
            DebtorAccountType = string.Empty,
            DebtorAgentBIC = string.Empty,
            DebtorIssuer = string.Empty,
            DebtorName = string.Empty,
            DebtorAddress = string.Empty,
            CreditorAccountType = string.Empty,
            CreditorAgentBIC = string.Empty,
            CreditorAddress = string.Empty,
            CreditorIssuer = string.Empty,
            CreditorName = string.Empty,
            RemittanceInformation = request.ReturnReason + " " + request.AdditionalInfo
        });
        var result = await _persistence.RecordISOMessageAsync(entity, ct);
        sw.Stop();
        _logger.LogInformation("DB persist RecordIncomingReturnAsync txId={TxId} durationMs={Duration}", request.OrgnlTxId, sw.ElapsedMilliseconds);
        return result;
    }

    public async Task<(ISOMessage? Message, bool IsNew)> TryRecordIncomingReturnAsync(
        ReturnPaymentRequestBuilder.Request request,
        string rawXml,
        string? returnDedupKey,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var entity = new ISOMessage
        {
            MessageType = ISOMessageType.ReturnRequest,
            Date = System.DateTimeOffset.Now.ToUniversalTime(),
            FromBIC = request.From,
            ToBIC = request.To,
            Message = Encoding.UTF8.GetBytes(rawXml),
            Status = TransactionStatus.Pending,
            TxId = request.OrgnlTxId,
            EndToEndId = request.OriginalEndToEnd,
            ReturnId = request.ReturnId,
            ReturnDedupKey = returnDedupKey,
            BizMsgIdr = request.BizMsgIdr,
            MsgDefIdr = request.MsgDefIdr,
            MsgId = request.MsgId
        };
        
        // Add basic transaction details for tracking
        entity.Transactions.Add(new Transaction
        {
            Type = TransactionType.ReturnWithdrawal,
            FromBIC = request.From,
            LocalInstrument = request.LocalInstrument ?? string.Empty,
            CategoryPurpose = request.CategoryPurpose ?? string.Empty,
            TxId = request.OrgnlTxId,
            Amount = request.OriginalAmount,
            Currency = request.OriginalCurrency,
            RemittanceInformation = request.ReturnReason ?? string.Empty,
            EndToEndId = request.OriginalEndToEnd ?? string.Empty,
            CreditorAccount = string.Empty,
            CreditorAccountType = string.Empty,
            CreditorAgentBIC = string.Empty,
            CreditorIssuer = "C",
            CreditorName = string.Empty,
            DebtorAccount = string.Empty,
            DebtorAccountType = string.Empty,
            DebtorAgentBIC = string.Empty,
            DebtorIssuer = "C",
            DebtorName = string.Empty,
            DebtorAddress = string.Empty,
            CreditorAddress = string.Empty
        });


        var result = await _persistence.TryRecordIncomingReturnAsync(entity, ct);
        sw.Stop();
        
        string status = result.IsNew ? "INSERT-First" : (result.Message != null ? "Follower-Loaded" : "Failed");
        _logger.LogInformation("DB persist TryRecordIncomingReturnAsync ({Status}) orgnlTxId={OrgnlTxId} rtrId={RtrId} durationMs={Duration}", 
            status, request.OrgnlTxId, request.ReturnId ?? "NONE", sw.ElapsedMilliseconds);
        
        return result;
    }

    public async Task PersistReturnResponseAsync(
        ISOMessage isoMessage,
        string status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct,
        TransactionStatus? internalStatus = null)
    {
        var sw = Stopwatch.StartNew();
        // update original object
        isoMessage.Response = Encoding.UTF8.GetBytes(responseXml);
        isoMessage.Status = internalStatus ?? (status == "ACSC" ? TransactionStatus.Success : TransactionStatus.Failed);
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        // snapshot for persistence
        var snapshot = new ISOMessage
        {
            Id = isoMessage.Id,
            MessageType = isoMessage.MessageType,
            Date = isoMessage.Date,
            FromBIC = isoMessage.FromBIC,
            ToBIC = isoMessage.ToBIC,
            // Persist the response payload
            Response = Encoding.UTF8.GetBytes(responseXml),
            Status = isoMessage.Status,
            Reason = isoMessage.Reason,
            AdditionalInfo = isoMessage.AdditionalInfo,
            BizMsgIdr = isoMessage.BizMsgIdr,
            MsgDefIdr = isoMessage.MsgDefIdr,
            MsgId = isoMessage.MsgId,
            TxId = isoMessage.TxId,
            UETR = isoMessage.UETR,
            EndToEndId = isoMessage.EndToEndId,
            CoreBankResponse = isoMessage.CoreBankResponse,
            ReturnId = isoMessage.ReturnId,
            Round = isoMessage.Round,
            CoreBankRetryCount = isoMessage.CoreBankRetryCount
        };
        await _persistence.ISOMessageResponseAsync(snapshot, ct);
        sw.Stop();
        _logger.LogInformation("DB persist PersistReturnResponseAsync txId={TxId} durationMs={Duration}", isoMessage.TxId, sw.ElapsedMilliseconds);
    }

    public async Task MarkForCheckStatusAsync(
        ISOMessage isoMessage,
        string reason,
        CancellationToken ct,
        bool incrementRound = true)
    {
        var sw = Stopwatch.StartNew();
        isoMessage.Status = TransactionStatus.CheckStatus;
        isoMessage.Reason = reason;
        if (incrementRound)
            isoMessage.Round += 1;
        await _persistence.ISOMessageResponseAsync(isoMessage, ct);
        sw.Stop();
        _logger.LogInformation("DB persist MarkForCheckStatusAsync txId={TxId} round={Round} durationMs={Duration}",
            isoMessage.TxId, isoMessage.Round, sw.ElapsedMilliseconds);
    }

    public async Task FinalizeAfterMaxRetriesAsync(
        ISOMessage isoMessage,
        string reason,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        isoMessage.Status = TransactionStatus.Failed;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = $"Exceeded max SAF retries ({isoMessage.Round})";
        await _persistence.ISOMessageResponseAsync(isoMessage, ct);
        sw.Stop();
        _logger.LogInformation("DB persist FinalizeAfterMaxRetriesAsync txId={TxId} round={Round} durationMs={Duration}",
            isoMessage.TxId, isoMessage.Round, sw.ElapsedMilliseconds);
    }

    public async Task<bool> AppendAuditLedgerEventAsync(
        int isoMessageId,
        object ledgerEvent,
        CancellationToken ct)
    {
        const int maxRetries = 3;
        var sw = Stopwatch.StartNew();

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            // 1. Get current xmin by fetching the message
            var message = await _persistence.GetISOMessageByIdAsync(isoMessageId, ct);
            if (message == null)
            {
                _logger.LogWarning("Failed to append audit ledger: ISOMessage {Id} not found.", isoMessageId);
                return false;
            }

            // 2. Attempt atomic append using the retrieved xmin
            var affected = await _persistence.AppendAuditLedgerEventAsync(isoMessageId, ledgerEvent, message.xmin, ct);

            if (affected == 1)
            {
                _logger.LogInformation("Audit ledger event appended to ISOMessage {Id} (Attempt {Attempt}, Duration: {Duration}ms)", 
                    isoMessageId, attempt, sw.ElapsedMilliseconds);
                return true;
            }

            _logger.LogWarning("Concurrency conflict appending audit ledger to ISOMessage {Id} (Attempt {Attempt}). Retrying...", 
                isoMessageId, attempt);
        }

        _logger.LogError("Failed to append audit ledger to ISOMessage {Id} after {Max} attempts due to concurrency conflicts.", 
            isoMessageId, maxRetries);
        return false;
    }
}
