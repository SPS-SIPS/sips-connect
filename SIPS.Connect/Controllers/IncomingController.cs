using System.Text;
using SIPS.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
namespace SIPS.Connect.Controllers;
[ApiController]
[Produces("application/json")]
[Route("api/v1/[controller]")]
public class IncomingController(IIncoming isoService) : ControllerBase
{
    private readonly IIncoming _isoService = isoService;
    [HttpPost]
    public async Task<ActionResult> Post(CancellationToken ct)
    {
        Request.EnableBuffering();

        using var reader = new StreamReader(Request.Body, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);

        var result = await _isoService.Handle(body, ct);

        return Content(result, "application/xml", Encoding.UTF8);
    }
}
