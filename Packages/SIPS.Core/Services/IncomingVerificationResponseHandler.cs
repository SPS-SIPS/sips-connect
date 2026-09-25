using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SIPS.Adapter;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.XMLDsig.Xades.Interfaces;

namespace SIPS.Core.Services;

/// <summary>
/// Delivers an asynchronous payee-verification report (acmt.024.001.03) to the participant bank.
///
/// For PAPSS, SIPS Connect submits the acmt.023 enquiry to the PAPSS gateway and only receives a
/// technical admission synchronously (the bank gets <c>requestMessageId</c>). The business result
/// arrives later as a signed acmt.024 on the incoming endpoint. The report echoes the original
/// request identity (BAH Rltd/BizMsgIdr, OrgnlAssgnmt/MsgId and Rpt/OrgnlId = verification id), which
/// is forwarded to the bank so it can correlate the result with the <c>requestMessageId</c> it holds.
/// </summary>
public sealed class IncomingVerificationResponseHandler(
    ISO20022Options options,
    ILogger<IncomingVerificationResponseHandler> logger,
    INativeSigner signer,
    IJsonAdapter jsonAdapter,
    ISignatureService signature,
    ICorrelationService correlation,
    ICallbackClient callback,
    ICallbackOrchestrator callbacks,
    IInboundAuthenticationContext authentication,
    IVerificationResultInbox? inbox = null) : IIncomingVerificationResponseHandler, IVerificationResultDelivery
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        var cid = correlation.Create();

        if (!authentication.IsPreAuthenticated)
        {
            var (ok, _) = await signature.VerifyAsync(message, ct);
            if (!ok)
            {
                logger.LogWarning("[{CorrelationId}] Rejected verification report: signature verification failed.", cid);
                return signer.SignEnvelope(SipsReject.Create(message, AdminRejectReasonCodes.SignatureInvalid, "Failed to verify the signature of the verification report."));
            }
        }

        PayeeVerificationResponseBuilder.Request report;
        try
        {
            report = PayeeVerificationResponseBuilder.Parse(message);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "[{CorrelationId}] Rejected verification report: it could not be parsed.", cid);
            return signer.SignEnvelope(SipsReject.Create(message, AdminRejectReasonCodes.InvalidXml, "The verification report could not be parsed."));
        }

        var dto = Map(report);
        if (string.IsNullOrWhiteSpace(dto.RequestMessageId) || string.IsNullOrWhiteSpace(dto.VerificationId))
        {
            logger.LogWarning("[{CorrelationId}] Rejected verification report {BizMsgIdr}: original request references are missing.", cid, report.BizMsgIdr);
            return signer.SignEnvelope(SipsReject.Create(message, AdminRejectReasonCodes.MandatoryElementMissing, "The verification report does not reference the original request."));
        }

        if (authentication.IsPreAuthenticated && inbox is not null)
        {
            // PAPSS: store durably first and acknowledge; the bank is notified from the push outbox
            // (with retries), so a slow or failing bank callback can no longer make the gateway
            // believe SIPS Connect did not receive the result.
            var outcome = await inbox.AcceptAsync(message, report, dto, ct);
            if (outcome != VerificationResultInboxOutcome.NotHandled)
            {
                logger.LogInformation(
                    "[{CorrelationId}] Verification result {ResponseMessageId} for RequestMessageId={RequestMessageId} {Outcome}; bank delivery is asynchronous.",
                    cid, dto.ResponseMessageId, dto.RequestMessageId, outcome == VerificationResultInboxOutcome.Duplicate ? "was a duplicate" : "stored");
                return string.Empty;
            }
        }

        await DeliverAsync(dto, cid, ct);
        return string.Empty;
    }

    public Task DeliverAsync(CBVerificationResultDto dto, CancellationToken ct) => DeliverAsync(dto, correlation.Create(), ct);

    private async Task DeliverAsync(CBVerificationResultDto dto, string cid, CancellationToken ct)
    {
        var headers = new Dictionary<string, string>
        {
            // One result per verification: lets the bank de-duplicate redeliveries.
            ["X-Idempotency-Key"] = dto.VerificationId
        };
        if (!string.IsNullOrWhiteSpace(options.Key)) headers[Constants.API_Key] = options.Key;
        if (!string.IsNullOrWhiteSpace(options.Secret)) headers[Constants.API_Secret] = options.Secret;

        logger.LogInformation(
            "[{CorrelationId}] Delivering verification result RequestMessageId={RequestMessageId} VerificationId={VerificationId} Verified={Verified} Reason={Reason}",
            cid, dto.RequestMessageId, dto.VerificationId, dto.Verified, dto.Reason);

        SIPS.ISO20022.Models.DTOs.Response<System.Text.Json.Nodes.JsonObject?> result;
        try
        {
            result = await callbacks.SendJsonAsync(
                options.Verification ?? string.Empty,
                headers,
                dto,
                Constants.CB_VerificationResult,
                jsonAdapter,
                correlation,
                SerializerOptions,
                callback,
                ct,
                cid);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "[{CorrelationId}] Verification result {RequestMessageId} could not be delivered to the bank callback.", cid, dto.RequestMessageId);
            throw new CallbackDeliveryException("The verification result could not be delivered to the participant callback.", null, error);
        }

        if (result is null || (int)result.StatusCode is < 200 or > 299)
        {
            logger.LogError("[{CorrelationId}] Bank callback rejected verification result {RequestMessageId} with status {StatusCode}: {Message}",
                cid, dto.RequestMessageId, result?.StatusCode, result?.Message);
            throw new CallbackDeliveryException("The participant callback did not acknowledge the verification result.", result?.StatusCode);
        }

        logger.LogInformation("[{CorrelationId}] Verification result {RequestMessageId} delivered with status {StatusCode}.", cid, dto.RequestMessageId, result.StatusCode);
    }

    public static CBVerificationResultDto Map(PayeeVerificationResponseBuilder.Request report)
    {
        var original = report.Original ?? new PayeeVerificationBuilder.Request();
        return new CBVerificationResultDto
        {
            RequestMessageId = FirstNonEmpty(original.BizMsgIdr, report.MsgId, original.MsgId),
            OriginalMsgId = FirstNonEmpty(report.MsgId, original.MsgId),
            VerificationId = FirstNonEmpty(report.VerificationId, original.SIPSRequestId),
            Verified = report.Verified,
            AccountNumber = FirstNonEmpty(report.Id, original.Alias),
            AccountType = FirstNonEmpty(report.Type, original.Type),
            AccountName = NullIfEmpty(report.Name),
            Address = NullIfEmpty(report.Address),
            Currency = NullIfEmpty(report.Currency),
            Reason = FirstNonEmpty(report.Reason, report.Verified ? Constants.SUCC : Constants.MISS),
            AdditionalInfo = NullIfEmpty(report.AdditionalInfo),
            FromBIC = report.From,
            ToBIC = report.To,
            ResponseMessageId = report.BizMsgIdr ?? string.Empty
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
}
