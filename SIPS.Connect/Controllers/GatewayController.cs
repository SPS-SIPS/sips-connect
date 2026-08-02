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
    IOptions<CoreOptions> coreOptions
    ) : ControllerBase
{
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IOutgoingVerificationHandler _verificationService = verificationService;
    private readonly IOutgoingTransactionHandler _transactionService = transactionService;
    private readonly IOutgoingTransactionStatusHandler _transactionStatusService = transactionStatusService;
    private readonly IOutgoingReturnTransactionHandler _returnTransactionService = returnTransactionService;
    private readonly IReturnRetryHandler _returnRetryHandler = returnRetryHandler;
    private readonly CoreOptions _coreOptions = coreOptions.Value;

    [HttpPost("Verify")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> VerifyPayee([FromBody] JsonObject body, CancellationToken ct)
    {
        JsonObject md = _jsonAdapter.Transform(body, VerificationRequest);
        var query = _jsonAdapter.ToObject<VerificationRequestDto>(md);
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
}
