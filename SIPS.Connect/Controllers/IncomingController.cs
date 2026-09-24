using System.Text;
using SIPS.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SIPS.Connect.Services;
using SIPS.Core.Services.Callback;
using System.Xml.Linq;
namespace SIPS.Connect.Controllers;
[ApiController]
[Produces("application/xml")]
[Route("api/v1/[controller]")]
public class IncomingController(IIncoming isoService, IPapssCallbackGuard papssGuard, IPapssPaymentDecisionPublisher decisionPublisher, IParticipantCallbackContext callbackContext, ILogger<IncomingController> logger) : ControllerBase
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
                logger.LogInformation("Validated PAPSS callback for local participant {Bic} using mapping profile {MappingProfile}", route.Bic, route.CallbackMappingProfile);
                using var mapping = callbackContext.Push(route);
                await _isoService.Handle(body, ct);
                if (MessageDefinition(body) == "pacs.008.001.10")
                    await decisionPublisher.PersistAndSubmitAsync(route, body, ct);
                return Ok();
            }
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
        catch (CallbackDeliveryException error)
        {
            // The result was valid but the bank callback did not acknowledge it: signal a retryable failure.
            logger.LogWarning(error, "PAPSS callback could not be delivered to the participant callback");
            return StatusCode(StatusCodes.Status502BadGateway, new { code = "CALLBACK_DELIVERY_FAILED", message = error.Message });
        }
        catch (InvalidDataException error)
        {
            return BadRequest(new { code = "INVALID_PAPSS_CALLBACK", message = error.Message });
        }

        string result;
        try
        {
            result = await _isoService.Handle(body, ct);
        }
        catch (CallbackDeliveryException error)
        {
            logger.LogWarning(error, "Incoming message could not be delivered to the participant callback");
            return StatusCode(StatusCodes.Status502BadGateway, new { code = "CALLBACK_DELIVERY_FAILED", message = error.Message });
        }

        return Content(result, "application/xml", Encoding.UTF8);
    }

    private static string? MessageDefinition(string xml)
    {
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader, LoadOptions.None).Descendants().SingleOrDefault(x => x.Name.LocalName == "AppHdr")?
            .Elements().SingleOrDefault(x => x.Name.LocalName == "MsgDefIdr")?.Value;
    }
}
