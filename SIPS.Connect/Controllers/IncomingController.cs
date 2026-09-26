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
public class IncomingController(IIncoming isoService, IPapssCallbackGuard papssGuard, IPapssPaymentDecisionPublisher decisionPublisher, IPapssInboundVerificationService inboundVerification, IParticipantCallbackContext callbackContext, ILogger<IncomingController> logger, IPapssPaymentCallbackService? paymentCallbacks = null) : ControllerBase
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
                // The gateway treats any 2xx as "delivered". Every PAPSS branch below returns 2xx only after
                // the inbound message is durably stored; bank delivery and gateway replies happen from outboxes.
                var definition = MessageDefinition(body);
                if (definition == "acmt.023.001.03")
                {
                    await inboundVerification.HandleAsync(route, body, ct);
                    return Ok();
                }
                if (paymentCallbacks is not null)
                {
                    // PAPSS pacs.002 (payment / return / status-enquiry result) and inbound pacs.004 are stored in the
                    // operation store and pushed to the bank from its outbox. They never reach the SmartVista handlers
                    // (no orphan reject: an uncorrelated report is stored for operators and still acknowledged).
                    if (definition == "pacs.002.001.12")
                    {
                        await paymentCallbacks.HandleStatusReportAsync(route, body, ct);
                        return Ok();
                    }
                    if (definition == "pacs.004.001.11")
                    {
                        await paymentCallbacks.HandleReturnAsync(route, body, ct);
                        return Ok();
                    }
                    // camt.029 (the beneficiary refused our recall): stored and correlated to the recall, never to the payment.
                    if (definition == PapssRecallMessages.Camt029)
                    {
                        await paymentCallbacks.HandleRecallResolutionAsync(route, body, ct);
                        return Ok();
                    }
                }
                var handled = await _isoService.Handle(body, ct);
                if (definition == "acmt.024.001.03" && !string.IsNullOrEmpty(handled))
                    logger.LogWarning("PAPSS acmt.024 callback was rejected by the result handler and not stored");
                if (definition == "pacs.008.001.10")
                {
                    // The legacy handler stored the payment and the bank decision (isomessages, CB_PaymentRequest);
                    // the decision outbox stays the durable, retryable path to the gateway. The operation store mirrors it.
                    if (paymentCallbacks is not null) await paymentCallbacks.RecordInboundPaymentAsync(route, body, ct);
                    await decisionPublisher.PersistAndSubmitAsync(route, body, ct);
                    if (paymentCallbacks is not null) await paymentCallbacks.SyncInboundPaymentAsync(body, ct);
                }
                return Ok();
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
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The PAPSS callback was not (completely) stored: a non-2xx makes the gateway redeliver.
            // Bank delivery failures no longer surface here; they are retried from the push outbox.
            logger.LogError(error, "PAPSS callback could not be processed and stored");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "PAPSS_CALLBACK_NOT_STORED", message = "The PAPSS callback could not be stored; retry later." });
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
