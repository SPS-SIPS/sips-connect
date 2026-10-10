using System.Xml.Linq;
using SIPS.Connect.Services;
using Xunit;

namespace SIPS.Connect.Tests;

/// <summary>
/// R2: parsing the gateway's camt.056.001.09 inbound-recall notification (Sps.Papss.Sips.Adapter/SipsInboundRecallMessage.Build
/// on the gateway side) and the inbound recall state rules.
/// </summary>
public sealed class PapssInboundRecallUnitTests
{
    static string Notification(string sourceMessageId, string cancellationId, string originalTxId, string originalEndToEndId, string? reasonCode = "DUPL", string? reasonProprietary = null, string? additionalInfo = null)
    {
        XNamespace h = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03", d = "urn:iso:std:iso:20022:tech:xsd:camt.056.001.09";
        XElement E(XNamespace ns, string name, params object?[] content) => new(ns + name, content);
        var reason = reasonCode is null && reasonProprietary is null ? null
            : E(d, "CxlRsnInf", reasonCode is not null ? E(d, "Rsn", E(d, "Cd", reasonCode)) : E(d, "Rsn", E(d, "Prtry", reasonProprietary)), additionalInfo is null ? null : E(d, "AddtlInf", additionalInfo));
        var app = E(h, "AppHdr", E(h, "Fr", E(h, "FIId", E(h, "FinInstnId", E(h, "Othr", E(h, "Id", "WPSIPSGW"))))), E(h, "To", E(h, "FIId", E(h, "FinInstnId", E(h, "BICFI", "ZKBASOS0")))),
            E(h, "BizMsgIdr", sourceMessageId), E(h, "MsgDefIdr", "camt.056.001.09"), E(h, "CreDt", "2026-10-04T08:31:49.563Z"));
        var document = E(d, "FIToFIPmtCxlReq",
            E(d, "Assgnmt", E(d, "Id", sourceMessageId), E(d, "Assgnr", E(d, "Agt", E(d, "FinInstnId", E(d, "Othr", E(d, "Id", "EG1002"))))), E(d, "Assgne", E(d, "Agt", E(d, "FinInstnId", E(d, "BICFI", "ZKBASOS0")))), E(d, "CreDtTm", "2026-10-04T08:31:49.563Z")),
            E(d, "Undrlyg", E(d, "TxInf", E(d, "CxlId", cancellationId), E(d, "OrgnlGrpInf", E(d, "OrgnlMsgId", "20260925EG1002000000000000000000481"), E(d, "OrgnlMsgNmId", "pacs.008.001.07")),
                E(d, "OrgnlEndToEndId", originalEndToEndId), E(d, "OrgnlTxId", originalTxId), new XElement(d + "OrgnlIntrBkSttlmAmt", new XAttribute("Ccy", "USD"), "500.00"), reason)));
        return new XElement("BusinessLayer", app, document).ToString(SaveOptions.DisableFormatting);
    }

    [Fact]
    public void Inbound_recall_notification_is_read_by_exact_path()
    {
        var xml = Notification("CT02-RECALL-1", "CXL-1", "RX-1", "E2E-RX-1", reasonCode: "DUPL");
        var message = PapssRecallMessages.ParseInboundRecall(xml);
        Assert.Equal(("CT02-RECALL-1", "CXL-1", "RX-1", "E2E-RX-1", "DUPL"), (message.SourceMessageId, message.CancellationId, message.OriginalTxId, message.OriginalEndToEndId, message.ReasonCode));
    }

    [Fact]
    public void A_proprietary_reason_falls_back_from_code_exactly_like_other_papss_messages()
    {
        var xml = Notification("CT02-RECALL-2", "CXL-2", "RX-2", "E2E-RX-2", reasonCode: null, reasonProprietary: "SUSPECTED FRAUD", additionalInfo: "flagged by compliance");
        var message = PapssRecallMessages.ParseInboundRecall(xml);
        Assert.Equal("SUSPECTED FRAUD", message.ReasonCode);
        Assert.Equal("flagged by compliance", message.AdditionalInfo);
    }

    [Fact]
    public void Missing_original_identifiers_fail_closed()
    {
        var document = XDocument.Parse(Notification("CT02-RECALL-3", "CXL-3", "RX-3", "E2E-RX-3"));
        document.Descendants().Single(x => x.Name.LocalName == "OrgnlTxId").Remove();
        Assert.Throws<InvalidDataException>(() => PapssRecallMessages.ParseInboundRecall(document.ToString()));
    }

    [Fact]
    public void More_than_one_transaction_fails_closed()
    {
        var document = XDocument.Parse(Notification("CT02-RECALL-4", "CXL-4", "RX-4", "E2E-RX-4"));
        var underlying = document.Descendants().Single(x => x.Name.LocalName == "Undrlyg");
        underlying.Add(new XElement(underlying.Elements().First()));
        Assert.Throws<InvalidDataException>(() => PapssRecallMessages.ParseInboundRecall(document.ToString()));
    }

    [Fact]
    public void Open_outcomes_are_exactly_the_four_non_terminal_states()
    {
        Assert.Equal(
        [
            SIPS.PostgreSQL.Enums.PapssOutcome.InboundRecallAwaitingDecision,
            SIPS.PostgreSQL.Enums.PapssOutcome.InboundRecallAcceptedByBank,
            SIPS.PostgreSQL.Enums.PapssOutcome.InboundRecallRejectedByBank,
            SIPS.PostgreSQL.Enums.PapssOutcome.InboundRecallUnresolved
        ], PapssInboundRecallRules.OpenOutcomes);
        Assert.False(PapssInboundRecallRules.IsOpen(SIPS.PostgreSQL.Enums.PapssOutcome.InboundRecallReplySubmitted));
        Assert.True(PapssInboundRecallRules.IsOpen(SIPS.PostgreSQL.Enums.PapssOutcome.InboundRecallAwaitingDecision));
    }
}
