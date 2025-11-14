using System.Text;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Correlation;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;

namespace SIPS.Core.Services;

public sealed class OutgoingVerificationHandler(
    ISO20022Options options,
    ILogger<OutgoingVerificationHandler> logger,
    INativeSigner signer,
    ISignatureService signature,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    SIPS.Core.Services.Abstractions.ISipsRequestSender sips,
    SIPS.Core.Services.Abstractions.IStatusOrchestrator statusOrchestrator,
    IOptions<CoreOptions> coreOptions
    ) : IOutgoingVerificationHandler
{
    private readonly ISO20022Options _configuration = options;
    private readonly ILogger<OutgoingVerificationHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly ISignatureService _signature = signature;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly SIPS.Core.Services.Abstractions.ISipsRequestSender _sips = sips;
    private readonly SIPS.Core.Services.Abstractions.IStatusOrchestrator _statusOrchestrator = statusOrchestrator;
    private readonly CoreOptions _core = coreOptions.Value;

    public async Task<Response<VerificationResponseDto>> HandleAsync(VerificationRequestDto message, CancellationToken ct)
    {
        // Step 1: Validate configuration and request values
        var fromBIC = _configuration.BIC ?? throw new InvalidOperationException("BIC not found in configuration.");
        var url = _configuration.SIPS ?? throw new InvalidOperationException("SIPS not found in configuration.");
        var cid = _correlation.Create();

        if (string.IsNullOrEmpty(message.Alias) ||
            string.IsNullOrEmpty(message.Type) ||
            string.IsNullOrEmpty(message.ToBIC))
        {
            return Response<VerificationResponseDto>.Fail("Failed to get proper values from the request!", System.Net.HttpStatusCode.BadRequest);
        }

        try
        {
            // Step 2: Build and sign the verification request
            if (!BuildRequest(message, fromBIC, out var signedRequest, out var bizMsgIdr, out var type))
            {
                return Response<VerificationResponseDto>.Fail("Failed to build acmt.023 message from your request", System.Net.HttpStatusCode.BadRequest);
            }

            // Step 3: Create and persist the initial ISO message record
            var isoMessage = new ISOMessage
            {
                MessageType = PostgreSQL.Enums.ISOMessageType.VerificationRequest,
                Date = DateTimeOffset.UtcNow,
                FromBIC = fromBIC,
                ToBIC = message.ToBIC,
                Message = Encoding.UTF8.GetBytes(signedRequest),
                Status = PostgreSQL.Enums.TransactionStatus.Pending,
                BizMsgIdr = bizMsgIdr,
                MsgDefIdr = type,
            };
            using var dbCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            var dbCt = dbCts.Token;
            var record = await _persistence.RecordISOMessageAsync(isoMessage, dbCt);

            // Step 4: Send the verification request to SIPS
            var responseMessage = await _sips.SendAsync(url, signedRequest, ct, cid);
            record.Response = Encoding.UTF8.GetBytes(responseMessage.Data ?? "");

            // Step 5: Validate the IPS response
            if (!responseMessage.IsSuccess || string.IsNullOrEmpty(responseMessage.Data))
            {
                var failedStatus = _statusOrchestrator.MapSingleStatus("RJCT", "IPS");
                await PersistISOMessageAsync(record, failedStatus, "Failed to receive valid response from IPS", responseMessage.Message, responseMessage.Data ?? string.Empty, string.Empty, dbCt);
                return Response<VerificationResponseDto>.Fail(Transformers.TransformSIPSHttpError(responseMessage.StatusCode), responseMessage.StatusCode);
            }

            // Step 6: Verify the signature on the IPS response
            var (ok, verbose) = await _signature.VerifyAsync(responseMessage.Data, ct);
            if (!ok)
            {
                var failedStatus = _statusOrchestrator.MapSingleStatus("RJCT", "IPS");
                await PersistISOMessageAsync(record, failedStatus, "Failed to verify the signature from IPS", "Signature verification failed", responseMessage.Data, string.Empty, dbCt);
                _logger.LogError("[{CorrelationId}] Failed to verify the signature: verbose {Verbose}", cid, verbose);
                return Response<VerificationResponseDto>.Fail("Failed to verify the signature from IPS.", System.Net.HttpStatusCode.BadRequest);
            }

            // Step 7: Parse and persist the IPS response
            var parsedResponse = PayeeVerificationResponseBuilder.Parse(responseMessage.Data);

            // Use StatusOrchestrator to map verification result consistently
            // Verified=true → SUCC, Verified=false → MISS
            var statusCode = parsedResponse.Verified ? "SUCC" : "MISS";
            var finalStatus = _statusOrchestrator.MapSingleStatus(statusCode, "IPS");

            await PersistISOMessageAsync(record, finalStatus, parsedResponse.Reason ?? string.Empty, string.Empty, responseMessage.Data, parsedResponse.VerificationId ?? string.Empty, dbCt);

            // Step 8: Return success response
            return Response<VerificationResponseDto>.Success(new VerificationResponseDto
            {
                IsVerified = parsedResponse.Verified,
                SIPSRequestId = parsedResponse.VerificationId ?? string.Empty,
                Reason = parsedResponse.Reason ?? string.Empty,
                Id = parsedResponse.Verified ? parsedResponse.Id : null,
                Type = parsedResponse.Verified ? parsedResponse.Type : null,
                Name = parsedResponse.Verified ? parsedResponse.Name : null,
                Currency = parsedResponse.Verified ? parsedResponse.Currency : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Failed to process the verification request.", cid);
            return Response<VerificationResponseDto>.Fail("Failed to process the request", System.Net.HttpStatusCode.InternalServerError);
        }
    }

    private bool BuildRequest(VerificationRequestDto message, string fromBIC, out string signedMessage, out string bizMsgIdr, out string type)
    {
        (string document, bizMsgIdr, type) = PayeeVerificationBuilder.Build(new PayeeVerificationBuilder.Request
        {
            From = fromBIC,
            Alias = message.Alias,
            Type = message.Type,
            To = message.ToBIC,
        });

        if (document != null)
        {
            signedMessage = _signer.SignEnvelope(document);
            return !string.IsNullOrEmpty(signedMessage);
        }
        else
        {
            signedMessage = string.Empty;
            return false;
        }
    }



    private async Task PersistISOMessageAsync(ISOMessage isoMessage, PostgreSQL.Enums.TransactionStatus status, string reason, string additionalInfo, string response, string originalId, CancellationToken ct)
    {
        isoMessage.Status = status;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        isoMessage.Response = Encoding.UTF8.GetBytes(response);
        isoMessage.TxId = originalId;
        await _persistence.ISOMessageResponseAsync(isoMessage, ct);
    }
}
