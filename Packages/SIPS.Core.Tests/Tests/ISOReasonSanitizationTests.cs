using System;
using FluentAssertions;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models;
using SIPS.ISO20022.Schemas.PRDocument;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class ISOReasonSanitizationTests
{
    private const string LongReason = "This service is temporarily unavailable.";

    [Fact]
    public void PaymentRequestResponseBuilder_Builds_WhenReasonExceedsIsoMax35()
    {
        var response = new PaymentRequestResponseBuilder.Response
        {
            From = "SSBMSOSM",
            To = "MYBASOSM",
            Status = "RJCT",
            Reason = LongReason,
            Original = PaymentRequest()
        };

        var xml = PaymentRequestResponseBuilder.Build(response);

        xml.Should().Contain("<document:Prtry>NARR</document:Prtry>");
        xml.Should().Contain(LongReason);
    }

    [Fact]
    public void PaymentStatusRequestResponseBuilder_Builds_WhenReasonExceedsIsoMax35()
    {
        var response = new PaymentStatusRequestResponseBuilder.Response
        {
            From = "SSBMSOSM",
            To = "MYBASOSM",
            Status = "RJCT",
            Reason = LongReason,
            Original = PaymentRequest()
        };

        var xml = PaymentStatusRequestResponseBuilder.Build(response);

        xml.Should().Contain("<document:Prtry>NARR</document:Prtry>");
        xml.Should().Contain(LongReason);
    }

    [Fact]
    public void ReturnPaymentResponseBuilder_Builds_WhenReasonExceedsIsoMax35()
    {
        var response = new ReturnPaymentResponseBuilder.Response
        {
            From = "SSBMSOSM",
            To = "MYBASOSM",
            Status = "RJCT",
            Reason = LongReason,
            Original = new ReturnPaymentRequestBuilder.Request
            {
                From = "MYBASOSM",
                To = "SSBMSOSM",
                BizMsgIdr = "BIZ",
                MsgDefIdr = "pacs.004.001.11",
                CreDt = DateTime.UtcNow,
                OrgnlTxId = "TX",
                OriginalEndToEnd = "E2E",
                OriginalAmount = 1,
                OriginalCurrency = "USD"
            }
        };

        var xml = ReturnPaymentResponseBuilder.Build(response);

        xml.Should().Contain("<document:Prtry>NARR</document:Prtry>");
        xml.Should().Contain(LongReason);
    }

    [Fact]
    public void PayeeVerificationResponseBuilder_Builds_WhenReasonExceedsIsoMax35()
    {
        var response = new PayeeVerificationResponseBuilder.Request
        {
            From = "SSBMSOSM",
            To = "MYBASOSM",
            Verified = false,
            Reason = LongReason,
            Original = new PayeeVerificationBuilder.Request
            {
                From = "MYBASOSM",
                To = "SSBMSOSM",
                MsgId = "MSG",
                MsgDefIdr = "acmt.023.001.03",
                CreDt = DateTime.UtcNow,
                SIPSRequestId = "REQ",
                Alias = "ALIAS",
                Type = "ACCT"
            }
        };

        var xml = PayeeVerificationResponseBuilder.Build(response);

        xml.Should().Contain("<document:Prtry>MISS</document:Prtry>");
    }

    private static PaymentRequestBuilder.Request PaymentRequest()
    {
        return new PaymentRequestBuilder.Request
        {
            From = "MYBASOSM",
            To = "SSBMSOSM",
            BizMsgIdr = "BIZ",
            MsgDefIdr = "pacs.008.001.10",
            MsgId = "MSG",
            CreDt = DateTime.UtcNow,
            TxId = "TX",
            EndToEndId = "E2E",
            Amount = 1,
            Currency = "USD",
            SettlementMethod = SettlementMethod1Code.CLRG,
            Debtor = new Person { Name = "Debtor", Address = "NA", Account = "D1", AccountType = "ACCT", Issuer = "C" },
            Creditor = new Person { Name = "Creditor", Address = "NA", Account = "C1", AccountType = "ACCT", Issuer = "C" }
        };
    }
}
