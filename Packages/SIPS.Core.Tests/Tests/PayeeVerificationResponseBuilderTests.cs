using System;
using FluentAssertions;
using SIPS.ISO20022.Helpers;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class PayeeVerificationResponseBuilderTests
{
    [Fact]
    public void Build_And_Parse_Preserves_Bill_Details_In_Supplementary_Data()
    {
        var response = new PayeeVerificationResponseBuilder.Request
        {
            From = "P2G",
            To = "BANK01",
            Verified = true,
            Reason = "SUCC",
            Original = new PayeeVerificationBuilder.Request
            {
                From = "BANK01",
                To = "P2G",
                MsgId = "MSG-1",
                SIPSRequestId = "VERIFY-1",
                Alias = "INV-123",
                Type = "BILL",
                MsgDefIdr = "acmt.023.001.03",
                CreDt = DateTime.UtcNow
            },
            Id = "401005007403",
            Type = "ACCT",
            Name = "Treasury MDA",
            Currency = "USD",
            InvoiceId = "INV-123",
            Upr = "UPR-123",
            BillReference = "UPR-123",
            Mda = "Ministry of Finance",
            MdaId = "MDA-001",
            MdaCode = "MOF",
            AmountPayable = 125.50m
        };

        var xml = PayeeVerificationResponseBuilder.Build(response);
        var parsed = PayeeVerificationResponseBuilder.Parse(xml);

        parsed.Id.Should().Be("401005007403");
        parsed.Type.Should().Be("ACCT");
        parsed.Name.Should().Be("Treasury MDA");
        parsed.Currency.Should().Be("USD");
        parsed.InvoiceId.Should().Be("INV-123");
        parsed.Upr.Should().Be("UPR-123");
        parsed.BillReference.Should().Be("UPR-123");
        parsed.Mda.Should().Be("Ministry of Finance");
        parsed.MdaId.Should().Be("MDA-001");
        parsed.MdaCode.Should().Be("MOF");
        parsed.AmountPayable.Should().Be(125.50m);
    }
}
