using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using SIPS.Connect.Config;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Enums;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Models.WpSips;
using SIPS.XMLDsig.Xades.Interfaces;

namespace SIPS.Connect.Services;

public interface IPapssFacingSipsClient
{
    Task<PapssAdmissionResponse> VerifyAsync(PapssParticipantBinding participant, VerificationRequestDto request, CancellationToken ct);
    Task<PapssAdmissionResponse> PayAsync(PapssParticipantBinding participant, PaymentRequestDto request, CancellationToken ct);
    Task<PapssAdmissionResponse> GetStatusAsync(PapssParticipantBinding participant, StatusRequestDto request, CancellationToken ct);
    Task<PapssAdmissionResponse> ReturnAsync(PapssParticipantBinding participant, ReturnPaymentRequestDto request, CancellationToken ct);
    Task<ReadinessResponse> GetReadinessAsync(PapssParticipantBinding participant, ReadinessRequest request, CancellationToken ct);
    Task<ParticipantDiscoveryResponse> DiscoverAsync(PapssParticipantBinding participant, ParticipantDiscoveryRequest request, CancellationToken ct);
    Task<FxRateResponse> GetFxAsync(PapssParticipantBinding participant, FxRateRequest request, CancellationToken ct);
}

public sealed record PapssAdmissionResponse(string RequestMessageId, string Code, bool DurablyAdmitted);

public sealed class PapssFacingSipsClient(
    PapssFacingOptions options,
    HttpClient http,
    INativeSigner signer,
    INativeVerifier verifier,
    IPapssHealthState health,
    ILogger<PapssFacingSipsClient> logger) : IPapssFacingSipsClient
{
    public async Task<PapssAdmissionResponse> VerifyAsync(PapssParticipantBinding participant, VerificationRequestDto request, CancellationToken ct)
    {
        Required(request.ToBIC, "destination institution"); Required(request.Alias, "account identifier"); Required(request.Type, "account type");
        var id = Id();
        var requestMsgId = request.MsgId ?? id;
        var unsigned = PayeeVerificationBuilder.Build(new() { From = participant.Bic, To = request.ToBIC, Alias = request.Alias, Type = request.Type, MsgId = requestMsgId, SIPSRequestId = requestMsgId }).document;
        return await SendAdmissionAsync(unsigned, participant, ct);
    }

    public async Task<PapssAdmissionResponse> PayAsync(PapssParticipantBinding participant, PaymentRequestDto request, CancellationToken ct)
    {
        ValidateCorridor(participant, request);
        var txId = Required(request.TxId, "transaction identifier");
        var built = PaymentRequestBuilder.Build(new()
        {
            From = participant.Bic, To = request.ToBIC, LocalInstrument = request.LocalInstrument,
            CategoryPurpose = request.CategoryPurpose, EndToEndId = request.EndToEndId, TxId = txId,
            Amount = request.Amount, Currency = request.Currency, Ustrd = request.RemittanceInformation,
            Debtor = new() { Name = request.DebtorName, Account = request.DebtorAccount, Address = request.DebtorAddress, AccountType = request.DebtorAccountType, AgentBIC = request.DebtorAgentBIC, Issuer = request.DebtorIssuer },
            Creditor = new() { Name = request.CreditorName, Account = request.CreditorAccount, Address = request.CreditorAddress, AccountType = request.CreditorAccountType, AgentBIC = request.CreditorAgentBIC, Issuer = request.CreditorIssuer },
            PapssCorridor = new(request.SenderCountry!, request.ReceiverCountry!, request.SenderCurrency!, request.ReceiverCurrency!)
        });
        return await SendAdmissionAsync(built.document, participant, ct);
    }

    public async Task<PapssAdmissionResponse> GetStatusAsync(PapssParticipantBinding participant, StatusRequestDto request, CancellationToken ct)
    {
        Required(request.TxId, "transaction identifier"); Required(request.EndToEnd, "end-to-end identifier"); Required(request.ToBIC, "destination institution");
        var requestMsgId = Id();
        var unsigned = PaymentStatusRequestBuilder.Build(new() { From = participant.Bic, To = request.ToBIC, MsgId = requestMsgId, CreDt = DateTime.UtcNow, OrgnlTxId = request.TxId, OriginalEndToEnd = request.EndToEnd });
        return await SendAdmissionAsync(unsigned, participant, ct);
    }

    public async Task<PapssAdmissionResponse> ReturnAsync(PapssParticipantBinding participant, ReturnPaymentRequestDto request, CancellationToken ct)
    {
        Required(request.OriginalTxId, "original transaction identifier"); Required(request.OriginalEndToEndId, "original end-to-end identifier"); Required(request.ReturnId, "return identifier"); Required(request.ToBIC, "destination institution"); Required(request.LocalInstrument, "local instrument"); Required(request.CategoryPurpose, "category purpose");
        var built = ReturnPaymentRequestBuilder.Build(new() { From = participant.Bic, To = request.ToBIC, MsgId = Id(), CreDt = DateTime.UtcNow, NumberOfTransactions = 1, LocalInstrument = request.LocalInstrument, CategoryPurpose = request.CategoryPurpose, ReturnId = request.ReturnId, OrgnlTxId = request.OriginalTxId, OriginalEndToEnd = request.OriginalEndToEndId, OriginalCurrency = Required(request.OriginalCurrency, "original currency"), OriginalAmount = request.OriginalAmount, ReturnReason = request.Reason, AdditionalInfo = request.AdditionalInfo, DebtorAgent = participant.Bic, CreditorAgent = request.ToBIC });
        return await SendAdmissionAsync(built.document, participant, ct);
    }

    public async Task<ReadinessResponse> GetReadinessAsync(PapssParticipantBinding participant, ReadinessRequest request, CancellationToken ct)
        => Payload<ReadinessResponse>(await Information(participant, WpSipsProfiles.Readiness, (h, id) => WpSipsInformationMessageBuilder.BuildReadinessRequest(h, id, request), ct));
    public async Task<ParticipantDiscoveryResponse> DiscoverAsync(PapssParticipantBinding participant, ParticipantDiscoveryRequest request, CancellationToken ct)
        => Payload<ParticipantDiscoveryResponse>(await Information(participant, WpSipsProfiles.Participant, (h, id) => WpSipsInformationMessageBuilder.BuildParticipantRequest(h, id, request), ct));
    public async Task<FxRateResponse> GetFxAsync(PapssParticipantBinding participant, FxRateRequest request, CancellationToken ct)
        => Payload<FxRateResponse>(await Information(participant, WpSipsProfiles.Fx, (h, id) => WpSipsInformationMessageBuilder.BuildFxRequest(h, id, request), ct));

    private async Task<WpSipsMessage<object>> Information(PapssParticipantBinding participant, string profile, Func<BusinessHeader, string, string> build, CancellationToken ct)
    {
        try
        {
            var id = Id(); var header = new BusinessHeader(participant.Bic, options.RemoteWpSipsIdentity, id, "admi.009.001.02", profile, DateTimeOffset.UtcNow);
            var unsigned = build(header, id); var signed = signer.SignEnvelope(unsigned); var response = await PostAsync(signed, id, ct);
            var verified = await verifier.VerifyWithProvenance(response, ct);
            if (!verified.Result || verified.Signer is null) throw new UnauthorizedAccessException("Invalid signed WP-SIPS response.");
            ValidateSignerAndHeader(verified.Signer, participant, response, "admi.010.001.02", profile);
            WpSipsProtocolValidator.ValidateCorrelation(signed, response);
            health.RecordSuccess();
            return WpSipsInformationMessageParser.Parse(response);
        }
        catch (Exception error) { health.RecordFailure(error); throw; }
    }

    private async Task<PapssAdmissionResponse> SendAdmissionAsync(string unsigned, PapssParticipantBinding participant, CancellationToken ct)
    {
        try
        {
            var profiled = SetBusinessService(unsigned, options.SecurityProfile);
            var requestMsgId = Value(SecureDocument(profiled).Descendants().Single(x => x.Name.LocalName == "AppHdr"), "BizMsgIdr");
            var signed = signer.SignEnvelope(profiled); var response = await PostAsync(signed, requestMsgId, ct);
            var verified = await verifier.VerifyWithProvenance(response, ct);
            if (!verified.Result || verified.Signer is null) throw new UnauthorizedAccessException("Invalid signed WP-SIPS response.");
            ValidateSignerAndHeader(verified.Signer, participant, response, WpSipsMessageTypes.MessageReject, options.SecurityProfile);
            WpSipsProtocolValidator.Validate(response);
            var parsed = WpSipsInformationMessageParser.Parse(response);
            if (parsed.Header.RelatedBusinessMessageId != requestMsgId || parsed.Payload is not AdminReject admission || admission.RejectedBusinessMessageId != requestMsgId)
                throw new InvalidDataException("The technical admission response does not correlate to the request.");
            if (admission.ReasonCode is not ("RECEIVED_AND_DURABLY_ADMITTED" or "EXACT_REPLAY"))
                throw new InvalidDataException("The WP-SIPS request was not durably admitted: " + admission.ReasonCode);
            health.RecordSuccess();
            return new(requestMsgId, admission.ReasonCode, true);
        }
        catch (Exception error) { health.RecordFailure(error); throw; }
    }

    private async Task<string> PostAsync(string signedXml, string correlation, CancellationToken ct)
    {
        if (!options.Enabled) throw new ParticipantRailException("PAPSS_DISABLED", "PAPSS is not enabled.");
        if (!Uri.TryCreate(options.IsoIngressUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            throw new InvalidOperationException("PapssFacing:IsoIngressUrl is invalid.");
        if (!string.Equals(uri.AbsolutePath.TrimEnd('/'), "/sips/messages", StringComparison.Ordinal))
            throw new InvalidOperationException("PapssFacing:IsoIngressUrl is not the common /sips/messages ingress.");
        if (options.AllowedHosts.Length != 0 && !options.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("PapssFacing:IsoIngressUrl host is not allowed.");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(signedXml, Encoding.UTF8, "application/xml") };
        request.Headers.TryAddWithoutValidation("X-Request-Context", correlation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("WP-SIPS service request failed.", null, response.StatusCode);
        if (response.Content.Headers.ContentType?.MediaType is not "application/xml" and not "text/xml") throw new InvalidDataException("WP-SIPS response media type is invalid.");
        if (response.Content.Headers.ContentLength > options.MaximumResponseBytes) throw new InvalidDataException("WP-SIPS response exceeds the configured limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var memory = new MemoryStream();
        var buffer = new byte[81920]; int read; while ((read = await stream.ReadAsync(buffer, timeout.Token)) != 0) { if (memory.Length + read > options.MaximumResponseBytes) throw new InvalidDataException("WP-SIPS response exceeds the configured limit."); await memory.WriteAsync(buffer.AsMemory(0, read), timeout.Token); }
        logger.LogInformation("WP-SIPS {Correlation} received HTTP {StatusCode}", correlation, (int)response.StatusCode);
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private void ValidateSignerAndHeader(VerifiedSignerProvenance provenance, PapssParticipantBinding participant, string xml, string expectedType, string expectedService)
    {
        if (!provenance.FromOwnershipVerified || !string.Equals(provenance.RepresentedParticipant, options.RemoteWpSipsIdentity, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The response signer is not the configured PAPSS responder.");
        if (!string.Equals(provenance.MessageDefinitionId, expectedType, StringComparison.Ordinal) ||
            !string.Equals(provenance.BusinessService, expectedService, StringComparison.Ordinal))
            throw new InvalidDataException("The signed response provenance does not match the expected message profile.");

        var header = SecureDocument(xml).Descendants().Single(x => x.Name.LocalName == "AppHdr");
        var from = Party(header, "Fr"); var to = Party(header, "To");
        var type = Value(header, "MsgDefIdr"); var service = Value(header, "BizSvc");
        if (!string.Equals(from, options.RemoteWpSipsIdentity, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(to, participant.Bic, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The response BAH parties do not reverse the request parties.");
        if (!string.Equals(type, expectedType, StringComparison.Ordinal) || !string.Equals(service, expectedService, StringComparison.Ordinal))
            throw new InvalidDataException("The response BAH profile is not the expected profile.");
    }

    private string SetBusinessService(string xml, string service)
    {
        var document = SecureDocument(xml); var header = document.Descendants().Single(x => x.Name.LocalName == "AppHdr");
        header.Elements().Single(x => x.Name.LocalName == "To").Descendants().Single(x => x.Name.LocalName == "Id").Value = options.RemoteWpSipsIdentity;
        var existing = header.Elements().FirstOrDefault(x => x.Name.LocalName == "BizSvc");
        if (existing is not null) existing.Value = service;
        else header.Elements().Single(x => x.Name.LocalName == "MsgDefIdr").AddAfterSelf(new XElement(header.Name.Namespace + "BizSvc", service));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static XDocument SecureDocument(string xml)
    {
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader, LoadOptions.None);
    }
    private static string Party(XElement header, string name) => header.Elements().Single(x => x.Name.LocalName == name).Descendants().Single(x => x.Name.LocalName == "Id").Value;
    private static string Value(XElement parent, string name) => parent.Elements().Single(x => x.Name.LocalName == name).Value;

    private static T Payload<T>(WpSipsMessage<object> message) => message.Payload is T value ? value : throw new InvalidDataException("Unexpected WP-SIPS response profile.");
    private static string Id() => "SIPS-" + Guid.NewGuid().ToString("N")[..24];
    private static string Required(string? value, string name) => !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Required PAPSS field missing: {name}.");
    private void ValidateCorridor(PapssParticipantBinding participant, PaymentRequestDto x)
    {
        Required(x.ToBIC, "destination institution"); Required(x.SenderCountry, "sender country"); Required(x.ReceiverCountry, "receiver country"); Required(x.SenderCurrency, "sender currency"); Required(x.ReceiverCurrency, "receiver currency"); Required(x.LocalInstrument, "local instrument");
        static bool Code(string value, int length) => value.Length == length && value.All(c => c is >= 'A' and <= 'Z');
        x.SenderCountry = x.SenderCountry!.Trim().ToUpperInvariant(); x.ReceiverCountry = x.ReceiverCountry!.Trim().ToUpperInvariant();
        x.SenderCurrency = x.SenderCurrency!.Trim().ToUpperInvariant(); x.ReceiverCurrency = x.ReceiverCurrency!.Trim().ToUpperInvariant();
        x.Currency = x.Currency.Trim().ToUpperInvariant(); x.LocalInstrument = x.LocalInstrument.Trim().ToUpperInvariant();
        if (!Code(x.SenderCountry, 2) || !Code(x.ReceiverCountry, 2)) throw new ArgumentException("PAPSS country codes must be two uppercase ASCII letters.");
        if (!Code(x.SenderCurrency, 3) || !Code(x.ReceiverCurrency, 3) || !Code(x.Currency, 3)) throw new ArgumentException("PAPSS currency codes must be three uppercase ASCII letters.");
        if (!string.Equals(x.Currency, x.SenderCurrency, StringComparison.Ordinal)) throw new ArgumentException("PAPSS sender currency must equal the instructed amount currency.");
        if (!x.LocalInstrument.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') || x.LocalInstrument.Length > 35 || !options.AllowedLocalInstruments.Contains(x.LocalInstrument, StringComparer.Ordinal)) throw new ArgumentException("PAPSS local instrument is invalid or not allowed.");
        if (!string.Equals(x.DebtorAgentBIC?.Trim(), participant.Bic, StringComparison.OrdinalIgnoreCase)) throw new ParticipantRailException("PARTICIPANT_BIC_MISMATCH", "The payment debtor agent does not match the authenticated participant BIC.");
        var corridor = options.AllowedCorridors.SingleOrDefault(c =>
            string.Equals(c.SenderCountry, x.SenderCountry, StringComparison.OrdinalIgnoreCase) && string.Equals(c.ReceiverCountry, x.ReceiverCountry, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.SenderCurrency, x.SenderCurrency, StringComparison.OrdinalIgnoreCase) && string.Equals(c.ReceiverCurrency, x.ReceiverCurrency, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.DestinationBic, x.ToBIC, StringComparison.OrdinalIgnoreCase));
        if (corridor is null || !corridor.LocalInstruments.Contains(x.LocalInstrument, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("The PAPSS corridor, destination, currency pair, and local instrument combination is not configured.");
    }
}
