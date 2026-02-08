using FluentAssertions;
using SIPS.Core.Services.Correlation;
using Xunit;

namespace SIPS.Core.Tests.Tests;

public class CorrelationServiceTests
{
    [Fact]
    public void Create_Returns_First_NonEmpty()
    {
        var c = new CorrelationService();
        c.Create(null, "", "A", "B").Should().Be("A");
    }

    [Fact]
    public void Create_Generates_When_All_Empty()
    {
        var c = new CorrelationService();
        var id = c.Create(null, "", null);
        id.Should().NotBeNullOrWhiteSpace();
        id.Length.Should().Be(32);
    }
}
