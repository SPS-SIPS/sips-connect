using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using System.Text;
using static SIPS.Connect.KnownRoles;

namespace SIPS.Connect.Controllers;

[ApiController]
[Route(V)]
public sealed class StatusMessagesController(IStorageBroker broker) : ControllerBase
{
    private const string V = "api/v1/[controller]";
    private readonly IStorageBroker _broker = broker;

    [HttpGet("status-messages")]
    [Authorize(Roles = ManageMassages)]
    public async Task<IActionResult> GetStatusMessages([FromQuery] MessageQuery request, CancellationToken ct)
    {
        var query = _broker.ISOMessages
            .AsNoTracking()
            .AsQueryable();

        // Status messages are ISO 20022 pacs.002 variants.
        query = query.Where(t => t.MsgDefIdr != null && EF.Functions.Like(t.MsgDefIdr, "pacs.002%"));

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
                    : string.Empty,
                Response = t.Response != null
                    ? Encoding.UTF8.GetString(t.Response)
                    : string.Empty,
            })
            .OrderByDescending(t => t.Id)
            .Skip(request.Page * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return Ok(messages);
    }
}
