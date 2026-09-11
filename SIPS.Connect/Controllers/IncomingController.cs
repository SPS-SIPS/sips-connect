using System.Text;
using SIPS.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SIPS.Connect.Services;
namespace SIPS.Connect.Controllers;
[ApiController]
[Produces("application/xml")]
[Route("api/v1/[controller]")]
public class IncomingController(IIncoming isoService, IPapssCallbackGuard papssGuard, IParticipantCallbackContext callbackContext, ILogger<IncomingController> logger) : ControllerBase
{
    private readonly IIncoming _isoService = isoService;
    [HttpPost]
    public async Task<ActionResult> Post(CancellationToken ct)
    {
        Request.EnableBuffering();

        using var reader = new StreamReader(Request.Body, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);

        try
        {
            if (await papssGuard.ValidateAsync(body, ct) is { } route)
            {
                logger.LogInformation("Validated PAPSS callback for participant {Participant} using mapping profile {MappingProfile}", route.Principal, route.CallbackMappingProfile);
                using var mapping = callbackContext.Push(route);
                var routedResult = await _isoService.Handle(body, ct);
                return Content(routedResult, "application/xml", Encoding.UTF8);
            }
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
        catch (InvalidDataException error)
        {
            return BadRequest(new { code = "INVALID_PAPSS_CALLBACK", message = error.Message });
        }

        var result = await _isoService.Handle(body, ct);

        return Content(result, "application/xml", Encoding.UTF8);
    }
}
