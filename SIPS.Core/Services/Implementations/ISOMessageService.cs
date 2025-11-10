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

namespace SIPS.Core.Services.Implementations;

public sealed class ISOMessageService(IPersistenceGateway persistence) : IISOMessageService
{
    private readonly IPersistenceGateway _persistence = persistence;

    public async Task<ISOMessage> RecordIncomingVerificationAsync(
        PayeeVerificationBuilder.Request request,
        string rawXml,
        CancellationToken ct)
    {
        return await _persistence.RecordISOMessageAsync(
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
                TxId = request.SIPSRequestId,
            }, ct);
    }

    public async Task PersistResponseAsync(
        ISOMessage isoMessage,
        string status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct)
    {
        isoMessage.Response = Encoding.UTF8.GetBytes(responseXml);
        isoMessage.Status = (status == "ACSC" || status == "SUCC") ? TransactionStatus.Success : TransactionStatus.Failed;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        await _persistence.ISOMessageResponseAsync(isoMessage, ct);
    }

    public async Task<ISOMessageStatus> RecordIncomingStatusAsync(
        ISOMessage isoMessage,
        string rawXml,
        CancellationToken ct)
    {
        var entity = new ISOMessageStatus
        {
            ISOMessageId = isoMessage.Id,
            Date = System.DateTimeOffset.Now.ToUniversalTime(),
            Message = Encoding.UTF8.GetBytes(rawXml),
            Status = TransactionStatus.Pending,
        };
        return await _persistence.RecordISOMessageStatusAsync(entity, ct);
    }

    public async Task PersistStatusResponseAsync(
        ISOMessageStatus isoMessageStatus,
        TransactionStatus status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct)
    {
        isoMessageStatus.Response = Encoding.UTF8.GetBytes(responseXml);
        isoMessageStatus.Status = status;
        isoMessageStatus.Reason = reason;
        isoMessageStatus.AdditionalInfo = additionalInfo;
        await _persistence.ISOMessageStatusResponseAsync(isoMessageStatus, ct);
    }

    public async Task<ISOMessage> RecordIncomingTransactionAsync(
        PaymentRequestBuilder.Request request,
        string rawXml,
        CancellationToken ct)
    {
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
            MsgDefIdr = request.MsgDefIdr,
            MsgId = request.MsgId,
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
            DebtorAccountType = request.Debtor.AccountType,
            DebtorAgentBIC = request.Debtor.AgentBIC,
            DebtorIssuer = request.Debtor.Issuer ?? "C",
            CreditorName = request.Creditor.Name,
            CreditorAccount = request.Creditor.Account,
            CreditorAccountType = request.Creditor.AccountType,
            CreditorAgentBIC = request.Creditor.AgentBIC,
            CreditorIssuer = request.Creditor.Issuer ?? "C",
            RemittanceInformation = request.Ustrd ?? string.Empty
        });
        return await _persistence.RecordISOMessageAsync(entity, ct);
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
        isoMessage.Response = Encoding.UTF8.GetBytes(responseXml);
        isoMessage.Status = status;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        isoMessage.TxId = txId;
        isoMessage.EndToEndId = endToEndId;
        await _persistence.ISOMessageResponseAsync(isoMessage, ct);
    }

    public async Task<ISOMessage> RecordIncomingReturnAsync(
        ReturnPaymentRequestBuilder.Request request,
        string rawXml,
        CancellationToken ct)
    {
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
            CreditorAccountType = string.Empty,
            CreditorAgentBIC = string.Empty,
            CreditorIssuer = string.Empty,
            CreditorName = string.Empty,
            RemittanceInformation = request.ReturnReason + " " + request.AdditionalInfo
        });
        return await _persistence.RecordISOMessageAsync(entity, ct);
    }

    public async Task PersistReturnResponseAsync(
        ISOMessage isoMessage,
        string status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct)
    {
        isoMessage.Response = Encoding.UTF8.GetBytes(responseXml);
        isoMessage.Status = status == "ACSC" ? TransactionStatus.Success : TransactionStatus.Failed;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        await _persistence.ISOMessageResponseAsync(isoMessage, ct);
    }
}
