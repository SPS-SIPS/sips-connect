using System.Text.Json.Nodes;
using SIPS.Adapter;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using Microsoft.AspNetCore.Mvc;
namespace SIPS.Connect.Controllers;
[ApiController]
[Produces("application/json")]
[Route("api/v1/[controller]")]
public class GatewayController(
    IJsonAdapter jsonAdapter,
    IOutgoingVerificationHandler verificationService,
    IOutgoingTransactionHandler transactionService,
    IOutgoingTransactionStatusHandler transactionStatusService
    ) : ControllerBase
{
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IOutgoingVerificationHandler _verificationService = verificationService;
    private readonly IOutgoingTransactionHandler _transactionService = transactionService;
    private readonly IOutgoingTransactionStatusHandler _transactionStatusService = transactionStatusService;

    [HttpPost("Verify")]
    public async Task<ActionResult> VerifyPayee([FromBody] JsonObject body, CancellationToken ct)
    {
        JsonObject md = _jsonAdapter.Transform(body, "VerificationRequest");
        var query = _jsonAdapter.ToObject<VerificationRequestDto>(md);
        var response = await _verificationService.HandleAsync(query, ct);
        return APIResponseRenderer.GenerateAdminMessage(response, _jsonAdapter, "VerificationResponse");
    }

    [HttpPost("Payment")]
    public async Task<ActionResult> MakePayment([FromBody] JsonObject body, CancellationToken ct)
    {
        JsonObject md = _jsonAdapter.Transform(body, "PaymentRequest");
        var query = _jsonAdapter.ToObject<PaymentRequestDto>(md);
        var response = await _transactionService.HandleAsync(query, ct);
        return APIResponseRenderer.GenerateAdminMessage(response, _jsonAdapter, "PaymentResponse");
    }

    [HttpPost("Status")]
    public async Task<ActionResult> GetStatus([FromBody] JsonObject body, CancellationToken ct)
    {
        JsonObject md = _jsonAdapter.Transform(body, "StatusRequest");
        var query = _jsonAdapter.ToObject<StatusRequestDto>(md);
        var response = await _transactionStatusService.HandleAsync(query, ct);
        return APIResponseRenderer.GenerateAdminMessage(response, _jsonAdapter, "PaymentResponse");
    }
}