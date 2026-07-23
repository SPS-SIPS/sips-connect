using FluentAssertions;
using SIPS.ISO20022.Models.DTOs.CB;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class BillReferenceMapperTests
{
    [Fact]
    public void Apply_Preserves_Bill_Remittance_And_Sets_InvoiceId()
    {
        var dto = new CBPaymentRequestDto
        {
            RemittanceInformation = "BILL:INV-123"
        };

        BillReferenceMapper.Apply(dto);

        dto.RemittanceInformation.Should().Be("BILL:INV-123");
        dto.InvoiceId.Should().Be("INV-123");
        dto.Upr.Should().BeNull();
    }

    [Fact]
    public void Apply_Normalizes_Obvious_Upr_Remittance()
    {
        var dto = new CBPaymentRequestDto
        {
            RemittanceInformation = "UPR-456"
        };

        BillReferenceMapper.Apply(dto);

        dto.RemittanceInformation.Should().Be("BILL:UPR-456");
        dto.Upr.Should().Be("UPR-456");
        dto.InvoiceId.Should().BeNull();
    }
}
