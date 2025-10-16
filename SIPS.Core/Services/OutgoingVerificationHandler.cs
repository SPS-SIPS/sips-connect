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

namespace SIPS.Core.Services;

public sealed class OutgoingVerificationHandler(
    ISO20022Options options,
    ILogger<OutgoingVerificationHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IIncomingRecorder record,
    ISignatureService signature,
    IPersistenceGateway persistence,
    ICorrelationService correlation
    ) : IOutgoingVerificationHandler
{
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ISO20022Options _configuration = options;
    private readonly ILogger<OutgoingVerificationHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly INativeVerifier _verifier = verifier;
    private readonly IIncomingRecorder _record = record;
    private readonly ISignatureService _signature = signature;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;

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
            var record = await _persistence.RecordISOMessageAsync(isoMessage, ct);

            // Step 4: Send the verification request to SIPS
            var responseMessage = await SendRequestToSIPSAsync(signedRequest, url, ct, cid);
            record.Response = Encoding.UTF8.GetBytes(responseMessage.Data ?? "");

            // Step 5: Validate the SIPS response
            if (!responseMessage.IsSuccess || string.IsNullOrEmpty(responseMessage.Data))
            {
                await PersistISOMessageAsync(record, false, "Failed to receive valid response from SIPS", responseMessage.Message, responseMessage.Data ?? string.Empty, string.Empty, ct);
                return Response<VerificationResponseDto>.Fail(Transformers.TransformSIPSHttpError(responseMessage.StatusCode), responseMessage.StatusCode);
            }

            // Step 6: Verify the signature on the SIPS response
            var (ok, verbose) = await _signature.VerifyAsync(responseMessage.Data, ct);
            if (!ok)
            {
                await PersistISOMessageAsync(record, false, "Failed to verify the signature from SIPS", "Signature verification failed", responseMessage.Data, string.Empty, ct);
                _logger.LogError("[{CorrelationId}] Failed to verify the signature: verbose {Verbose}", cid, verbose);
                return Response<VerificationResponseDto>.Fail("Failed to verify the signature from SIPS.", System.Net.HttpStatusCode.BadRequest);
            }

            // Step 7: Parse and persist the SIPS response
            var parsedResponse = PayeeVerificationResponseBuilder.Parse(responseMessage.Data);
            await PersistISOMessageAsync(record, parsedResponse.Verified, parsedResponse.Reason ?? string.Empty, string.Empty, responseMessage.Data, parsedResponse.VerificationId ?? string.Empty, ct);

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

    private async Task<Response<string>> SendRequestToSIPSAsync(string message, string url, CancellationToken ct, string cid)
    {
        // Log the callback URL and payload
        _logger.LogInformation("[{CorrelationId}] Callback URL: {Url}", cid, url);
        _logger.LogInformation("[{CorrelationId}] Callback Payload: {Payload}", cid, message);
        var content = new StringContent(message, Encoding.UTF8, "application/xml");
        return await _httpClient.Send4XML(url, content, ct);
    }

    private async Task PersistISOMessageAsync(ISOMessage isoMessage, bool isVerified, string reason, string additionalInfo, string response, string originalId, CancellationToken ct)
    {
        isoMessage.Status = isVerified ? PostgreSQL.Enums.TransactionStatus.Success : PostgreSQL.Enums.TransactionStatus.Failed;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        isoMessage.Response = Encoding.UTF8.GetBytes(response);
        isoMessage.TxId = originalId;
        await _persistence.ISOMessageResponseAsync(isoMessage, ct);
    }
}
