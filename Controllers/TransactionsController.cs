using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using System.Text;              // added for Encoding
using static SIPS.Connect.KnownRoles;

namespace SIPS.Connect.Controllers;

[ApiController]
[Route(V)]
public sealed class TransactionsController(IStorageBroker broker) : ControllerBase
{
    private const string V = "api/v1/[controller]";
    private readonly IStorageBroker _broker = broker;
    [HttpGet("transactions")]
    [Authorize(Roles = ManageTransactions)]
    public async Task<IActionResult> GetTransactions([FromQuery] TransactionQuery request, CancellationToken ct)
    {
        var query = _broker.ISOMessages.AsNoTracking().AsQueryable();

        if (request.ISOMessageId > 0)
        {
            query = query.Where(t => t.Id == request.ISOMessageId);
        }

        if (!string.IsNullOrEmpty(request.TransactionId))
        {
            query = query.Where(t => t.TxId == request.TransactionId);
        }

        if (!string.IsNullOrEmpty(request.EndToEndId))
        {
            query = query.Where(t => t.EndToEndId == request.EndToEndId);
        }

        if (!string.IsNullOrEmpty(request.LocalInstrument))
        {
            query = query.Where(t => t.Transactions.Any(tr => tr.LocalInstrument == request.LocalInstrument));
        }

        if (!string.IsNullOrEmpty(request.CategoryPurpose))
        {
            query = query.Where(t => t.Transactions.Any(tr => tr.CategoryPurpose == request.CategoryPurpose));
        }

        if (!string.IsNullOrEmpty(request.DebtorAccount))
        {
            query = query.Where(t => t.Transactions.Any(tr => tr.DebtorAccount == request.DebtorAccount));
        }

        if (!string.IsNullOrEmpty(request.CreditorAccount))
        {
            query = query.Where(t => t.Transactions.Any(tr => tr.CreditorAccount == request.CreditorAccount));
        }

        if (!string.IsNullOrEmpty(request.Status))
        {
            TransactionStatus status = Enum.Parse<TransactionStatus>(request.Status, true);
            query = query.Where(t => t.Status.Equals(status));
        }

        if (!string.IsNullOrEmpty(request.FromDate))
        {
            DateTime fromDate = DateTime.Parse(request.FromDate);
            query = query.Where(t => t.Date >= fromDate);
        }

        if (!string.IsNullOrEmpty(request.ToDate))
        {
            DateTime toDate = DateTime.Parse(request.ToDate);
            query = query.Where(t => t.Date <= toDate);
        }

        var transactions = await query
            .SelectMany(t => t.Transactions)
            .Select(tr => new TransactionDto
            {
                Id = tr.Id,
                Type = tr.Type,
                ISOMessageId = tr.ISOMessageId,
                FromBIC = tr.FromBIC,
                LocalInstrument = tr.LocalInstrument,
                CategoryPurpose = tr.CategoryPurpose,
                EndToEndId = tr.EndToEndId,
                TxId = tr.TxId,
                Amount = tr.Amount,
                Currency = tr.Currency,
                DebtorName = tr.DebtorName,
                DebtorAccount = tr.DebtorAccount,
                DebtorAccountType = tr.DebtorAccountType,
                DebtorAgentBIC = tr.DebtorAgentBIC,
                DebtorIssuer = tr.DebtorIssuer,
                CreditorName = tr.CreditorName,
                CreditorAccount = tr.CreditorAccount,
                CreditorAccountType = tr.CreditorAccountType,
                CreditorAgentBIC = tr.CreditorAgentBIC,
                CreditorIssuer = tr.CreditorIssuer,
                RemittanceInformation = tr.RemittanceInformation
            })
            .OrderByDescending(tr => tr.Id)
            .Skip(request.Page * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);


        return Ok(transactions);
    }

    [HttpGet("iso-messages")]
    [Authorize(Roles = ManageMassages)]
    public async Task<IActionResult> GetMessages([FromQuery] MessageQuery request, CancellationToken ct)
    {
        var query = _broker.ISOMessages.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(request.MsgId))
        {
            query = query.Where(t => t.MsgId == request.MsgId);
        }

        if (!string.IsNullOrEmpty(request.BizMsgIdr))
        {
            query = query.Where(t => t.BizMsgIdr == request.BizMsgIdr);
        }

        if (!string.IsNullOrEmpty(request.MsgDefIdr))
        {
            query = query.Where(t => t.MsgDefIdr == request.MsgDefIdr);
        }

        if (!string.IsNullOrEmpty(request.Type))
        {
            ISOMessageType type = Enum.Parse<ISOMessageType>(request.Type, true);
            query = query.Where(t => t.MessageType.Equals(type));
        }

        if (!string.IsNullOrEmpty(request.Status))
        {
            TransactionStatus status = Enum.Parse<TransactionStatus>(request.Status, true);
            query = query.Where(t => t.Status.Equals(status));
        }

        if (!string.IsNullOrEmpty(request.FromDate))
        {
            DateTime fromDate = DateTime.Parse(request.FromDate);
            query = query.Where(t => t.Date >= fromDate);
        }

        if (!string.IsNullOrEmpty(request.ToDate))
        {
            DateTime toDate = DateTime.Parse(request.ToDate);
            query = query.Where(t => t.Date <= toDate);
        }

        var messages = await query
            .Select(t => new ISOMessageDto
            {
                Id = t.Id,
                MessageType = t.MessageType,
                Status = t.Status,
                MsgId = t.MsgId,
                BizMsgIdr = t.BizMsgIdr,
                MsgDefIdr = t.MsgDefIdr,
                Round = t.Round,
                TxId = t.TxId,
                EndToEndId = t.EndToEndId,
                Reason = t.Reason,
                AdditionalInfo = t.AdditionalInfo,
                Date = t.Date,
                FromBIC = t.FromBIC,
                ToBIC = t.ToBIC,
                Request = t.Message != null
                    ? Encoding.UTF8.GetString(t.Message)
                    : string.Empty,                     // decode byte[] → string
                Response = t.Response != null
                    ? Encoding.UTF8.GetString(t.Response)
                    : string.Empty,                     // decode byte[] → string
            })
            .OrderByDescending(t => t.Id)
            .Skip(request.Page * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return Ok(messages);
    }
}

public sealed class MessageQuery
{
    public int Page { get; set; } = 0;
    public int PageSize { get; set; } = 10;
    public string? MsgId { get; set; }
    public string? BizMsgIdr { get; set; }
    public string? MsgDefIdr { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
}

public sealed class TransactionQuery
{
    public int Page { get; set; } = 0;
    public int PageSize { get; set; } = 10;
    public int ISOMessageId { get; set; }
    public string? TransactionId { get; set; }
    public string? EndToEndId { get; set; }
    public string? LocalInstrument { get; set; }
    public string? CategoryPurpose { get; set; }
    public string? DebtorAccount { get; set; }
    public string? CreditorAccount { get; set; }
    public string? Status { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
}

public sealed class TransactionDto
{
    public int Id { get; set; }

    public TransactionType Type { get; set; }
    public int ISOMessageId { get; set; }

    public string? FromBIC { get; set; }

    public string? LocalInstrument { get; set; }

    public string? CategoryPurpose { get; set; }

    public string? EndToEndId { get; set; }

    public string? TxId { get; set; }

    public decimal? Amount { get; set; }

    public string? Currency { get; set; }

    public string? DebtorName { get; set; }

    public string? DebtorAccount { get; set; }

    public string? DebtorAccountType { get; set; }

    public string? DebtorAgentBIC { get; set; }

    public string DebtorIssuer { get; set; } = "C";


    public string? CreditorName { get; set; }

    public string? CreditorAccount { get; set; }

    public string? CreditorAccountType { get; set; }

    public string? CreditorAgentBIC { get; set; }

    public string? CreditorIssuer { get; set; } = "C";


    public string? RemittanceInformation { get; set; }
}

public sealed class ISOMessageDto
{
    public int Id { get; set; }

    public ISOMessageType MessageType { get; set; }

    public TransactionStatus Status { get; set; }

    public string MsgId { get; set; } = string.Empty;

    public string BizMsgIdr { get; set; } = string.Empty;

    public string MsgDefIdr { get; set; } = string.Empty;

    public int Round { get; set; } = 1;

    public string? TxId { get; set; }

    public string? EndToEndId { get; set; }

    public string? Reason { get; set; }

    public string? AdditionalInfo { get; set; }

    public DateTimeOffset Date { get; set; }

    public string FromBIC { get; set; } = string.Empty;

    public string ToBIC { get; set; } = string.Empty;

    public string Request { get; set; } = string.Empty;

    public string Response { get; set; } = string.Empty;
}