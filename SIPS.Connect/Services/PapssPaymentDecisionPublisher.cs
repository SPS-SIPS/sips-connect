using System.Text;
using System.Xml.Linq;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SIPS.Connect.Config;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;

namespace SIPS.Connect.Services;

public interface IPapssPaymentDecisionPublisher
{
    Task PersistAndSubmitAsync(PapssParticipantBinding participant, string inboundPacs008, CancellationToken ct);
}

public sealed class PapssPaymentDecisionPublisher(
    IStorageBroker storage,
    IPapssFacingSipsClient papss,
    INativeSigner signer,
    PapssFacingOptions options,
    ILogger<PapssPaymentDecisionPublisher> logger) : IPapssPaymentDecisionPublisher
{
    public async Task PersistAndSubmitAsync(PapssParticipantBinding participant, string inboundPacs008, CancellationToken ct)
    {
        var inbound = SecureDocument(inboundPacs008);
        var inboundHeader = inbound.Descendants().Single(x => x.Name.LocalName == "AppHdr");
        if (Required(inboundHeader.Elements().Single(x => x.Name.LocalName == "MsgDefIdr").Value, "MsgDefIdr") != "pacs.008.001.10" ||
            Required(inboundHeader.Elements().SingleOrDefault(x => x.Name.LocalName == "BizSvc")?.Value, "BizSvc") != options.SecurityProfile)
            throw new InvalidDataException("The stored message is not a PAPSS payment callback.");
        var txId = Required(inbound.Descendants().SingleOrDefault(x => x.Name.LocalName == "TxId")?.Value, "TxId");
        var record = await storage.ISOMessages.AsNoTracking().SingleAsync(
            x => x.MessageType == ISOMessageType.TransactionRequest && x.TxId == txId, ct);

        var receivedBytes = Encoding.UTF8.GetBytes(inboundPacs008);
        if (record.Message.Length != receivedBytes.Length || !CryptographicOperations.FixedTimeEquals(record.Message, receivedBytes))
            throw new ParticipantRailException("DUPLICATE_CONFLICT", "The transaction identifier was reused with a different signed payment payload.");

        if (record.PapssDecision is null)
        {
            if (record.Response is null) throw new InvalidOperationException("The bank decision was not durably persisted.");
            var decision = CreateSignedDecision(participant, inbound, Encoding.UTF8.GetString(record.Response));
            var bytes = Encoding.UTF8.GetBytes(decision);
            await storage.ISOMessages
                .Where(x => x.Id == record.Id && x.PapssDecision == null)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.PapssDecision, bytes), ct);
            record = await storage.ISOMessages.AsNoTracking().SingleAsync(x => x.Id == record.Id, ct);
        }

        if (record.PapssDecisionPublishedAt is not null || record.PapssDecisionFailedAt is not null) return;
        await TrySubmitAsync(participant, record.Id, record.PapssDecision!, ct);
    }

    internal async Task TrySubmitAsync(PapssParticipantBinding participant, int recordId, byte[] signedDecision, CancellationToken ct)
    {
        try
        {
            var admission = await papss.SubmitPaymentDecisionAsync(participant, Encoding.UTF8.GetString(signedDecision), ct);
            await storage.ISOMessages.Where(x => x.Id == recordId && x.PapssDecisionPublishedAt == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.PapssDecisionAdmissionCode, admission.Code)
                    .SetProperty(x => x.PapssDecisionPublishedAt, DateTimeOffset.UtcNow), ct);
        }
        catch (Exception error) when (error is ParticipantRailException or UnauthorizedAccessException or InvalidDataException)
        {
            var code = error switch
            {
                ParticipantRailException rail => rail.Code,
                UnauthorizedAccessException => "AUTHORIZATION_REJECTED",
                _ => "REJECTED_BEFORE_EXTERNAL_EFFECT"
            };
            await storage.ISOMessages.Where(x => x.Id == recordId && x.PapssDecisionPublishedAt == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.PapssDecisionFailureCode, code)
                    .SetProperty(x => x.PapssDecisionFailedAt, DateTimeOffset.UtcNow), ct);
            logger.LogError(error, "PAPSS decision {MessageId} reached terminal admission failure {FailureCode}", recordId, code);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            logger.LogWarning(error, "PAPSS decision handoff is ambiguous for ISO message {MessageId}; the stored signed decision will be retried", recordId);
        }
    }

    private string CreateSignedDecision(PapssParticipantBinding participant, XDocument inbound, string unsignedPacs002)
    {
        var decision = SecureDocument(unsignedPacs002);
        var header = decision.Descendants().Single(x => x.Name.LocalName == "AppHdr");
        var to = header.Elements().Single(x => x.Name.LocalName == "To").Descendants().Single(x => x.Name.LocalName == "Id");
        to.Value = options.RemoteWpSipsIdentity;
        var service = header.Elements().FirstOrDefault(x => x.Name.LocalName == "BizSvc");
        if (service is null) header.Elements().Single(x => x.Name.LocalName == "MsgDefIdr").AddAfterSelf(new XElement(header.Name.Namespace + "BizSvc", options.SecurityProfile));
        else service.Value = options.SecurityProfile;

        var status = decision.Descendants().Single(x => x.Name.LocalName == "TxSts");
        status.Value = status.Value == "RJCT" ? "RJCT" : "ACCP";
        if (status.Value == "RJCT" && !HasRejectionReason(decision))
            throw new InvalidDataException("A rejected PAPSS payment decision requires a reason.");

        Correlation(inbound, decision, "MsgId", "OrgnlMsgId");
        Correlation(inbound, decision, "InstrId", "OrgnlInstrId", optional: true);
        Correlation(inbound, decision, "EndToEndId", "OrgnlEndToEndId");
        Correlation(inbound, decision, "TxId", "OrgnlTxId");
        Correlation(inbound, decision, "UETR", "OrgnlUETR", optional: true);
        if (header.Elements().Count(x => x.Name.LocalName == "Rltd") != 1)
            throw new InvalidDataException("The PAPSS decision must contain exactly one related BAH.");
        var inboundHeader = inbound.Descendants().Single(x => x.Name.LocalName == "AppHdr");
        var related = header.Elements().Single(x => x.Name.LocalName == "Rltd");
        foreach (var name in new[] { "BizMsgIdr", "MsgDefIdr", "CreDt" }) Correlation(inboundHeader, related, name, name);
        foreach (var name in new[] { "Fr", "To" })
        {
            var expectedParty = inboundHeader.Elements().Single(x => x.Name.LocalName == name).Descendants().Single(x => x.Name.LocalName == "Id").Value;
            var actualParty = related.Elements().Single(x => x.Name.LocalName == name).Descendants().Single(x => x.Name.LocalName == "Id").Value;
            if (!string.Equals(expectedParty, actualParty, StringComparison.Ordinal))
                throw new InvalidDataException($"The PAPSS decision related BAH does not preserve {name}.");
        }

        return signer.SignEnvelope(decision.ToString(SaveOptions.DisableFormatting), XadesProfile.WpSipsPapss);
    }

    private static void Correlation(XContainer source, XContainer target, string sourceName, string targetName, bool optional = false)
    {
        var expected = source.Descendants().FirstOrDefault(x => x.Name.LocalName == sourceName)?.Value;
        var actual = target.Descendants().FirstOrDefault(x => x.Name.LocalName == targetName)?.Value;
        if (optional && string.IsNullOrWhiteSpace(expected)) return;
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidDataException($"The PAPSS decision does not preserve {sourceName} correlation.");
    }

    internal static bool HasRejectionReason(XDocument document) => document.Descendants()
        .Where(x => x.Name.LocalName == "StsRsnInf")
        .SelectMany(x => x.Elements().Where(y => y.Name.LocalName == "Rsn"))
        .SelectMany(x => x.Elements())
        .Any(x => x.Name.LocalName is "Cd" or "Prtry" && !string.IsNullOrWhiteSpace(x.Value));

    private static XDocument SecureDocument(string xml)
    {
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static string Required(string? value, string name) => !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"The PAPSS callback is missing {name}.");
}
