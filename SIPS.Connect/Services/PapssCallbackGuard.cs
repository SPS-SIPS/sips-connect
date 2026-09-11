using System.Xml;
using System.Xml.Linq;
using SIPS.Adapter.Models;
using SIPS.Connect.Config;
using SIPS.ISO20022.Models.WpSips;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Enums;
using SIPS.XMLDsig.Xades.Interfaces;

namespace SIPS.Connect.Services;

public interface IPapssCallbackGuard
{
    Task<PapssParticipantBinding?> ValidateAsync(string xml, CancellationToken ct);
}

public sealed class PapssCallbackGuard(PapssFacingOptions options, JsonAdapterOptions mappings, INativeVerifier verifier) : IPapssCallbackGuard
{
    public async Task<PapssParticipantBinding?> ValidateAsync(string xml, CancellationToken ct)
    {
        if (!options.Enabled) return null;
        var header = Document(xml).Descendants().SingleOrDefault(x => x.Name.LocalName == "AppHdr") ?? throw new InvalidDataException("WP-SIPS AppHdr is missing.");
        var from = Party(header, "Fr");
        var service = header.Elements().FirstOrDefault(x => x.Name.LocalName == "BizSvc")?.Value;
        if (string.IsNullOrWhiteSpace(service) || service != options.SecurityProfile && !WpSipsProfiles.All.Contains(service))
        {
            if (string.Equals(from, options.RemoteWpSipsIdentity, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The PAPSS callback business service is missing or unsupported.");
            return null;
        }
        var messageDefinition = header.Elements().Single(x => x.Name.LocalName == "MsgDefIdr").Value;
        if (WpSipsProfiles.All.Contains(service))
        {
            if (messageDefinition != WpSipsMessageTypes.StaticDataReport) throw new InvalidDataException("The WP-SIPS callback message/profile pairing is invalid.");
            WpSipsProtocolValidator.Validate(xml);
        }
        else
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "acmt.023.001.03", "acmt.024.001.03", "pacs.008.001.10", "pacs.028.001.05", "pacs.004.001.11", "pacs.002.001.12" };
            if (!allowed.Contains(messageDefinition)) throw new InvalidDataException("The PAPSS financial callback message/profile pairing is invalid.");
            ValidateFinancialReferences(Document(xml), messageDefinition);
        }
        var verified = await verifier.VerifyWithProvenance(xml, ct);
        if (!verified.Result || verified.Signer is not { FromOwnershipVerified: true } signer || !string.Equals(signer.RepresentedParticipant, options.RemoteWpSipsIdentity, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The callback signer is not the configured PAPSS responder.");
        if (!string.Equals(signer.MessageDefinitionId, messageDefinition, StringComparison.Ordinal) || !string.Equals(signer.BusinessService, service, StringComparison.Ordinal))
            throw new InvalidDataException("The protected callback provenance does not match its BAH profile.");
        var to = Party(header, "To");
        if (!string.Equals(from, options.RemoteWpSipsIdentity, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("PAPSS callback BAH From is invalid.");
        var matches = options.Participants.Where(x => x.Value.Enabled && string.Equals(x.Value.Bic, to, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("PAPSS callback destination does not resolve to exactly one participant.");
        var match = matches[0];
        if (string.IsNullOrWhiteSpace(match.Value.CallbackMappingProfile) || !mappings.Endpoints.Keys.Any(x => x.StartsWith(match.Value.CallbackMappingProfile + ".", StringComparison.Ordinal)))
            throw new InvalidDataException("The participant callback mapping profile is missing or unknown.");
        return new(match.Key, match.Value.Bic.Trim().ToUpperInvariant(), match.Value.CallbackMappingProfile, match.Value.CallbackUrl);
    }

    private static XDocument Document(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader, LoadOptions.None);
    }
    private static string Party(XElement header, string name) => header.Elements().Single(x => x.Name.LocalName == name).Descendants().Single(x => x.Name.LocalName == "Id").Value;
    private static void ValidateFinancialReferences(XDocument document, string messageDefinition)
    {
        bool Has(string name) => document.Descendants().Any(x => x.Name.LocalName == name && !string.IsNullOrWhiteSpace(x.Value));
        if (messageDefinition is "pacs.002.001.12" or "pacs.004.001.11" or "pacs.028.001.05")
        {
            if (!Has("OrgnlTxId") || !Has("OrgnlEndToEndId")) throw new InvalidDataException("The callback original transaction references are incomplete.");
        }
        if (messageDefinition == "pacs.002.001.12" && !Has("OrgnlMsgId")) throw new InvalidDataException("The callback original message reference is missing.");
    }
}
