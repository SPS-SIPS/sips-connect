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
namespace SIPS.Connect.Controllers;
[ApiController]
[Produces("application/json")]
[Route("api/v1/[controller]")]
public class GatewayController(
    IJsonAdapter jsonAdapter,
    IOutgoingVerificationHandler verificationService,
    IOutgoingTransactionHandler transactionService,
    IOutgoingTransactionStatusHandler transactionStatusService,
    IOutgoingReturnTransactionHandler returnTransactionService
    ) : ControllerBase
{
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IOutgoingVerificationHandler _verificationService = verificationService;
    private readonly IOutgoingTransactionHandler _transactionService = transactionService;
    private readonly IOutgoingTransactionStatusHandler _transactionStatusService = transactionStatusService;
    private readonly IOutgoingReturnTransactionHandler _returnTransactionService = returnTransactionService;

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
        JsonObject md = _jsonAdapter.Transform(body, PaymentRequest);
        var query = _jsonAdapter.ToObject<PaymentRequestDto>(md);
        var response = await _transactionService.HandleAsync(query, ct);
        return GenerateAdminMessage(response, _jsonAdapter, PaymentResponse);
    }

    [HttpPost("Status")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> GetStatus([FromBody] JsonObject body, CancellationToken ct)
    {
        JsonObject md = _jsonAdapter.Transform(body, StatusRequest);
        var query = _jsonAdapter.ToObject<StatusRequestDto>(md);
        var response = await _transactionStatusService.HandleAsync(query, ct);
        return GenerateAdminMessage(response, _jsonAdapter, PaymentResponse);
    }

    [HttpPost("Return")]
    [Authorize(Roles = Gateway)]
    public async Task<ActionResult> GetReturn([FromBody] JsonObject body, CancellationToken ct)
    {
        JsonObject md = _jsonAdapter.Transform(body, ReturnRequest);
        var query = _jsonAdapter.ToObject<ReturnPaymentRequestDto>(md);
        Console.WriteLine("md.OriginalTxId" + query.OriginalTxId);
        var response = await _returnTransactionService.HandleAsync(query, ct);
        return GenerateAdminMessage(response, _jsonAdapter, PaymentResponse);
    }
}