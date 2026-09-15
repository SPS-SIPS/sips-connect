using System.Text.Json.Nodes;
using SIPS.Adapter;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using Microsoft.AspNetCore.Mvc;
using static SIPS.Connect.Helpers.APIResponseRenderer;
using static SIPS.Connect.Constants;
using static SIPS.Connect.KnownRoles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
using SIPS.Connect.Services;
using SIPS.ISO20022.Models.WpSips;
using System.Security.Claims;

namespace SIPS.Connect.Controllers;
[ApiController]
[Produces("application/json")]
[Route("api/v1/[controller]")]
public class GatewayController(
    IJsonAdapter jsonAdapter,
    IOutgoingVerificationHandler verificationService,
    IOutgoingTransactionHandler transactionService,
    IOutgoingTransactionStatusHandler transactionStatusService,
    IOutgoingReturnTransactionHandler returnTransactionService,
    IReturnRetryHandler returnRetryHandler,
    IOptions<CoreOptions> coreOptions,
    IParticipantOperationRouter operationRouter,
    IPapssFacingSipsClient papssClient
    ) : ControllerBase
{
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IOutgoingVerificationHandler _verificationService = verificationService;
    private readonly IOutgoingTransactionHandler _transactionService = transactionService;
    private readonly IOutgoingTransactionStatusHandler _transactionStatusService = transactionStatusService;
    private readonly IOutgoingReturnTransactionHandler _returnTransactionService = returnTransactionService;
    private readonly IReturnRetryHandler _returnRetryHandler = returnRetryHandler;
    private readonly CoreOptions _coreOptions = coreOptions.Value;
    private readonly IParticipantOperationRouter _operationRouter = operationRouter;
    private readonly IPapssFacingSipsClient _papssClient = papssClient;

    [HttpPost("Verify")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> VerifyPayee([FromBody] JsonObject body, CancellationToken ct)
    {
        JsonObject md = _jsonAdapter.Transform(body, VerificationRequest);
        var query = _jsonAdapter.ToObject<VerificationRequestDto>(md);
        if (Select(ParticipantOperation.Verification, query.Rail) == DownstreamRail.Papss)
            return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.VerifyAsync(Binding(ParticipantOperation.Verification), query, ct), PapssAdmissionMapping)));
        var response = await _verificationService.HandleAsync(query, ct);
        return GenerateAdminMessage(response, _jsonAdapter, VerificationResponse);
    }

    [HttpPost("Payment")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> MakePayment([FromBody] JsonObject body, CancellationToken ct)
    {
        if (_coreOptions.VerificationOnlyMode)
        {
            return BadRequest(new { Error = "SIPS Connect is configured in Verification Only mode. This operation is not allowed." });
        }

        JsonObject md = _jsonAdapter.Transform(body, PaymentRequest);
        var query = _jsonAdapter.ToObject<PaymentRequestDto>(md);
        if (Select(ParticipantOperation.Payment, query.Rail) == DownstreamRail.Papss)
            return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.PayAsync(Binding(ParticipantOperation.Payment), query, ct), PapssAdmissionMapping)));
        var response = await _transactionService.HandleAsync(query, ct);
        return GenerateAdminMessage(response, _jsonAdapter, PaymentResponse);
    }

    [HttpPost("Status")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> GetStatus([FromBody] JsonObject body, CancellationToken ct)
    {
        if (_coreOptions.VerificationOnlyMode)
        {
            return BadRequest(new { Error = "SIPS Connect is configured in Verification Only mode. This operation is not allowed." });
        }

        JsonObject md = _jsonAdapter.Transform(body, StatusRequest);
        var query = _jsonAdapter.ToObject<StatusRequestDto>(md);
        if (Select(ParticipantOperation.Status, query.Rail) == DownstreamRail.Papss)
            return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.GetStatusAsync(Binding(ParticipantOperation.Status), query, ct), PapssAdmissionMapping)));
        var response = await _transactionStatusService.HandleAsync(query, ct);
        return GenerateAdminMessage(response, _jsonAdapter, PaymentResponse);
    }

    [HttpPost("Return")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> GetReturn([FromBody] JsonObject body, CancellationToken ct)
    {
        if (_coreOptions.VerificationOnlyMode)
        {
            return BadRequest(new { Error = "SIPS Connect is configured in Verification Only mode. This operation is not allowed." });
        }

        JsonObject md = _jsonAdapter.Transform(body, ReturnRequest);
        var query = _jsonAdapter.ToObject<ReturnPaymentRequestDto>(md);
        if (Select(ParticipantOperation.Return, query.Rail) == DownstreamRail.Papss)
            return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.ReturnAsync(Binding(ParticipantOperation.Return), query, ct), PapssAdmissionMapping)));
        var response = await _returnTransactionService.HandleAsync(query, ct);
        return GenerateAdminMessage(response, _jsonAdapter, PaymentResponse);
    }


    [HttpPost("Retry/{id}")]
    [Authorize(Roles = Recon)]
    public async Task<ActionResult> Retry([FromRoute] string id, CancellationToken ct)
    {
        if (_coreOptions.VerificationOnlyMode)
        {
            return BadRequest(new { Error = "SIPS Connect is configured in Verification Only mode. This operation is not allowed." });
        }

        var response = await _returnRetryHandler.RetryReturnAsync(id, ct);
        if (response.Success)
            return GenerateAdminMessage(Response<ReturnRetryResult>.Success(response), _jsonAdapter, PaymentResponse);

        var statusCode = response.Status switch
        {
            "NotFound" => StatusCodes.Status404NotFound,
            "CallbackFailed" or "Error" => StatusCodes.Status502BadGateway,
            _ when response.Message.Equals("CoreBank retry failed", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status502BadGateway,
            "RetryConflict" => StatusCodes.Status409Conflict,
            "NoTransactionDetails" => StatusCodes.Status422UnprocessableEntity,
            _ when response.Message.Contains("not configured", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status503ServiceUnavailable,
            _ when response.Reason?.StartsWith("Missing", StringComparison.OrdinalIgnoreCase) == true => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status409Conflict
        };
        return StatusCode(statusCode, response);
    }

    [HttpPost("Readiness")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> Readiness([FromBody] JsonObject body, CancellationToken ct)
    {
        var mapped = _jsonAdapter.ToObject<ParticipantReadinessJsonRequest>(_jsonAdapter.Transform(body, Constants.ReadinessRequest));
        Select(ParticipantOperation.Readiness, mapped.Rail);
        return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.GetReadinessAsync(Binding(ParticipantOperation.Readiness), new(mapped.PapssId, mapped.Bic), ct), Constants.ReadinessResponse)));
    }

    [HttpPost("Discovery")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> Discovery([FromBody] JsonObject body, CancellationToken ct)
    {
        var mapped = _jsonAdapter.ToObject<ParticipantDiscoveryJsonRequest>(_jsonAdapter.Transform(body, Constants.ParticipantDiscoveryRequest));
        Select(ParticipantOperation.Discovery, mapped.Rail);
        return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.DiscoverAsync(Binding(ParticipantOperation.Discovery), new(mapped.Online, mapped.Type, mapped.Bic, mapped.PapssId), ct), Constants.ParticipantDiscoveryResponse)));
    }

    [HttpPost("FX")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> Fx([FromBody] JsonObject body, CancellationToken ct)
    {
        var mapped = _jsonAdapter.ToObject<FxJsonRequest>(_jsonAdapter.Transform(body, Constants.FxRequest));
        Select(ParticipantOperation.Fx, mapped.Rail);
        return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.GetFxAsync(Binding(ParticipantOperation.Fx), new(mapped.SenderCountry, mapped.ReceiverCountry, mapped.SenderCurrency, mapped.ReceiverCurrency, mapped.ReceiverBank, mapped.LocalInstrument, mapped.Amount, mapped.IsInvoice, mapped.InvoiceCurrency), ct), Constants.FxResponse)));
    }

    private DownstreamRail Select(ParticipantOperation operation, string? rail)
        => _operationRouter.Select(operation, rail);

    private PapssParticipantBinding Binding(ParticipantOperation operation)
        => _operationRouter.ResolvePapss(operation, User.Identity?.Name ?? User.FindFirstValue("preferred_username") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"));

    private async Task<ActionResult> Papss(Func<Task<ActionResult>> action)
    {
        try { return await action(); }
        catch (ParticipantRailException e) { return BadRequest(new { code = e.Code, message = e.Message }); }
        catch (ArgumentException e) { return UnprocessableEntity(new { code = "PAPSS_VALIDATION_FAILED", message = e.Message }); }
        catch (UnauthorizedAccessException) { return StatusCode(StatusCodes.Status502BadGateway, new { code = "INVALID_SIGNED_RESPONSE", message = "The WP-SIPS response could not be authenticated." }); }
        catch (HttpRequestException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "WP_SIPS_UNAVAILABLE", message = "The WP-SIPS service is unavailable." }); }
        catch (InvalidDataException) { return StatusCode(StatusCodes.Status502BadGateway, new { code = "INVALID_WP_SIPS_RESPONSE", message = "The WP-SIPS response is invalid." }); }
    }
}

public sealed record ParticipantReadinessJsonRequest(string? Rail, string? PapssId, string? Bic);
public sealed record ParticipantDiscoveryJsonRequest(string? Rail, bool? Online, string? Type, string? Bic, string? PapssId);
public sealed record FxJsonRequest(string? Rail, string SenderCountry, string ReceiverCountry, string SenderCurrency, string ReceiverCurrency, string ReceiverBank, string LocalInstrument, decimal Amount, bool IsInvoice, string? InvoiceCurrency);
