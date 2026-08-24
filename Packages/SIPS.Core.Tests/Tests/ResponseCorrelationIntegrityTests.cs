using System;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class ResponseCorrelationIntegrityTests
{
    private static readonly DateTime OriginalCreatedAt =
        new(2025, 8, 24, 7, 37, 15, DateTimeKind.Utc);

    [Fact]
    public void PaymentResponse_PreservesInboundHeaderOnlyInRelatedHeader()
    {
        var original = PaymentRequest();
        var xml = PaymentRequestResponseBuilder.Build(new PaymentRequestResponseBuilder.Response
        {
            From = original.To,
            To = original.From,
            Original = original,
            Status = "ACSC",
            AcceptanceDate = OriginalCreatedAt.AddSeconds(1)
        });

        var doc = XDocument.Parse(xml);
        var appHdr = Element(doc, "AppHdr");
        var related = Element(appHdr, "Rltd");

        DirectValue(appHdr, "BizMsgIdr").Should().NotBe(original.BizMsgIdr);
        PartyId(related, "Fr").Should().Be(original.From);
        PartyId(related, "To").Should().Be(original.To);
        DirectValue(related, "BizMsgIdr").Should().Be(original.BizMsgIdr);
        DirectValue(related, "MsgDefIdr").Should().Be("pacs.008.001.10");
        DateTime.Parse(DirectValue(related, "CreDt")!).ToUniversalTime().Should().Be(OriginalCreatedAt);
        Value(doc, "OrgnlMsgId").Should().Be(original.MsgId);
        Value(doc, "OrgnlMsgNmId").Should().Be("pacs.008.001.10");
    }

    [Fact]
    public void ReturnResponse_UsesDocumentMessageIdAndPreservesRelatedHeader()
    {
        var original = ReturnRequest();
        var xml = ReturnPaymentResponseBuilder.Build(new ReturnPaymentResponseBuilder.Response
        {
            From = original.To,
            To = original.From,
            Original = original,
            Status = "ACSC",
            AcceptanceDate = OriginalCreatedAt.AddSeconds(1)
        });

        var doc = XDocument.Parse(xml);
        var related = Element(Element(doc, "AppHdr"), "Rltd");
        DirectValue(related, "BizMsgIdr").Should().Be(original.BizMsgIdr);
        DirectValue(related, "MsgDefIdr").Should().Be("pacs.004.001.11");
        Value(doc, "OrgnlMsgId").Should().Be(original.MsgId);
        Value(doc, "OrgnlMsgId").Should().NotBe(original.BizMsgIdr);
    }

    [Fact]
    public void VerificationResponse_PreservesOriginalRelatedHeaderIdentity()
    {
        var original = VerificationRequest();
        var xml = PayeeVerificationResponseBuilder.Build(new PayeeVerificationResponseBuilder.Request
        {
            From = original.To,
            To = original.From,
            Original = original,
            Verified = false,
            Reason = "MISS"
        });

        var related = Element(Element(XDocument.Parse(xml), "AppHdr"), "Rltd");
        DirectValue(related, "BizMsgIdr").Should().Be(original.BizMsgIdr);
        DirectValue(related, "MsgDefIdr").Should().Be("acmt.023.001.03");
        DateTime.Parse(DirectValue(related, "CreDt")!).ToUniversalTime().Should().Be(OriginalCreatedAt);
    }

    [Fact]
    public void ResponseBuilders_OmitImpossibleAcceptanceDates()
    {
        var original = PaymentRequest();

        var futureXml = PaymentRequestResponseBuilder.Build(new PaymentRequestResponseBuilder.Response
        {
            From = original.To,
            To = original.From,
            Original = original,
            Status = "ACSC",
            AcceptanceDate = DateTime.UtcNow.AddMinutes(1)
        });
        var beforeRequestXml = PaymentRequestResponseBuilder.Build(new PaymentRequestResponseBuilder.Response
        {
            From = original.To,
            To = original.From,
            Original = original,
            Status = "ACSC",
            AcceptanceDate = original.CreDt.AddSeconds(-1)
        });

        futureXml.Should().NotContain("AccptncDtTm");
        beforeRequestXml.Should().NotContain("AccptncDtTm");
    }

    [Fact]
    public void StatusResponse_AllowsAcceptanceBeforeTheLaterStatusMessage()
    {
        var statusMessage = PaymentRequest();
        statusMessage.MsgDefIdr = "pacs.028.001.05";
        statusMessage.MsgId = "STATUS-MSG";
        statusMessage.BizMsgIdr = "STATUS-BIZ";

        var xml = PaymentStatusRequestResponseBuilder.Build(new PaymentStatusRequestResponseBuilder.Response
        {
            From = statusMessage.To,
            To = statusMessage.From,
            Original = statusMessage,
            Status = "ACSC",
            AcceptanceDate = statusMessage.CreDt.AddMinutes(-5)
        });

        xml.Should().Contain("AccptncDtTm");
    }

    [Fact]
    public void ResponseBuilders_RejectMissingOriginalCorrelationValues()
    {
        var original = PaymentRequest();
        original.BizMsgIdr = string.Empty;

        var act = () => PaymentRequestResponseBuilder.Build(new PaymentRequestResponseBuilder.Response
        {
            From = original.To,
            To = original.From,
            Original = original,
            Status = "ACSC"
        });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void InboundParsers_UseApplicationHeaderPartiesInsteadOfDocumentAgents()
    {
        var payment = PaymentRequest();
        var (paymentXml, _, _, _) = PaymentRequestBuilder.Build(payment);
        paymentXml = ChangeDocumentParties(paymentXml, "DOC-FROM", "DOC-TO");
        PaymentRequestBuilder.Parse(paymentXml).From.Should().Be(payment.From);
        PaymentRequestBuilder.Parse(paymentXml).To.Should().Be(payment.To);

        var status = new PaymentStatusRequestBuilder.Request
        {
            From = "HDR-FROM", To = "HDR-TO", MsgId = "STATUS-MSG",
            CreDt = OriginalCreatedAt, OrgnlTxId = "TX-1", OriginalEndToEnd = "E2E-1"
        };
        var statusXml = ChangeDocumentParties(PaymentStatusRequestBuilder.Build(status), "DOC-FROM", "DOC-TO");
        PaymentStatusRequestBuilder.Parse(statusXml).From.Should().Be(status.From);
        PaymentStatusRequestBuilder.Parse(statusXml).To.Should().Be(status.To);

        var returnRequest = ReturnRequest();
        var (returnXml, _, _, _) = ReturnPaymentRequestBuilder.Build(returnRequest);
        returnXml = ChangeDocumentParties(returnXml, "DOC-FROM", "DOC-TO");
        ReturnPaymentRequestBuilder.Parse(returnXml).From.Should().Be(returnRequest.From);
        ReturnPaymentRequestBuilder.Parse(returnXml).To.Should().Be(returnRequest.To);

        var verification = VerificationRequest();
        var (verificationXml, _, _) = PayeeVerificationBuilder.Build(verification);
        verificationXml = ChangeDocumentParties(verificationXml, "DOC-FROM", "DOC-TO");
        PayeeVerificationBuilder.Parse(verificationXml).From.Should().Be(verification.From);
        PayeeVerificationBuilder.Parse(verificationXml).To.Should().Be(verification.To);
    }

    [Fact]
    public void PaymentParser_RejectsMismatchedApplicationHeaderMessageType()
    {
        var (xml, _, _, _) = PaymentRequestBuilder.Build(PaymentRequest());
        var document = XDocument.Parse(xml);
        var appHeader = Element(document, "AppHdr");
        appHeader.Elements().First(e => e.Name.LocalName == "MsgDefIdr").Value = "pacs.002.001.12";

        var act = () => PaymentRequestBuilder.Parse(document.ToString(SaveOptions.DisableFormatting));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MessageTypeDetection_IsNamespaceAwareAndPrefixIndependent()
    {
        const string xml = """
            <Envelope xmlns:h="urn:iso:std:iso:20022:tech:xsd:head.001.001.03">
              <h:AppHdr><h:MsgDefIdr>pacs.008.001.10</h:MsgDefIdr></h:AppHdr>
            </Envelope>
            """;

        Transformers.GetMessageType(xml).Should().Be("pacs.008.001.10");
    }

    private static PaymentRequestBuilder.Request PaymentRequest() => new()
    {
        From = "HDR-FROM", To = "HDR-TO", BizMsgIdr = "ORIGINAL-BIZ",
        MsgDefIdr = "pacs.008.001.10", MsgId = "ORIGINAL-MSG", CreDt = OriginalCreatedAt,
        TxId = "TX-1", EndToEndId = "E2E-1", Amount = 10, Currency = "USD",
        LocalInstrument = "CRTRM", CategoryPurpose = "C2CCRT", Ustrd = "test",
        Debtor = new Person { Name = "Debtor", Address = "A", Account = "D1", AccountType = "CACC", Issuer = "C", AgentBIC = "DBTR-BIC" },
        Creditor = new Person { Name = "Creditor", Address = "B", Account = "C1", AccountType = "CACC", Issuer = "C", AgentBIC = "CDTR-BIC" }
    };

    private static ReturnPaymentRequestBuilder.Request ReturnRequest() => new()
    {
        From = "HDR-FROM", To = "HDR-TO", BizMsgIdr = "RETURN-BIZ",
        MsgDefIdr = "pacs.004.001.11", MsgId = "RETURN-MSG", CreDt = OriginalCreatedAt,
        NumberOfTransactions = 1, ReturnId = "RETURN-1", OrgnlTxId = "TX-1",
        OriginalEndToEnd = "E2E-1", OriginalAmount = 10, OriginalCurrency = "USD",
        ReturnReason = "AC01", LocalInstrument = "CRTRM", CategoryPurpose = "C2CCRT"
    };

    private static PayeeVerificationBuilder.Request VerificationRequest() => new()
    {
        From = "HDR-FROM", To = "HDR-TO", BizMsgIdr = "VERIFY-BIZ",
        MsgDefIdr = "acmt.023.001.03", MsgId = "VERIFY-MSG", CreDt = OriginalCreatedAt,
        Alias = "123456", Type = "ACCT", SIPSRequestId = "VERIFY-1"
    };

    private static string ChangeDocumentParties(string xml, string from, string to)
    {
        var doc = XDocument.Parse(xml);
        var appHdr = Element(doc, "AppHdr");
        var documentPartyIds = doc.Descendants()
            .Where(e => e.Name.LocalName == "Id" && !e.Ancestors().Contains(appHdr))
            .Where(e => e.Ancestors().Any(a =>
                a.Name.LocalName is "InstgAgt" or "InstdAgt" or "Assgnr" or "Assgne"))
            .Take(2)
            .ToArray();
        documentPartyIds.Should().HaveCount(2);
        documentPartyIds[0].Value = from;
        documentPartyIds[1].Value = to;
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Element(XContainer parent, string name) =>
        parent.Descendants().First(e => e.Name.LocalName == name);

    private static string? Value(XContainer parent, string name) =>
        parent.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    private static string? DirectValue(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    private static string? PartyId(XElement parent, string partyName) =>
        parent.Elements().First(e => e.Name.LocalName == partyName)
            .Descendants().First(e => e.Name.LocalName == "Id").Value;
}
