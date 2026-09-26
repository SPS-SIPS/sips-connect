using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using Xunit;
using P = SIPS.Connect.Services.PapssFieldProvenance;

namespace SIPS.Connect.Tests;

/// <summary>R1 outbound recall: the camt.056 we build, the answers we read and the recall state rules.</summary>
public sealed class PapssRecallUnitTests
{
    private const string RecallId = "SIPS-0123456789abcdef01234567";

    [Fact]
    public void Recall_request_has_the_exact_camt056_contract_shape()
    {
        var created = new DateTimeOffset(2026, 9, 26, 7, 30, 0, TimeSpan.Zero);
        var xml = PapssRecallMessages.BuildRecallRequest(new(RecallId, "ZKBASOS0", "WPSIPSGW", "ZKBASOS0620269MSG", "ZKBE2E20260925133328",
            "20260925SO1016133329694343036841", 1m, "USD", "DUPL", created));
        var document = XDocument.Parse(xml);
        XNamespace h = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03", d = "urn:iso:std:iso:20022:tech:xsd:camt.056.001.08";

        // Envelope as for the other PAPSS-rail messages: identified root, header:/document: prefixes, AppHdr + Document.
        Assert.Equal("BL-" + RecallId, document.Root!.Attribute("Id")!.Value);
        Assert.Equal("header", document.Root.GetPrefixOfNamespace(h));
        Assert.Equal("document", document.Root.GetPrefixOfNamespace(d));
        var app = document.Root.Element(h + "AppHdr")!;
        Assert.Equal("ZKBASOS0", app.Element(h + "Fr")!.Descendants(h + "Id").Single().Value);
        Assert.Equal("WPSIPSGW", app.Element(h + "To")!.Descendants(h + "Id").Single().Value);
        Assert.Equal(RecallId, app.Element(h + "BizMsgIdr")!.Value);
        Assert.Equal("camt.056.001.08", app.Element(h + "MsgDefIdr")!.Value);
        Assert.Equal("2026-09-26T07:30:00.000Z", app.Element(h + "CreDt")!.Value);

        var request = document.Root.Element(d + "Document")!.Element(d + "FIToFIPmtCxlReq")!;
        var assignment = request.Element(d + "Assgnmt")!;
        Assert.Equal(RecallId, assignment.Element(d + "Id")!.Value);
        Assert.Equal("ZKBASOS0", assignment.Element(d + "Assgnr")!.Element(d + "Agt")!.Element(d + "FinInstnId")!.Element(d + "BICFI")!.Value);
        Assert.Equal("WPSIPSGW", assignment.Element(d + "Assgne")!.Element(d + "Agt")!.Element(d + "FinInstnId")!.Element(d + "Othr")!.Element(d + "Id")!.Value);
        Assert.Equal("2026-09-26T07:30:00.000Z", assignment.Element(d + "CreDtTm")!.Value);

        var tx = Assert.Single(request.Element(d + "Undrlyg")!.Elements(d + "TxInf"));
        Assert.Equal(["CxlId", "OrgnlGrpInf", "OrgnlEndToEndId", "OrgnlTxId", "OrgnlIntrBkSttlmAmt", "CxlRsnInf"], tx.Elements().Select(x => x.Name.LocalName));
        Assert.Equal(RecallId, tx.Element(d + "CxlId")!.Value);
        Assert.Equal("ZKBASOS0620269MSG", tx.Element(d + "OrgnlGrpInf")!.Element(d + "OrgnlMsgId")!.Value);
        Assert.Equal("pacs.008.001.10", tx.Element(d + "OrgnlGrpInf")!.Element(d + "OrgnlMsgNmId")!.Value);
        Assert.Equal("ZKBE2E20260925133328", tx.Element(d + "OrgnlEndToEndId")!.Value);
        Assert.Equal("20260925SO1016133329694343036841", tx.Element(d + "OrgnlTxId")!.Value);
        Assert.Equal(("USD", "1.00"), (tx.Element(d + "OrgnlIntrBkSttlmAmt")!.Attribute("Ccy")!.Value, tx.Element(d + "OrgnlIntrBkSttlmAmt")!.Value));
        Assert.Equal("DUPL", tx.Element(d + "CxlRsnInf")!.Element(d + "Rsn")!.Element(d + "Cd")!.Value);
    }

    [Fact]
    public void Camt029_is_read_by_exact_path_with_and_without_the_resolved_case()
    {
        var xml = GatewayProvenanceXml.Add(GatewayRecallXml.Resolution("CT02-CXL-1", RecallId, "ZKBASOS0620269MSG", "TX-1", "E2E-1", reason: "AGNT", responderId: "UG1003CXL1"),
            "sha256:ab", ("CxlDtls/TxInfAndSts/CxlStsRsnInf", P.NetworkReported));
        var message = PapssRecallMessages.ParseResolution(xml);
        Assert.Equal(("CT02-CXL-1", "CT02-CXL-1", RecallId, "RJCR", "UG1003CXL1"), (message.SourceMessageId, message.AssignmentId, message.ResolvedCaseId, message.Confirmation, message.ResponderId));
        Assert.Equal(("ZKBASOS0620269MSG", "pacs.008.001.10", "TX-1", "E2E-1", "AGNT", "customer declined"),
            (message.OriginalMessageId, message.OriginalMessageType, message.OriginalTxId, message.OriginalEndToEndId, message.ReasonCode, message.AdditionalInfo));
        Assert.Equal("sha256:ab", message.Provenance!.RawEvidenceReference);

        Assert.Null(PapssRecallMessages.ParseResolution(GatewayRecallXml.Resolution("CT02-CXL-2", null, "M", "TX-1", "E2E-1")).ResolvedCaseId);

        var twice = XDocument.Parse(GatewayRecallXml.Resolution("CT02-CXL-3", RecallId, "M", "TX-1", "E2E-1"));
        var details = twice.Descendants().Single(x => x.Name.LocalName == "CxlDtls");
        details.Add(new XElement(details.Elements().First()));
        Assert.Throws<InvalidDataException>(() => PapssRecallMessages.ParseResolution(twice.ToString()));
        var noTx = XDocument.Parse(GatewayRecallXml.Resolution("CT02-CXL-4", RecallId, "M", "TX-1", "E2E-1"));
        noTx.Descendants().Single(x => x.Name.LocalName == "OrgnlTxId").Remove();
        Assert.Throws<InvalidDataException>(() => PapssRecallMessages.ParseResolution(noTx.ToString()));
    }

    [Fact]
    public void Papss_answer_to_our_camt056_is_recognised_by_its_original_message_name()
    {
        var xml = GatewayRecallXml.RecallStatus("PAPSS-R-1", RecallId, "TX-1", "E2E-1", "ACCP", statusId: "PS0099");
        var report = PapssPaymentMessages.ParseStatusReport(xml);
        Assert.Equal((RecallId, "camt.056.001.08", "TX-1", "E2E-1", "ACCP"), (report.OriginalMessageId, report.OriginalMessageType, report.OriginalTxId, report.OriginalEndToEndId, report.Status));
        Assert.True(PapssRecallMessages.IsRecallAnswer(report.OriginalMessageType));
        Assert.True(PapssRecallMessages.IsRecallAnswer("camt.056.001.06"));
        Assert.False(PapssRecallMessages.IsRecallAnswer("pacs.008.001.10"));
        Assert.False(PapssRecallMessages.IsRecallAnswer(null));
        Assert.Equal("PS0099", PapssRecallMessages.StatusId(xml));
    }

    [Theory]
    [InlineData("RECALL_PENDING", "ACCP", "APPLIED")]
    [InlineData("RECALL_PENDING", "RJCT", "APPLIED")]
    [InlineData("RECALL_PENDING", "ACSC", "UNKNOWN_STATUS")]
    [InlineData("RECALL_PENDING", null, "UNKNOWN_STATUS")]
    [InlineData("RECALL_ACCEPTED_BY_PAPSS", "ACCP", "NOT_ADVANCING")]
    [InlineData("RECALL_ACCEPTED_BY_PAPSS", "RJCT", "CONFLICT")]
    [InlineData("RECALL_REJECTED_BY_PAPSS", "RJCT", "DUPLICATE_FINAL")]
    [InlineData("RECALL_REJECTED_BY_PAPSS", "ACCP", "CONFLICT")]
    [InlineData("RECALL_REJECTED_BY_BENEFICIARY", "ACCP", "NOT_ADVANCING")]
    [InlineData("RECALL_RETURNED", "ACCP", "NOT_ADVANCING")]
    [InlineData("RECALL_RETURNED", "RJCT", "CONFLICT")]
    [InlineData("REJECTED", "ACCP", "CONFLICT")]
    public void Papss_recall_status_applies_only_to_an_open_recall(string current, string? status, string expected)
        => Assert.Equal(expected, PapssRecallRules.EvaluatePapssStatus(Parse<PapssOutcome>(current), status));

    [Theory]
    [InlineData("RECALL_PENDING", "RJCR", "APPLIED")]
    [InlineData("RECALL_ACCEPTED_BY_PAPSS", "RJCR", "APPLIED")]
    [InlineData("RECALL_REJECTED_BY_BENEFICIARY", "RJCR", "DUPLICATE_FINAL")]
    [InlineData("RECALL_RETURNED", "RJCR", "CONFLICT")]
    [InlineData("RECALL_REJECTED_BY_PAPSS", "RJCR", "CONFLICT")]
    [InlineData("RECALL_ACCEPTED_BY_PAPSS", "CNCL", "UNKNOWN_STATUS")]
    [InlineData("RECALL_ACCEPTED_BY_PAPSS", null, "UNKNOWN_STATUS")]
    public void Beneficiary_rejection_applies_only_to_an_open_recall(string current, string? confirmation, string expected)
        => Assert.Equal(expected, PapssRecallRules.EvaluateResolution(Parse<PapssOutcome>(current), confirmation));

    [Fact]
    public void Recall_outcomes_are_stored_upper_snake_and_only_pending_and_accepted_are_open()
    {
        Assert.Equal("RECALL", UpperSnakeEnumConverter<PapssOperationType>.Of(PapssOperationType.Recall));
        Assert.Equal(["RECALL_PENDING", "RECALL_ACCEPTED_BY_PAPSS", "RECALL_REJECTED_BY_PAPSS", "RECALL_REJECTED_BY_BENEFICIARY", "RECALL_RETURNED"],
            new[] { PapssOutcome.RecallPending, PapssOutcome.RecallAcceptedByPapss, PapssOutcome.RecallRejectedByPapss, PapssOutcome.RecallRejectedByBeneficiary, PapssOutcome.RecallReturned }
                .Select(UpperSnakeEnumConverter<PapssOutcome>.Of));
        Assert.Equal(PapssOutcome.RecallAcceptedByPapss, Parse<PapssOutcome>("RECALL_ACCEPTED_BY_PAPSS"));
        Assert.Equal(PapssRecallRules.OpenOutcomes, Enum.GetValues<PapssOutcome>().Where(PapssRecallRules.IsOpen));
        // The partial unique index names exactly the open outcomes.
        foreach (var outcome in Enum.GetValues<PapssOutcome>())
            Assert.Equal(PapssRecallRules.IsOpen(outcome), PapssOperationConfiguration.OpenRecallFilter.Contains($"'{UpperSnakeEnumConverter<PapssOutcome>.Of(outcome)}'"));
        Assert.Equal(PapssOutcome.RecallRejectedByBeneficiary, PapssRecallRules.OutcomeOfEvent(PapssEventTypes.RecallResolution, "RJCR"));
        Assert.Equal(PapssOutcome.RecallReturned, PapssRecallRules.OutcomeOfEvent(PapssEventTypes.RecallReturned, null));
        Assert.Equal(PapssOutcome.RecallRejectedByPapss, PapssRecallRules.OutcomeOfEvent(PapssEventTypes.RecallStatus, "RJCT"));
        Assert.Null(PapssRecallRules.OutcomeOfEvent(PapssEventTypes.PaymentStatus, "ACCP"));
    }

    [Theory]
    [InlineData("RECALL_PENDING", "SUBMITTING", "PENDING")]
    [InlineData("RECALL_PENDING", "SUBMISSION_UNKNOWN", "UNKNOWN")]
    [InlineData("RECALL_PENDING", "ADMITTED", "PENDING")]
    // ACCP = accepted for processing, not completed.
    [InlineData("RECALL_ACCEPTED_BY_PAPSS", "ADMITTED", "PENDING")]
    [InlineData("RECALL_REJECTED_BY_PAPSS", "ADMITTED", "REJECTED")]
    [InlineData("RECALL_REJECTED_BY_BENEFICIARY", "ADMITTED", "REJECTED")]
    [InlineData("RECALL_RETURNED", "ADMITTED", "COMPLETED")]
    [InlineData("REJECTED", "REJECTED", "REJECTED")]
    public void Recall_summary_status_is_derived_from_the_recall_outcome(string outcome, string gateway, string expected)
    {
        var recall = new PapssOperation { Direction = PapssDirection.Outbound, Operation = PapssOperationType.Recall, PapssOutcome = Parse<PapssOutcome>(outcome), GatewayState = Parse<PapssGatewayState>(gateway), CreatedAt = DateTimeOffset.UtcNow };
        Assert.Equal(expected, PapssOperationResult.Summarize(recall, null, DateTimeOffset.UtcNow, 1));
    }

    [Fact]
    public void Response_deadline_is_record_only_and_flags_an_open_recall_past_it()
    {
        var now = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        var recall = new PapssOperation { Operation = PapssOperationType.Recall, PapssOutcome = PapssOutcome.RecallAcceptedByPapss, DeadlineAt = now.AddDays(-1), RequestMessageId = RecallId, CreatedAt = now.AddDays(-31) };
        Assert.True(PapssRecallSummary.From(recall, now).ResponseOverdue);
        Assert.Equal("PENDING", PapssRecallSummary.From(recall, now).Status);
        recall.PapssOutcome = PapssOutcome.RecallRejectedByBeneficiary;
        Assert.False(PapssRecallSummary.From(recall, now).ResponseOverdue);
        recall.DeadlineAt = null;
        Assert.Null(PapssRecallSummary.From(recall, now).ResponseOverdue);
    }

    [Theory]
    [InlineData("dupl", "DUPL")]
    [InlineData(" FRAD ", "FRAD")]
    [InlineData("AC03", "AC03")]
    public void Recall_reason_is_passed_through_with_the_iso_shape_only(string reason, string expected)
        => Assert.Equal(expected, PapssRecallMessages.NormalizeReason(reason));

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("DUPLI")]
    [InlineData("DU-P")]
    public void Recall_reason_must_be_present_and_iso_shaped(string? reason)
        => Assert.Throws<ArgumentException>(() => PapssRecallMessages.NormalizeReason(reason));

    [Theory]
    [InlineData(RecallId, true)]
    [InlineData("SIPS-0123456789ABCDEF01234567", false)]
    [InlineData("SIPS-0123456789abcdef0123456", false)]
    [InlineData("BANK-0123456789abcdef01234567", false)]
    public void Recall_id_follows_the_contract_shape(string id, bool valid)
    {
        Assert.Equal(valid, PapssRecallMessages.IsRecallId(id));
        Assert.True(PapssRecallMessages.IsRecallId(PapssFacingSipsClient.Id()));
    }

    [Theory]
    [InlineData("SIPS", null, "DUPL", null, "OPERATION_NOT_SUPPORTED")]
    [InlineData(null, "TX-1", null, null, "ArgumentException")]
    [InlineData(null, null, "DUPL", null, "ArgumentException")]
    [InlineData(null, "TX-1", "DUPL", "my-recall-1", "ArgumentException")]
    public async Task Invalid_recall_requests_are_refused_before_the_store_or_gateway_is_used(string? rail, string? txId, string? reason, string? recallId, string expected)
    {
        var db = new Mock<IStorageBroker>(MockBehavior.Strict);
        var gateway = new Mock<IPapssFacingSipsClient>(MockBehavior.Strict);
        var options = new PapssFacingOptions { Enabled = true, RemoteWpSipsIdentity = "WPSIPSGW" };
        var service = new PapssPaymentService(new PapssOperationStore(db.Object, options, TimeProvider.System, NullLogger<PapssOperationStore>.Instance), gateway.Object, options, TimeProvider.System, NullLogger<PapssPaymentService>.Instance);
        var request = new PapssRecallRequest { Rail = rail, TxId = txId, Reason = reason, RecallId = recallId };
        var binding = new PapssParticipantBinding("ZKBASOS0", "SO", ["USD"], "papss-callback-v1", "https://bank.test/cb");
        if (expected == "ArgumentException")
            await Assert.ThrowsAsync<ArgumentException>(() => service.RecallAsync(binding, request, CancellationToken.None));
        else
            Assert.Equal(expected, (await Assert.ThrowsAsync<ParticipantRailException>(() => service.RecallAsync(binding, request, CancellationToken.None))).Code);
        db.VerifyNoOtherCalls();
        gateway.VerifyNoOtherCalls();
    }

    private static T Parse<T>(string value) where T : struct, Enum => Enum.Parse<T>(value.Replace("_", string.Empty), true);
}
