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
using SIPS.Connect.Config;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;
using System.Text;

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
    IPapssFacingSipsClient papssClient,
    PapssOperationStore papssStore,
    PapssFacingOptions papssOptions,
    TimeProvider clock
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
            return await Papss(() => VerifyViaPapssAsync(query, ct));
        var response = await _verificationService.HandleAsync(query, ct);
        return GenerateAdminMessage(response, _jsonAdapter, VerificationResponse);
    }

    /// <summary>Pull API: the stored state/result of a PAPSS operation by the requestMessageId returned to the bank.</summary>
    [HttpGet("Operations/{requestMessageId}")]
    [Authorize(Roles = Gateway)]
    public Task<ActionResult> GetOperation([FromRoute] string requestMessageId, [FromQuery] int? waitSeconds, CancellationToken ct)
        => LookupAsync(requestMessageId, outboundVerificationOnly: false, waitSeconds, ct);

    /// <summary>Pull API for a bank-initiated PAPSS verification (same payload as Operations/{requestMessageId}).</summary>
    [HttpGet("Verify/{requestMessageId}")]
    [Authorize(Roles = Gateway)]
    public Task<ActionResult> GetVerification([FromRoute] string requestMessageId, [FromQuery] int? waitSeconds, CancellationToken ct)
        => LookupAsync(requestMessageId, outboundVerificationOnly: true, waitSeconds, ct);

    private async Task<ActionResult> LookupAsync(string requestMessageId, bool outboundVerificationOnly, int? waitSeconds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requestMessageId) || requestMessageId.Length > 128)
            return BadRequest(new { code = "INVALID_REQUEST_MESSAGE_ID", message = "requestMessageId is required (max 128 characters)." });
        // Long-poll only when an operator enabled it: PapssFacing:Lookup:MaxWaitSeconds (default 0).
        var wait = TimeSpan.FromSeconds(Math.Clamp(waitSeconds ?? 0, 0, Math.Max(0, papssOptions.Lookup.MaxWaitSeconds)));
        var until = clock.GetUtcNow() + wait;
        while (true)
        {
            var operation = await papssStore.FindAsync(requestMessageId, outboundVerificationOnly, ct);
            if (operation is null)
                return NotFound(new { code = "OPERATION_NOT_FOUND", message = "No PAPSS operation is stored for this requestMessageId." });
            var reply = operation.Direction == PapssDirection.Inbound ? await papssStore.FindReplyAsync(operation.Id, ct) : null;
            var now = clock.GetUtcNow();
            var result = PapssOperationResult.From(operation, reply, now, papssOptions.Outbound.VerificationResultExpirySeconds);
            if (result.Status != PapssOperationResult.Pending || now >= until)
                return Ok(_jsonAdapter.Transform(result, OperationResultMapping));
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    private async Task<ActionResult> VerifyViaPapssAsync(VerificationRequestDto query, CancellationToken ct)
    {
        var binding = Binding();
        var prepared = _papssClient.PrepareVerification(binding, query);
        // Durable BEFORE the gateway is called: a crash or timeout after this point leaves a
        // SUBMITTING/SUBMISSION_UNKNOWN row that the bank can look up and retry.
        var (operation, created) = await papssStore.CreateOutboundVerificationAsync(prepared, query, ct);
        var signed = prepared.SignedXml;
        if (!created)
        {
            if (!SameVerification(operation, query))
                throw new ParticipantRailException("DUPLICATE_CONFLICT", "The request message id was already used for a different verification.");
            switch (operation.GatewayState)
            {
                case PapssGatewayState.Admitted:
                    return Ok(_jsonAdapter.Transform(new PapssAdmissionResponse(operation.RequestMessageId, operation.AdmissionCode ?? "EXACT_REPLAY", true), PapssAdmissionMapping));
                case PapssGatewayState.Rejected:
                    throw new ParticipantRailException(operation.AdmissionCode ?? "REJECTED_BEFORE_EXTERNAL_EFFECT", "The verification was previously rejected by WP-SIPS.");
            }
            // Ambiguous earlier attempt: re-submit the stored signed bytes unchanged (the gateway answers EXACT_REPLAY).
            signed = Encoding.UTF8.GetString(operation.SignedRequest!);
        }

        PapssAdmissionResponse admission;
        try
        {
            admission = await _papssClient.SubmitSignedAsync(binding, signed, ct);
        }
        catch (ParticipantRailException rejected)
        {
            await papssStore.MarkGatewayRejectedAsync(operation.Id, rejected.Code, CancellationToken.None);
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await papssStore.MarkGatewaySubmissionUnknownAsync(operation.Id, error switch
            {
                UnauthorizedAccessException => "INVALID_SIGNED_RESPONSE",
                InvalidDataException => "INVALID_WP_SIPS_RESPONSE",
                OperationCanceledException => "WP_SIPS_TIMEOUT",
                _ => "WP_SIPS_UNAVAILABLE"
            }, CancellationToken.None);
            throw;
        }
        await papssStore.MarkGatewayAdmittedAsync(operation.Id, admission.Code, CancellationToken.None);
        return Ok(_jsonAdapter.Transform(admission, PapssAdmissionMapping));
    }

    private static bool SameVerification(PapssOperation operation, VerificationRequestDto query)
        => string.Equals(operation.CounterpartyBic, query.ToBIC?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(operation.AccountId, query.Alias, StringComparison.Ordinal)
           && string.Equals(operation.AccountType, query.Type, StringComparison.OrdinalIgnoreCase);

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
            return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.PayAsync(Binding(), query, ct), PapssAdmissionMapping)));
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
            return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.GetStatusAsync(Binding(), query, ct), PapssAdmissionMapping)));
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
            return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.ReturnAsync(Binding(), query, ct), PapssAdmissionMapping)));
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
        return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.GetReadinessAsync(Binding(), new(mapped.PapssId, mapped.Bic), ct), Constants.ReadinessResponse)));
    }

    [HttpPost("Discovery")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> Discovery([FromBody] JsonObject body, CancellationToken ct)
    {
        var mapped = _jsonAdapter.ToObject<ParticipantDiscoveryJsonRequest>(_jsonAdapter.Transform(body, Constants.ParticipantDiscoveryRequest));
        Select(ParticipantOperation.Discovery, mapped.Rail);
        return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.DiscoverAsync(Binding(), new(mapped.Online, mapped.Type, mapped.Bic, mapped.PapssId), ct), Constants.ParticipantDiscoveryResponse)));
    }

    [HttpPost("FX")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> Fx([FromBody] JsonObject body, CancellationToken ct)
    {
        var mapped = _jsonAdapter.ToObject<FxJsonRequest>(_jsonAdapter.Transform(body, Constants.FxRequest));
        Select(ParticipantOperation.Fx, mapped.Rail);
        return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssClient.GetFxAsync(Binding(), new(mapped.SenderCountry, mapped.ReceiverCountry, mapped.SenderCurrency, mapped.ReceiverCurrency, mapped.ReceiverBank, mapped.LocalInstrument, mapped.Amount, mapped.IsInvoice, mapped.InvoiceCurrency), ct), Constants.FxResponse)));
    }

    [HttpGet("PAPSS/Positions")]
    [Authorize(Roles = Gateway + "," + Recon)]
    public async Task<ActionResult> PapssPositions([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        Select(ParticipantOperation.Position, "PAPSS");
        return await Papss(async () => Ok(await _papssClient.GetPositionsAsync(Binding(), new(limit), ct)));
    }

    private DownstreamRail Select(ParticipantOperation operation, string? rail)
        => _operationRouter.Select(operation, rail);

    private PapssParticipantBinding Binding()
        => _operationRouter.ResolvePapss();

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
