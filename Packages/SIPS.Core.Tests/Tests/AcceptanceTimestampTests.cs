using System;
using FluentAssertions;
using SIPS.Core.Services.ISOParsers;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class AcceptanceTimestampTests
{
    [Fact]
    public void PaymentRequest_FormatsAcceptanceDateWithOffsetForIpsCompatibility()
    {
        var request = CreateOriginalRequest();
        request.CreDt = new DateTime(2026, 06, 11, 08, 53, 06, 369, DateTimeKind.Utc);

        var (xml, _, _, _) = PaymentRequestBuilder.Build(request);

        xml.Should().Contain("<document:AccptncDtTm>");
        xml.Should().Contain("+00:00</document:AccptncDtTm>");
    }

    [Fact]
    public void PaymentResponse_FormatsAcceptanceDateAsUtcZ()
    {
        var response = new PaymentRequestResponseBuilder.Response
        {
            From = "BICA",
            To = "BICB",
            BizMsgIdr = "BIZ",
            MsgDefIdr = "pacs.002.001.12",
            CreDt = new DateTime(2026, 06, 11, 12, 30, 18, 100, DateTimeKind.Utc),
            MsgId = "MSG",
            Status = "ACSC",
            AcceptanceDate = new DateTime(2026, 06, 11, 12, 30, 19, 750, DateTimeKind.Utc),
            Original = CreateOriginalRequest()
        };
        response.Original.CreDt = new DateTime(2026, 06, 11, 12, 30, 18, 100, DateTimeKind.Utc);

        var xml = PaymentRequestResponseBuilder.Build(response);

        xml.Should().Contain("<document:AccptncDtTm>2026-06-11T12:30:19.750Z</document:AccptncDtTm>");
        xml.Should().NotContain("+00:00");
    }

    [Fact]
    public void PaymentResponse_DoesNotFabricateMissingAcceptanceDate()
    {
        var response = new PaymentRequestResponseBuilder.Response
        {
            From = "BICA",
            To = "BICB",
            BizMsgIdr = "BIZ",
            MsgDefIdr = "pacs.002.001.12",
            CreDt = DateTime.UtcNow,
            MsgId = "MSG",
            Status = "ACSC",
            Original = CreateOriginalRequest()
        };

        var xml = PaymentRequestResponseBuilder.Build(response);
        var parsed = PaymentRequestResponseBuilder.Parse(xml);

        xml.Should().NotContain("AccptncDtTm");
        parsed.Should().NotBeNull();
        parsed!.AcceptanceDate.Should().BeNull();
    }

    [Fact]
    public void PaymentStatusResponse_DoesNotFabricateMissingAcceptanceDate()
    {
        var response = new PaymentStatusRequestResponseBuilder.Response
        {
            From = "BICA",
            To = "BICB",
            BizMsgIdr = "BIZ",
            MsgDefIdr = "pacs.002.001.12",
            CreDt = DateTime.UtcNow,
            MsgId = "MSG",
            Status = "ACSC",
            Original = CreateOriginalRequest()
        };

        var xml = PaymentStatusRequestResponseBuilder.Build(response);
        var parsed = PaymentStatusRequestResponseBuilder.Parse(xml);

        xml.Should().NotContain("AccptncDtTm");
        parsed.Should().NotBeNull();
        parsed!.AcceptanceDate.Should().BeNull();
    }

    [Fact]
    public void ReturnPaymentStatusParser_DoesNotUseMessageCreationTimeAsAcceptanceDate()
    {
        var messageCreatedAt = new DateTime(2026, 06, 11, 12, 30, 18, 100, DateTimeKind.Utc);
        var response = new ReturnPaymentResponseBuilder.Response
        {
            From = "BICA",
            To = "BICB",
            BizMsgIdr = "BIZ",
            MsgDefIdr = "pacs.002.001.12",
            CreDt = messageCreatedAt,
            MsgId = "MSG",
            Status = "ACSC",
            TxId = "TX",
            Original = CreateOriginalReturnRequest()
        };

        var beforeBuild = DateTime.UtcNow;
        var xml = ReturnPaymentResponseBuilder.Build(response);
        var afterBuild = DateTime.UtcNow;
        var parser = new PaymentStatusReportParser();

        parser.TryParse(xml, out var parsed).Should().BeTrue();
        xml.Should().NotContain("AccptncDtTm");
        parsed.Should().NotBeNull();
        parsed!.CreDt.Should().BeOnOrAfter(beforeBuild).And.BeOnOrBefore(afterBuild);
        parsed.AcceptanceDate.Should().BeNull();
    }

    private static PaymentRequestBuilder.Request CreateOriginalRequest()
    {
        return new PaymentRequestBuilder.Request
        {
            From = "BICB",
            To = "BICA",
            MsgDefIdr = "pacs.008.001.10",
            BizMsgIdr = "ORIGBIZ",
            MsgId = "ORIGMSG",
            CreDt = DateTime.UtcNow,
            TxId = "TX",
            EndToEndId = "E2E",
            LocalInstrument = "CRTRM",
            CategoryPurpose = "C2CCRT",
            Currency = "USD",
            Amount = 10,
            Ustrd = "test",
            Debtor = new Person
            {
                Name = "Debtor",
                Account = "D-001",
                Address = "Debtor address",
                AccountType = "CACC",
                Issuer = "C",
                AgentBIC = "ZKBASOS0"
            },
            Creditor = new Person
            {
                Name = "Creditor",
                Account = "C-001",
                Address = "Creditor address",
                AccountType = "CACC",
                Issuer = "C",
                AgentBIC = "AGROSOS0"
            }
        };
    }

    private static ReturnPaymentRequestBuilder.Request CreateOriginalReturnRequest()
    {
        return new ReturnPaymentRequestBuilder.Request
        {
            From = "BICB",
            To = "BICA",
            MsgDefIdr = "pacs.004.001.11",
            BizMsgIdr = "ORIGBIZ",
            MsgId = "ORIGMSG",
            CreDt = DateTime.UtcNow,
            NumberOfTransactions = 1,
            LocalInstrument = "CRTRM",
            CategoryPurpose = "C2CCRT",
            ReturnId = "RTRN",
            OriginalEndToEnd = "E2E",
            OrgnlTxId = "TX",
            OriginalCurrency = "USD",
            OriginalAmount = 10,
            ReturnReason = "AC01",
            AdditionalInfo = "test",
            DebtorAgent = "ZKBASOS0",
            CreditorAgent = "AGROSOS0"
        };
    }
}
