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

    [HttpGet]
    [Authorize(Roles = ManageMassages)]
    public async Task<IActionResult> GetStatusMessages([FromQuery] MessageQuery request, CancellationToken ct)
    {
        var query = _broker.ISOMessageStatuses
            .AsNoTracking()
            .Include(s => s.ISOMessage)
            .AsQueryable();

        if (request.RelatedToISOMessageId > 0)
        {
            query = query.Where(s => s.ISOMessageId == request.RelatedToISOMessageId);
        }

        if (!string.IsNullOrEmpty(request.Status))
        {
            TransactionStatus status = Enum.Parse<TransactionStatus>(request.Status, true);
            query = query.Where(s => s.Status.Equals(status));
        }

        var messages = await query
            .Select(s => new ISOMessageDto
            {
                Id = s.Id,
                MessageType = s.ISOMessage.MessageType,
                Status = s.Status,
                MsgId = s.ISOMessage.MsgId,
                BizMsgIdr = s.ISOMessage.BizMsgIdr,
                MsgDefIdr = s.ISOMessage.MsgDefIdr,
                Round = s.ISOMessage.Round,
                TxId = s.ISOMessage.TxId,
                EndToEndId = s.ISOMessage.EndToEndId,
                Reason = s.Reason,
                AdditionalInfo = s.AdditionalInfo,
                Date = s.Date,
                FromBIC = s.ISOMessage.FromBIC,
                ToBIC = s.ISOMessage.ToBIC,
                Request = s.Message != null
                    ? Encoding.UTF8.GetString(s.Message)
                    : string.Empty,
                Response = s.Response != null
                    ? Encoding.UTF8.GetString(s.Response)
                    : string.Empty,
            })
            .OrderByDescending(s => s.Id)
            .Skip(request.Page * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return Ok(messages);
    }
}
