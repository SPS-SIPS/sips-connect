using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SIPS.Adapter;
using SIPS.Core.Options;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using static SIPS.Core.Constants;

namespace SIPS.Core.Services;

/// <summary>Outcome of asking the core bank to verify an account for an inbound acmt.023.</summary>
/// <param name="BankReason">
/// The reason exactly as the core bank returned it (reason/status/message), empty when it gave none.
/// <see cref="PayeeVerificationResponseBuilder.Request.Reason"/> may carry the SIPS-rail default (SUCC/MISS)
/// instead; PAPSS replies must use this raw value and never a substituted default.
/// </param>
public sealed record CoreBankVerificationResult(
    bool Answered,
    PayeeVerificationResponseBuilder.Request Response,
    HttpStatusCode? StatusCode,
    string? FailureReason,
    string BankReason = "");

/// <summary>
/// Calls the core bank verification callback (CB_VerificationRequest / CB_VerificationResponse
/// mappings) for an inbound payee-verification request. Shared by the SIPS rail handler and the
/// PAPSS inbound enquiry service so both build the same core-bank request and parse the same answer.
/// </summary>
public interface ICoreBankVerificationClient
{
    /// <summary>
    /// Calls the core bank. <see cref="CoreBankVerificationResult.Answered"/> is true only when the core
    /// bank returned HTTP 200 with a body; otherwise the result is indeterminate/failed and
    /// <see cref="CoreBankVerificationResult.Response"/> carries no business answer.
    /// Uses the existing Core:CoreBankTimeoutSeconds budget.
    /// </summary>
    Task<CoreBankVerificationResult> VerifyAsync(PayeeVerificationBuilder.Request request, string correlationId, CancellationToken ct);
}

public sealed class CoreBankVerificationClient(
    ISO20022Options options,
    IJsonAdapter jsonAdapter,
    ICallbackOrchestrator callbacks,
    ICorrelationService correlation,
    ICallbackClient callback,
    IOptions<CoreOptions> coreOptions,
    ILogger logger) : ICoreBankVerificationClient
{
    private readonly CoreOptions _core = coreOptions.Value;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // DI constructor (ILogger<T> cannot be resolved as the non-generic ILogger).
    public CoreBankVerificationClient(
        ISO20022Options options,
        IJsonAdapter jsonAdapter,
        ICallbackOrchestrator callbacks,
        ICorrelationService correlation,
        ICallbackClient callback,
        IOptions<CoreOptions> coreOptions,
        ILogger<CoreBankVerificationClient> logger)
        : this(options, jsonAdapter, callbacks, correlation, callback, coreOptions, (ILogger)logger)
    {
    }

    public async Task<CoreBankVerificationResult> VerifyAsync(PayeeVerificationBuilder.Request request, string correlationId, CancellationToken ct)
    {
        var response = InitialResponse(request);
        using var coreBankCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        coreBankCts.CancelAfter(TimeSpan.FromSeconds(_core.CoreBankTimeoutSeconds > 0 ? _core.CoreBankTimeoutSeconds : 3));
        Response<JsonObject?>? result;
        try
        {
            // This asks the corebank to verify an account for an inbound PAPSS enquiry - it must reach
            // options.Verification itself, never the PAPSS participant's bank-notification CallbackUrl that a
            // participant binding would otherwise redirect it to (TVR UAT 2026-10-08: every inbound PAPSS
            // acmt.023 was answered from that CallbackUrl's response instead of a real corebank lookup).
            result = await callbacks.SendJsonAsync(
                options.Verification!, BuildHeaders(request), BuildRequest(request), CB_VerificationRequest,
                jsonAdapter, correlation, SerializerOptions, callback, coreBankCts.Token, correlationId, bypassParticipantBinding: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, response, HttpStatusCode.RequestTimeout, "CORE_BANK_TIMEOUT");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException)
        {
            logger.LogWarning(error, "[{CorrelationId}] Core bank verification call failed for VerificationId={VerificationId}", correlationId, request.SIPSRequestId);
            return new(false, response, null, "CORE_BANK_UNAVAILABLE");
        }

        if (result is { StatusCode: HttpStatusCode.OK, Data: not null })
        {
            var bankReason = ApplyResult(result.Data, response);
            return new(true, response, result.StatusCode, null, bankReason);
        }
        var reason = result is null ? "CORE_BANK_NO_RESPONSE"
            : result.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout ? "CORE_BANK_TIMEOUT"
            : "CORE_BANK_HTTP_" + (int)result.StatusCode;
        return new(false, response, result?.StatusCode, reason);
    }

    public static PayeeVerificationResponseBuilder.Request InitialResponse(PayeeVerificationBuilder.Request request) => new()
    {
        Original = request,
        VerificationId = request.SIPSRequestId ?? string.Empty,
        From = request.To,
        To = request.From,
        Type = request.Type
    };

    public Dictionary<string, string> BuildHeaders(PayeeVerificationBuilder.Request request)
    {
        var headers = new Dictionary<string, string>
        {
            { API_Key, options.Key! },
            { API_Secret, options.Secret! }
        };
        if (!string.IsNullOrWhiteSpace(request.SIPSRequestId))
            headers["X-Idempotency-Key"] = request.SIPSRequestId;
        return headers;
    }

    public CBVerificationRequestDto BuildRequest(PayeeVerificationBuilder.Request request)
    {
        // Normalize alias and type prior to CoreBank matching
        var normalizedAlias = request.Alias ?? string.Empty;
        var requestedType = request.Type;
        var normalizedType = requestedType ?? string.Empty;

        // [BUSINESS COMPLIANCE]: Strip legacy 'USD:' prefix and auto-detect IBAN for Somalia ISO standards.
        if (normalizedAlias.StartsWith("USD:", StringComparison.OrdinalIgnoreCase))
        {
            normalizedAlias = normalizedAlias.Substring(4);
        }
        if (normalizedAlias.StartsWith("SO", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalizedType, "ACCT", StringComparison.OrdinalIgnoreCase))
        {
            normalizedType = "IBAN";
        }

        var isMdaAccountLookup = !string.IsNullOrWhiteSpace(requestedType) && !IsBillLookupType(requestedType);
        var verificationRequestId = request.SIPSRequestId
            ?? request.MsgId
            ?? Guid.NewGuid().ToString("N");
        var callbackAgent = !string.IsNullOrWhiteSpace(options.Agent)
            ? options.Agent!
            : request.From;
        var payerBankCode = !string.IsNullOrWhiteSpace(options.PayerBankCode)
            ? options.PayerBankCode!
            : request.From;

        return new CBVerificationRequestDto
        {
            Alias = normalizedAlias,
            Type = isMdaAccountLookup ? normalizedType : null,
            FromBIC = request.From,
            VerificationId = verificationRequestId,
            InvoiceIdOrUpr = isMdaAccountLookup ? null : normalizedAlias,
            AccountNo = isMdaAccountLookup ? normalizedAlias : null,
            Agent = callbackAgent,
            VerificationRequestId = verificationRequestId,
            PayerBankCode = isMdaAccountLookup ? null : payerBankCode,
            PayerChannel = isMdaAccountLookup
                ? null
                : string.IsNullOrWhiteSpace(options.PayerChannel) ? "SIPS_CONNECT" : options.PayerChannel
        };
    }

    /// <returns>The business reason exactly as returned by the core bank (possibly empty).</returns>
    public string ApplyResult(JsonObject data, PayeeVerificationResponseBuilder.Request response)
    {
        logger.LogInformation("Callback Response: {Response}", data.ToJsonString(SerializerOptions));

        // First, transform the raw callback payload using our configured mapping
        // so fields like accountNo/accountType map to AccountNo/AccountType regardless of casing.
        JsonObject mapped;
        try
        {
            mapped = jsonAdapter.Transform(data, CB_VerificationResponse);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to transform callback payload using mapping '{MappingKey}'. Falling back to raw payload.", CB_VerificationResponse);
            mapped = data;
        }

        // Deserialize into our DTO with case-insensitive property matching
        var deserializedContent = jsonAdapter.ToObject<VerificationResponseDto>(mapped);
        logger.LogInformation("[ParseCallbackResult] Verification result for {Alias}: Verified={Verified}", response.Original?.Alias, deserializedContent?.IsVerified);

        response.Verified = deserializedContent?.IsVerified ?? false;
        var businessReason = FirstNonEmpty(
            deserializedContent?.Reason,
            deserializedContent?.Status,
            deserializedContent?.Message);
        response.Reason = response.Verified
            ? FirstNonEmpty(businessReason, SUCC)
            : FirstNonEmpty(businessReason, MISS);
        response.AdditionalInfo = FirstNonEmpty(
            deserializedContent?.Message,
            deserializedContent?.Reason,
            deserializedContent?.Status);
        response.Id = deserializedContent?.AccountNo ?? string.Empty;
        // Map Type from callback; default to IBAN only if unspecified
        response.Type = string.IsNullOrWhiteSpace(deserializedContent?.AccountType) ? IBAN : deserializedContent.AccountType;
        response.Name = FirstNonEmpty(
            deserializedContent?.Name,
            deserializedContent?.CreditorName,
            deserializedContent?.Address,
            deserializedContent?.Mda);
        response.Address = deserializedContent?.Address ?? string.Empty;
        response.Currency = FirstNonEmpty(deserializedContent?.Currency, deserializedContent?.PaymentCurrency);
        response.InvoiceId = deserializedContent?.InvoiceId;
        response.Upr = deserializedContent?.Upr;
        response.BillReference = FirstNonEmpty(
            deserializedContent?.BillReference,
            deserializedContent?.Upr,
            deserializedContent?.InvoiceId,
            response.Original?.Alias);
        response.Mda = deserializedContent?.Mda;
        response.MdaId = deserializedContent?.MdaId;
        response.MdaCode = deserializedContent?.MdaCode;
        response.AmountPayable = deserializedContent?.AmountPayable;
        return businessReason;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool IsBillLookupType(string value) =>
        string.Equals(value, "BILL", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "INVOICE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "UPR", StringComparison.OrdinalIgnoreCase);
}
