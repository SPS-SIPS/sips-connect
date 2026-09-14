using Microsoft.Extensions.Logging.Abstractions;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.XMLDsig.Xades.Options;
using Xunit;

namespace SIPS.Connect.Tests;

public sealed class ParticipantOperationRouterTests
{
    [Theory]
    [InlineData(ParticipantOperation.Verification)]
    [InlineData(ParticipantOperation.Payment)]
    [InlineData(ParticipantOperation.Status)]
    [InlineData(ParticipantOperation.Return)]
    public void Missing_rail_keeps_legacy_sips_without_an_identity_claim(ParticipantOperation operation) => Assert.Equal(DownstreamRail.Sips, Router(new()).Select(operation, null));

    [Fact]
    public void Papss_resolves_local_xades_bic_to_configured_capability()
    {
        var router = Router(Options());
        Assert.Equal(DownstreamRail.Papss, router.Select(ParticipantOperation.Payment, "PAPSS"));
        Assert.Equal("BANKSOSIXXX", router.ResolvePapss(ParticipantOperation.Payment).Bic);
    }

    [Fact]
    public void Operation_mismatch_fails_closed()
    {
        var error = Assert.Throws<ParticipantRailException>(() => Router(Options()).Select(ParticipantOperation.Return, "PAPSS"));
        Assert.Equal("OPERATION_NOT_PERMITTED", error.Code);
    }

    [Fact]
    public void Papss_with_no_capability_map_uses_local_xades_bic()
    {
        var binding = Router(new() { Enabled = true }).ResolvePapss(ParticipantOperation.Verification);
        Assert.Equal("BANKSOSIXXX", binding.Bic);
        Assert.Equal("BANKSOSIXXX", binding.Principal);
    }

    [Fact]
    public void Configured_participants_that_do_not_match_local_bic_fail_closed()
    {
        var options = Options();
        options.Participants["bank-a"].Bic = "OTHERBIC";
        var error = Assert.Throws<ParticipantRailException>(() => Router(options).Select(ParticipantOperation.Payment, "PAPSS"));
        Assert.Equal("PAPSS_NOT_ENABLED", error.Code);
    }

    private static ParticipantOperationRouter Router(PapssFacingOptions options) => new(options, new XadesOptions { BIC = "banksosixxx" }, NullLogger<ParticipantOperationRouter>.Instance);
    private static PapssFacingOptions Options() => new() { Enabled = true, Participants = new(StringComparer.OrdinalIgnoreCase) { ["bank-a"] = new() { Enabled = true, Bic = "banksosixxx", AllowedOperations = ["Payment"] } } };
}
