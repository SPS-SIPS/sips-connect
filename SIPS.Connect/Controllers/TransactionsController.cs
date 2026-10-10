using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using System.Text;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.XMLDsig.Xades.Options;
using static SIPS.Connect.KnownRoles;

namespace SIPS.Connect.Controllers;

[ApiController]
[Route(V)]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("UI")]
public sealed class TransactionsController(IStorageBroker broker, XadesOptions? xades = null, PapssFacingOptions? papss = null) : ControllerBase
{
    private const string V = "api/v1/[controller]";
    private readonly IStorageBroker _broker = broker;
    private readonly UnifiedTransactionLists _lists = new(broker, xades?.BIC, papss?.SecurityProfile);
    [HttpGet("transactions")]
    [Authorize(Roles = ManageTransactions)]
    public async Task<IActionResult> GetTransactions([FromQuery] TransactionQuery request, CancellationToken ct)
    {
        if (UnifiedTransactionLists.Validate(request) is { } error) return BadRequest(error);
        var query = _lists.LegacyMessages(request);

        if (request.RelatedToISOMessageId > 0)
        {
            var related = await _broker.ISOMessages
                .AsNoTracking()
                .Where(t => t.Id == request.RelatedToISOMessageId)
                .Select(t => new { t.TxId, t.EndToEndId })
                .SingleOrDefaultAsync(ct);

            if (related is null)
            {
                return NotFound($"ISO message {request.RelatedToISOMessageId} not found");
            }

            if (!string.IsNullOrEmpty(related.TxId))
            {
                query = query.Where(t => t.TxId == related.TxId);
            }

            if (!string.IsNullOrEmpty(related.EndToEndId))
            {
                query = query.Where(t => t.EndToEndId == related.EndToEndId);
            }
        }

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
            DateTimeOffset fromDate = DateTimeOffset.Parse(request.FromDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
            query = query.Where(t => t.Date >= fromDate);
        }

        if (!string.IsNullOrEmpty(request.ToDate))
        {
            DateTimeOffset toDate = DateTimeOffset.Parse(request.ToDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
            query = query.Where(t => t.Date <= toDate);
        }

        var transactions = query
            .SelectMany(t => t.Transactions)
            .Select(tr => new TransactionDto
            {
                Id = tr.Id,
                ListItemId = "ISO:" + tr.Id.ToString().PadLeft(10, '0'),
                Rail = tr.ISOMessage.BusinessService == _lists.SecurityProfile ? "PAPSS" : "SIPS",
                OperationType = tr.Type == TransactionType.ReturnDeposit || tr.Type == TransactionType.ReturnWithdrawal ? "RETURN" : "PAYMENT",
                Direction = tr.Type == TransactionType.Deposit || tr.Type == TransactionType.ReturnDeposit ? "INBOUND" : "OUTBOUND",
                RecordedAtUtc = tr.ISOMessage.Date,
                Status = tr.ISOMessage.Status,
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
                DebtorAddress = tr.DebtorAddress,
                DebtorAccountType = tr.DebtorAccountType,
                DebtorAgentBIC = tr.DebtorAgentBIC,
                DebtorIssuer = tr.DebtorIssuer,
                CreditorName = tr.CreditorName,
                CreditorAccount = tr.CreditorAccount,
                CreditorAddress = tr.CreditorAddress,
                CreditorAccountType = tr.CreditorAccountType,
                CreditorAgentBIC = tr.CreditorAgentBIC,
                CreditorIssuer = tr.CreditorIssuer,
                RemittanceInformation = tr.RemittanceInformation
            })
            .OrderByDescending(tr => tr.RecordedAtUtc).ThenByDescending(tr => tr.Id);

        return Ok(await _lists.TransactionsAsync(transactions, request, ct));
    }

    [HttpGet("iso-messages")]
    [Authorize(Roles = ManageMassages)]
    public async Task<IActionResult> GetMessages([FromQuery] MessageQuery request, CancellationToken ct)
    {
        if (UnifiedTransactionLists.Validate(request) is { } error) return BadRequest(error);
        var query = _lists.LegacyMessages(request);

        if (request.RelatedToISOMessageId > 0)
        {
            var related = await _broker.ISOMessages.AsNoTracking().Where(t => t.Id == request.RelatedToISOMessageId)
                .Select(t => new { t.TxId, t.EndToEndId }).SingleOrDefaultAsync(ct);
            if (related is null) return NotFound($"ISO message {request.RelatedToISOMessageId} not found");
            if (!string.IsNullOrEmpty(related.TxId)) query = query.Where(t => t.TxId == related.TxId);
            if (!string.IsNullOrEmpty(related.EndToEndId)) query = query.Where(t => t.EndToEndId == related.EndToEndId);
        }

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

        if (!string.IsNullOrEmpty(request.EndToEndId))
        {
            query = query.Where(t => t.EndToEndId == request.EndToEndId || t.Transactions.Any(tr => tr.EndToEndId == request.EndToEndId));
        }

        if (!string.IsNullOrEmpty(request.TransactionId))
        {
            query = query.Where(t => t.TxId == request.TransactionId || t.Transactions.Any(tr => tr.TxId == request.TransactionId));
        }

        if (!string.IsNullOrEmpty(request.DebtorAccount))
        {
            query = query.Where(t => t.Transactions.Any(tr => tr.DebtorAccount == request.DebtorAccount));
        }

        if (!string.IsNullOrEmpty(request.CreditorAccount))
        {
            query = query.Where(t => t.Transactions.Any(tr => tr.CreditorAccount == request.CreditorAccount));
        }

        if (!string.IsNullOrEmpty(request.FromDate))
        {
            DateTimeOffset fromDate = DateTimeOffset.Parse(request.FromDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
            query = query.Where(t => t.Date >= fromDate);
        }

        if (!string.IsNullOrEmpty(request.ToDate))
        {
            DateTimeOffset toDate = DateTimeOffset.Parse(request.ToDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
            query = query.Where(t => t.Date <= toDate);
        }

        var messages = query
            .Select(t => new ISOMessageDto
            {
                Id = t.Id,
                ISOMessageId = t.Id,
                ListItemId = "ISO:" + t.Id.ToString().PadLeft(10, '0'),
                Rail = t.BusinessService == _lists.SecurityProfile ? "PAPSS" : "SIPS",
                OperationType = t.MessageType == ISOMessageType.VerificationRequest || t.MessageType == ISOMessageType.VerificationResponse ? "VERIFICATION"
                    : t.MessageType == ISOMessageType.TransactionRequest || t.MessageType == ISOMessageType.TransactionResponse ? "PAYMENT"
                    : t.MessageType == ISOMessageType.ReturnRequest || t.MessageType == ISOMessageType.ReturnResponse ? "RETURN" : "STATUS_ENQUIRY",
                Direction = _lists.LocalBic == null ? "UNKNOWN" : t.FromBIC == _lists.LocalBic ? "OUTBOUND" : "INBOUND",
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
                RecordedAtUtc = t.Date,
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
            .OrderByDescending(t => t.RecordedAtUtc).ThenByDescending(t => t.Id);

        return Ok(await _lists.MessagesAsync(messages, request, ct));
    }
}

public sealed class MessageQuery : UnifiedListQuery
{
    public int RelatedToISOMessageId { get; set; }
    public string? MsgId { get; set; }
    public string? BizMsgIdr { get; set; }
    public string? MsgDefIdr { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? EndToEndId { get; set; }
    public string? TransactionId { get; set; }
    public string? DebtorAccount { get; set; }
    public string? CreditorAccount { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
}

public sealed class TransactionQuery : UnifiedListQuery
{
    public int RelatedToISOMessageId { get; set; }
    public int? ISOMessageId { get; set; }
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

public sealed class TransactionDto : UnifiedListItem
{
    public int? Id { get; set; }

    public TransactionType? Type { get; set; }
    public int? ISOMessageId { get; set; }

    public string? FromBIC { get; set; }

    public string? LocalInstrument { get; set; }

    public string? CategoryPurpose { get; set; }

    public string? EndToEndId { get; set; }

    public string? TxId { get; set; }

    public decimal? Amount { get; set; }

    public string? Currency { get; set; }

    public string? DebtorName { get; set; }

    public string? DebtorAccount { get; set; }
    public string? DebtorAddress { get; set; }
    public string? DebtorAccountType { get; set; }

    public string? DebtorAgentBIC { get; set; }

    public string DebtorIssuer { get; set; } = "C";


    public string? CreditorName { get; set; }

    public string? CreditorAccount { get; set; }
    public string? CreditorAddress { get; set; }
    public string? CreditorAccountType { get; set; }

    public string? CreditorAgentBIC { get; set; }

    public string? CreditorIssuer { get; set; } = "C";


    public string? RemittanceInformation { get; set; }
}

public sealed class ISOMessageDto : UnifiedListItem
{
    public int? Id { get; set; }
    public int? ISOMessageId { get; set; }

    public ISOMessageType MessageType { get; set; }


    public string MsgId { get; set; } = string.Empty;

    public string BizMsgIdr { get; set; } = string.Empty;

    public string MsgDefIdr { get; set; } = string.Empty;

    public int Round { get; set; } = 1;

    public string? TxId { get; set; }

    public string? EndToEndId { get; set; }

    public string? Reason { get; set; }

    public string? AdditionalInfo { get; set; }


    public DateTimeOffset? MessageCreatedAtUtc { get; set; }

    public DateTimeOffset Date { get; set; }

    public string FromBIC { get; set; } = string.Empty;

    public string ToBIC { get; set; } = string.Empty;

    public string Request { get; set; } = string.Empty;

    public string Response { get; set; } = string.Empty;
}
