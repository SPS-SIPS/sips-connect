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
    IPapssFacingSipsClient papssClient,
    PapssOperationStore papssStore,
    PapssFacingOptions papssOptions,
    TimeProvider clock,
    IPapssPaymentService? papssPayments = null
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
    private readonly IPapssPaymentService _papssPayments = papssPayments
        ?? new PapssPaymentService(papssStore, papssClient, papssOptions, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<PapssPaymentService>.Instance);

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

    /// <summary>Pull API: the stored PAPSS payment (OUTBOUND wins over a received payment with the same TxId).</summary>
    [HttpGet("Payment/{txId}")]
    [Authorize(Roles = Gateway)]
    public Task<ActionResult> GetPayment([FromRoute] string txId, [FromQuery] int? waitSeconds, CancellationToken ct)
        => LookupAsync(txId, "txId", ct => papssStore.FindPaymentAsync(txId, ct), "No PAPSS payment is stored for this txId.", waitSeconds, ct);

    /// <summary>Pull API: the stored PAPSS return by RtrId (OUTBOUND wins over a received return with the same id).</summary>
    [HttpGet("Return/{returnId}")]
    [Authorize(Roles = Gateway)]
    public Task<ActionResult> GetReturnOperation([FromRoute] string returnId, [FromQuery] int? waitSeconds, CancellationToken ct)
        => LookupAsync(returnId, "returnId", ct => papssStore.FindReturnAsync(returnId, ct), "No PAPSS return is stored for this returnId.", waitSeconds, ct);

    /// <summary>Pull API: a recall (camt.056) by its recallId, with its answers and the recalled payment's outcome.</summary>
    [HttpGet("Recall/{recallId}")]
    [Authorize(Roles = Gateway)]
    public Task<ActionResult> GetRecall([FromRoute] string recallId, [FromQuery] int? waitSeconds, CancellationToken ct)
        => LookupAsync(recallId, "recallId", ct => papssStore.FindOutboundRecallAsync(recallId, ct), "No PAPSS recall is stored for this recallId.", waitSeconds, ct);

    /// <summary>R2: pull API for an inbound recall (a counterparty recalling a payment this institution received), keyed by its PAPSS source message id.</summary>
    [HttpGet("Recall/Inbound/{recallId}")]
    [Authorize(Roles = Gateway)]
    public Task<ActionResult> GetInboundRecall([FromRoute] string recallId, [FromQuery] int? waitSeconds, CancellationToken ct)
        => LookupAsync(recallId, "recallId", ct => papssStore.FindInboundRecallAsync(recallId, ct), "No inbound PAPSS recall is stored for this recallId.", waitSeconds, ct);

    /// <summary>R2: inbound recalls still awaiting a decision (there is no bank-push notification for this event yet).</summary>
    [HttpGet("Recall/Inbound")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> PendingInboundRecalls([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var pending = await papssStore.PendingInboundRecallsAsync(limit, ct);
        var results = new List<object>(pending.Count);
        foreach (var operation in pending) results.Add(_jsonAdapter.Transform(await _papssPayments.DescribeAsync(operation, ct), OperationResultMapping));
        return Ok(results);
    }

    /// <summary>Pull API: the stored PAPSS payment by EndToEndId (<c>?endToEndId=</c>).</summary>
    [HttpGet("Operations")]
    [Authorize(Roles = Gateway)]
    public Task<ActionResult> FindOperation([FromQuery] string? endToEndId, [FromQuery] int? waitSeconds, CancellationToken ct)
        => LookupAsync(endToEndId ?? string.Empty, "endToEndId", ct => papssStore.FindPaymentByEndToEndIdAsync(endToEndId!, ct), "No PAPSS payment is stored for this endToEndId.", waitSeconds, ct);

    /// <summary>Operator view: received PAPSS pacs.002/pacs.004/camt.029 that are uncorrelated, conflicting or carry an unknown status.</summary>
    [HttpGet("Operations/Unresolved")]
    [Authorize(Roles = Gateway + "," + Recon)]
    public async Task<ActionResult> UnresolvedPapssEvents([FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok((await papssStore.UnresolvedStatusEventsAsync(limit, ct)).Select(e => new
        {
            receivedAt = PapssOperationResult.Iso(e.ReceivedAt),
            eventType = e.EventType,
            messageType = e.MessageType,
            sourceMessageId = e.SourceMessageId,
            status = e.Status,
            reasonCode = e.ReasonCode,
            correlation = e.Correlation,
            disposition = e.Disposition,
            originalMessageId = e.OriginalMessageId,
            originalMessageType = e.OriginalMessageType,
            originalTxId = e.OriginalTxId,
            originalEndToEndId = e.OriginalEndToEndId,
            amount = e.Amount,
            currency = e.Currency,
            amountSource = e.AmountSource,
            categoryPurposeSource = e.CategoryPurposeSource,
            rawEvidenceReference = e.RawEvidenceReference,
            attached = e.OperationId is not null,
            note = e.Note
        }));

    private Task<ActionResult> LookupAsync(string requestMessageId, bool outboundVerificationOnly, int? waitSeconds, CancellationToken ct)
        => LookupAsync(requestMessageId, "requestMessageId", ct => papssStore.FindAsync(requestMessageId, outboundVerificationOnly, ct), "No PAPSS operation is stored for this requestMessageId.", waitSeconds, ct);

    private async Task<ActionResult> LookupAsync(string key, string keyName, Func<CancellationToken, Task<PapssOperation?>> find, string notFound, int? waitSeconds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            return BadRequest(new { code = keyName == "requestMessageId" ? "INVALID_REQUEST_MESSAGE_ID" : "INVALID_LOOKUP_KEY", message = $"{keyName} is required (max 128 characters)." });
        // Long-poll only when an operator enabled it: PapssFacing:Lookup:MaxWaitSeconds (default 0).
        var wait = TimeSpan.FromSeconds(Math.Clamp(waitSeconds ?? 0, 0, Math.Max(0, papssOptions.Lookup.MaxWaitSeconds)));
        var until = clock.GetUtcNow() + wait;
        while (true)
        {
            var operation = await find(ct);
            if (operation is null)
                return NotFound(new { code = "OPERATION_NOT_FOUND", message = notFound });
            var result = await _papssPayments.DescribeAsync(operation, ct);
            if (result.Status != PapssOperationResult.Pending || clock.GetUtcNow() >= until)
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
            return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssPayments.PayAsync(Binding(), query, ct), PapssAdmissionMapping)));
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
            return await Papss(async () => Ok(await StatusViaPapssAsync(query, ct)));
        var response = await _transactionStatusService.HandleAsync(query, ct);
        return GenerateAdminMessage(response, _jsonAdapter, PaymentResponse);
    }

    /// <summary>
    /// /Status on the PAPSS rail: the stored, authoritative state (PaymentResponse mapping + stored fields). A pacs.028 is
    /// only sent while the stored payment is non-final; a TxId unknown to the store keeps the previous behaviour
    /// (pacs.028 sent, admission returned).
    /// </summary>
    private async Task<JsonObject> StatusViaPapssAsync(StatusRequestDto query, CancellationToken ct)
    {
        var result = await _papssPayments.StatusAsync(Binding(), query, ct);
        if (result.Payment is not { } payment)
            return _jsonAdapter.Transform(result.Enquiry!, PapssAdmissionMapping);

        var json = _jsonAdapter.Transform(new PaymentResponseDto
        {
            Status = payment.PaymentStatus!,
            Reason = payment.StatusReasonCode ?? payment.ReasonCode,
            AdditionalInfo = payment.AdditionalInfo,
            TxId = payment.TxId ?? query.TxId,
            EndToEndId = payment.EndToEndId ?? query.EndToEnd
        }, PaymentResponse);
        json["requestMessageId"] = payment.RequestMessageId;
        json["paymentOutcome"] = UpperSnakeEnumConverter<PapssOutcome>.Of(payment.PapssOutcome);
        json["operation"] = _jsonAdapter.Transform(await _papssPayments.DescribeAsync(payment, ct), OperationResultMapping);
        json["statusEnquiry"] = result.Enquiry is null && result.EnquiryError is null ? null : new JsonObject
        {
            ["requestMessageId"] = result.Enquiry?.RequestMessageId ?? result.EnquiryRequestMessageId,
            ["code"] = result.Enquiry?.Code ?? result.EnquiryError,
            ["durablyAdmitted"] = result.Enquiry?.DurablyAdmitted ?? false
        };
        return json;
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
            return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssPayments.ReturnAsync(Binding(), query, ct), PapssAdmissionMapping)));
        var response = await _returnTransactionService.HandleAsync(query, ct);
        return GenerateAdminMessage(response, _jsonAdapter, PaymentResponse);
    }


    /// <summary>
    /// Recalls (camt.056.001.08) one of this participant's settled OUTBOUND PAPSS payments. The recall is stored before it is
    /// submitted; the response is the gateway admission (requestMessageId = recallId). PAPSS's answer and the beneficiary's
    /// answer arrive later on the bank callback (CB_RecallResult) and on GET Recall/{recallId}.
    /// </summary>
    [HttpPost("Recall")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> Recall([FromBody] JsonObject body, CancellationToken ct)
    {
        if (_coreOptions.VerificationOnlyMode)
        {
            return BadRequest(new { Error = "SIPS Connect is configured in Verification Only mode. This operation is not allowed." });
        }

        var query = _jsonAdapter.ToObject<PapssRecallRequest>(_jsonAdapter.Transform(body, RecallRequest));
        try
        {
            return await Papss(async () => Ok(_jsonAdapter.Transform(await _papssPayments.RecallAsync(Binding(), query, ct), PapssAdmissionMapping)));
        }
        catch (ParticipantRailException e) when (RecallRefusalStatus(e.Code) is { } status)
        {
            return StatusCode(status, new { code = e.Code, message = e.Message });
        }
    }

    /// <summary>
    /// R2: the core bank's accept/reject decision on an inbound recall (a counterparty recalling a payment this institution
    /// received, visible via GET Recall/Inbound). ACCEPT submits a pacs.004 via the existing return path; REJECT submits a
    /// camt.029.001.08 rejection. Both are idempotent on retry (same recallId + same decision replays safely); a retry with
    /// the OPPOSITE decision, or any retry once the recall has already reached REPLY_SUBMITTED, is refused (409).
    /// </summary>
    [HttpPost("Recall/Inbound/{recallId}/Decision")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> DecideInboundRecall([FromRoute] string recallId, [FromBody] PapssInboundRecallDecisionRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recallId) || recallId.Length > 128)
            return BadRequest(new { code = "INVALID_LOOKUP_KEY", message = "recallId is required (max 128 characters)." });
        try
        {
            return await Papss(async () => Ok(await _papssPayments.DecideInboundRecallAsync(Binding(), recallId, body, ct)));
        }
        catch (ParticipantRailException e) when (RecallRefusalStatus(e.Code) is { } status)
        {
            return StatusCode(status, new { code = e.Code, message = e.Message });
        }
    }

    /// <summary>Recall refusals raised before anything is submitted; other codes keep the common PAPSS mapping (400).</summary>
    private static int? RecallRefusalStatus(string code) => code switch
    {
        "ORIGINAL_PAYMENT_NOT_FOUND" or "OPERATION_NOT_FOUND" => StatusCodes.Status404NotFound,
        "RECALL_ALREADY_OPEN" or "ORIGINAL_NOT_SETTLED" or "RECALL_WINDOW_EXPIRED" or "DECISION_ALREADY_MADE" => StatusCodes.Status409Conflict,
        "RECALL_NOT_ALLOWED" => StatusCodes.Status422UnprocessableEntity,
        _ => null
    };

    /// <summary>
    /// The authenticated caller's own identity for audit purposes: the API key's configured Name (already the sole claim
    /// carrying it), or - for a Keycloak/JWT operator, where this app maps no realm claim onto ClaimTypes.Name - the first
    /// of the claims Keycloak tokens actually carry for this: preferred_username, then the subject.
    /// </summary>
    private static string? CallerIdentity(ClaimsPrincipal? user)
        => user?.FindFirst(ClaimTypes.Name)?.Value
            ?? user?.FindFirst("preferred_username")?.Value
            ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.FindFirst("sub")?.Value;

    /// <summary>
    /// Operator recovery: manually closes a recall stuck OPEN, for example RECALL_OUTCOME_UNRESOLVED (the gateway could not
    /// read a definite PAPSS outcome for the camt.056). Sets the final RECALL_ABANDONED outcome, releases the one-open-recall
    /// lock (ux_papss_op_open_recall) so a new recall may be submitted for the payment, pushes CB_RecallResult, and records
    /// who/when/why (and which of the two paths below authenticated the call) as an audited papss_operation_events entry.
    /// Manual only: SIPS Connect never applies this by itself.
    ///
    /// Two authorized paths, never anonymous: the human/operator role (Recon, same as Retry and the other reconciliation
    /// endpoints) via Keycloak; or a narrow, per-API-key machine capability (KnownRoles.RecallClose, granted only to the
    /// specific API key configured with it via ApiKey.Roles) for an API party that needs to close its own stuck recalls
    /// without the broader Recon role.
    /// </summary>
    [HttpPost("Recall/{recallId}/Close")]
    [Authorize(Roles = Recon + "," + RecallClose)]
    public async Task<ActionResult> CloseRecall([FromRoute] string recallId, [FromBody] PapssRecallCloseRequest body, CancellationToken ct)
    {
        var reason = body?.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return UnprocessableEntity(new { code = "PAPSS_VALIDATION_FAILED", message = "A reason is required to close a recall manually." });
        if (string.IsNullOrWhiteSpace(recallId) || recallId.Length > 128)
            return BadRequest(new { code = "INVALID_LOOKUP_KEY", message = "recallId is required (max 128 characters)." });

        var closedBy = CallerIdentity(User);
        // The auth path reflects which scheme actually authenticated the caller (ApiKey vs. Keycloak/JWT), not which
        // role happened to satisfy [Authorize] above - an operator is never mistaken for an API party or vice versa.
        var authPath = User?.Identity is ClaimsIdentity { AuthenticationType: ApiKeyDefaults.AuthenticationScheme }
            ? PapssRecallCloseAuthPath.ApiParty
            : PapssRecallCloseAuthPath.Operator;
        var (outcome, recall) = await papssStore.CloseRecallAsync(recallId, reason, closedBy, authPath, ct);
        return outcome switch
        {
            PapssRecallCloseOutcome.NotFound => NotFound(new { code = "OPERATION_NOT_FOUND", message = "No PAPSS recall is stored for this recallId." }),
            PapssRecallCloseOutcome.AlreadyClosed => Conflict(new
            {
                code = "RECALL_ALREADY_CLOSED",
                message = $"Recall {recallId} is already {UpperSnakeEnumConverter<PapssOutcome>.Of(recall!.PapssOutcome)}; it cannot be closed again."
            }),
            _ => Ok(_jsonAdapter.Transform(await _papssPayments.DescribeAsync(recall!, ct), OperationResultMapping))
        };
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
        catch (ParticipantRailException e) when (RecallRefusalStatus(e.Code) is not null) { throw; }
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
