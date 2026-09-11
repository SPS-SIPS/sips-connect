using Microsoft.Extensions.Logging.Abstractions;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class ParticipantOperationRouterTests
{
    [Theory]
    [InlineData(ParticipantOperation.Verification)]
    [InlineData(ParticipantOperation.Payment)]
    [InlineData(ParticipantOperation.Status)]
    [InlineData(ParticipantOperation.Return)]
    public void Missing_rail_keeps_legacy_sips(ParticipantOperation operation) => Assert.Equal(DownstreamRail.Sips, Router(new()).Select("bank-a", operation, null));

    [Fact]
    public void Papss_resolves_authenticated_principal_to_configured_bic()
    {
        var router = Router(Options());
        Assert.Equal(DownstreamRail.Papss, router.Select("bank-a", ParticipantOperation.Payment, "PAPSS"));
        Assert.Equal("BANKSOSIXXX", router.ResolvePapss("bank-a", ParticipantOperation.Payment).Bic);
    }

    [Theory]
    [InlineData("bank-b", "PAPSS_NOT_ENABLED")]
    [InlineData("bank-a", "OPERATION_NOT_PERMITTED")]
    public void Participant_or_operation_mismatch_fails_closed(string principal, string code)
    {
        var error = Assert.Throws<ParticipantRailException>(() => Router(Options()).Select(principal, ParticipantOperation.Return, "PAPSS"));
        Assert.Equal(code, error.Code);
    }

    private static ParticipantOperationRouter Router(PapssFacingOptions options) => new(options, NullLogger<ParticipantOperationRouter>.Instance);
    private static PapssFacingOptions Options() => new() { Enabled = true, Participants = new(StringComparer.OrdinalIgnoreCase) { ["bank-a"] = new() { Enabled = true, Bic = "banksosixxx", AllowedOperations = ["Payment"] } } };
}
