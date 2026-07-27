using System;
using FluentAssertions;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class PayeeVerificationResponseBuilderTests
{
    [Fact]
    public void Build_And_Parse_Preserves_Standard_Verification_Response_Fields()
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
            Name = "P2G1|IMO-IRS-001|PAYE-2026|INV-2026-1001|USD|1500.00|20260731|TAX-123457|E58D8569",
            Address = "Imo State IRS Collection Account",
            Currency = "USD"
        };

        var xml = PayeeVerificationResponseBuilder.Build(response);
        var parsed = PayeeVerificationResponseBuilder.Parse(xml);

        parsed.Id.Should().Be("401005007403");
        parsed.Type.Should().Be("ACCT");
        parsed.Name.Should().Be("P2G1|IMO-IRS-001|PAYE-2026|INV-2026-1001|USD|1500.00|20260731|TAX-123457|E58D8569");
        parsed.Address.Should().Be("Imo State IRS Collection Account");
        parsed.Currency.Should().Be("USD");
        parsed.InvoiceId.Should().BeNull();
        parsed.Upr.Should().BeNull();
        parsed.BillReference.Should().BeNull();
        parsed.Mda.Should().BeNull();
        parsed.MdaId.Should().BeNull();
        parsed.MdaCode.Should().BeNull();
        parsed.AmountPayable.Should().BeNull();
        xml.Should().NotContain("SplmtryData");
        xml.Should().NotContain("BillDetails");
    }

    [Fact]
    public void P2GNameDescriptorParser_Parses_Descriptor_From_Name()
    {
        var ok = P2GNameDescriptorParser.TryParse(
            "P2G1|IMO-IRS-001|PAYE-2026|INV-2026-1001|USD|1500.00|20260731|TAX-123457|E58D8569",
            out var descriptor);

        ok.Should().BeTrue();
        descriptor.MdaCode.Should().Be("IMO-IRS-001");
        descriptor.ServiceCode.Should().Be("PAYE-2026");
        descriptor.InvoiceId.Should().Be("INV-2026-1001");
        descriptor.Currency.Should().Be("USD");
        descriptor.Amount.Should().Be(1500.00m);
        descriptor.DueDate.Should().Be(new DateOnly(2026, 7, 31));
        descriptor.PayerReference.Should().Be("TAX-123457");
        descriptor.Hmac.Should().Be("E58D8569");
        descriptor.RemittanceInformation.Should().Be("BILL:INV-2026-1001");
    }

    [Fact]
    public void P2GNameDescriptorParser_Ignores_Normal_Name()
    {
        var ok = P2GNameDescriptorParser.TryParse("John Doe", out _);

        ok.Should().BeFalse();
    }
}
