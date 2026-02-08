using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIPS.ISO20022.Models;
using SIPS.ISO20022.Models.DTOs;
using SIPS.PostgreSQL.Enums;

namespace SIPS.Connect.Controllers;
[ApiController]
[Produces("application/json")]
[Route("api/v1/[controller]")]
public class EnumerationsController : ControllerBase
{

    [HttpGet("TransactionStatuses")]
    [Authorize]
    public Response<List<Lookup<int>>> TransactionStatuses()
    {
        var data = Enum.GetValues(typeof(TransactionStatus))
                       .Cast<TransactionStatus>()
                       .Select(enumValue => new Lookup<int>((int)enumValue, enumValue.ToString(), ""))
                       .ToList();
        return new Response<List<Lookup<int>>>(data);
    }

    [HttpGet("ISOMessageTypes")]
    [Authorize]
        public Response<List<Lookup<int>>> ISOMessageTypes()
    {
        var data = Enum.GetValues(typeof(ISOMessageType))
                       .Cast<ISOMessageType>()
                       .Select(enumValue => new Lookup<int>((int)enumValue, enumValue.ToString(), ""))
                       .ToList();
        return new Response<List<Lookup<int>>>(data);
    }

    [HttpGet("TransactionType")]
    [Authorize]
    public Response<List<Lookup<int>>> TransactionTypes()
    {
        var data = Enum.GetValues(typeof(TransactionType))
                       .Cast<TransactionType>()
                       .Select(enumValue => new Lookup<int>((int)enumValue, enumValue.ToString(), ""))
                       .ToList();
        return new Response<List<Lookup<int>>>(data);
    }
}
