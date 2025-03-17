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

namespace SIPS.Core.Services;

public sealed class OutgoingVerificationHandler(
    ISO20022Options options,
    ILogger<OutgoingVerificationHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IIncomingRecorder record
    ) : IOutgoingVerificationHandler
{
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ISO20022Options _configuration = options;
    private readonly ILogger<OutgoingVerificationHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly INativeVerifier _verifier = verifier;
    private readonly IIncomingRecorder _record = record;

    public async Task<Response<VerificationResponseDto>> HandleAsync(VerificationRequestDto message, CancellationToken ct)
    {
        // Validate configuration and request values.
        var fromBIC = _configuration.BIC ?? throw new InvalidOperationException("BIC not found in configuration.");
        var url = _configuration.SIPS ?? throw new InvalidOperationException("SIPS not found in configuration.");

        if (string.IsNullOrEmpty(message.Alias) ||
            string.IsNullOrEmpty(message.Type) ||
            string.IsNullOrEmpty(message.ToBIC))
        {
            return Response<VerificationResponseDto>.Fail("Failed to get proper values from the request!", System.Net.HttpStatusCode.BadRequest);
        }

        try
        {
            // Build and sign the verification request.
            if (!BuildRequest(message, fromBIC, out var signedRequest, out var bizMsgIdr, out var type))
            {
                return Response<VerificationResponseDto>.Fail("Failed to build acmt.023 message from your request", System.Net.HttpStatusCode.BadRequest);
            }

            // Create and persist the initial ISO message record.
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

            var record = await _record.ISOMessageAsync(isoMessage, ct);

            // Send the verification request to SIPS.
            var responseMessage = await SendRequestToSIPSAsync(signedRequest, url, ct);
            record.Response = Encoding.UTF8.GetBytes(responseMessage.Data ?? "");

            // Validate the SIPS response.
            if (!responseMessage.IsSuccess || string.IsNullOrEmpty(responseMessage.Data))
            {
                await PersistISOMessageAsync(record, false, "Failed to receive valid response from SIPS", responseMessage.Message, responseMessage.Data ?? "", "", ct);
                return Response<VerificationResponseDto>.Fail(Transformers.TransformSIPSHttpError(responseMessage.StatusCode), responseMessage.StatusCode);
            }

            // Verify the signature on the SIPS response.
            var (isSignatureValid, verbose) = await _verifier.VerifySignature(responseMessage.Data, false, ct);
            if (!isSignatureValid)
            {
                await PersistISOMessageAsync(record, false, "Failed to verify the signature from SIPS", "Signature verification failed", responseMessage.Data, "", ct);
                _logger.LogError("Failed to verify the signature: verbose {Verbose}", verbose);
                return Response<VerificationResponseDto>.Fail("Failed to verify the signature from SIPS.", System.Net.HttpStatusCode.BadRequest);
            }

            // Parse the SIPS response.
            var parsedResponse = PayeeVerificationResponseBuilder.Parse(responseMessage.Data);
            await PersistISOMessageAsync(record, parsedResponse.Verified, parsedResponse.Reason, string.Empty, responseMessage.Data, parsedResponse.Id, ct);

            return Response<VerificationResponseDto>.Success(new VerificationResponseDto
            {
                IsVerified = parsedResponse.Verified,
                SIPSRequestId = parsedResponse.VerificationId,
                Reason = parsedResponse.Reason,
                Id = parsedResponse.Verified ? parsedResponse.Id : null,
                Type = parsedResponse.Verified ? parsedResponse.Type : null,
                Name = parsedResponse.Verified ? parsedResponse.Name : null,
                Currency = parsedResponse.Verified ? parsedResponse.Currency : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process the verification request.");
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

    private async Task<Response<string>> SendRequestToSIPSAsync(string message, string url, CancellationToken ct)
    {
        _logger.LogDebug("Sending verification request to SIPS: {SignedMessage}", message);
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
        await _record.ISOMessageResponseAsync(isoMessage, ct);
    }
}
