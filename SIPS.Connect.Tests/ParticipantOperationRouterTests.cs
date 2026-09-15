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
    public void Missing_rail_keeps_legacy_sips(ParticipantOperation operation)
        => Assert.Equal(DownstreamRail.Sips, Router(new()).Select(operation, null));

    [Theory]
    [InlineData(ParticipantOperation.Verification)]
    [InlineData(ParticipantOperation.Payment)]
    [InlineData(ParticipantOperation.Status)]
    [InlineData(ParticipantOperation.Return)]
    [InlineData(ParticipantOperation.Readiness)]
    [InlineData(ParticipantOperation.Discovery)]
    [InlineData(ParticipantOperation.Fx)]
    public void Enabled_deployment_can_select_every_PAPSS_operation(ParticipantOperation operation)
        => Assert.Equal(DownstreamRail.Papss, Router(Options()).Select(operation, "PAPSS"));

    [Fact]
    public void Local_binding_comes_from_single_deployment_configuration()
    {
        var binding = Router(Options()).ResolvePapss();
        Assert.Equal("BANKSOSIXXX", binding.Bic);
        Assert.Equal("SO", binding.LocalCountry);
        Assert.Equal(["SOS"], binding.SendingCurrencies);
    }

    [Fact]
    public void Disabled_deployment_rejects_PAPSS()
    {
        var error = Assert.Throws<ParticipantRailException>(() => Router(new()).Select(ParticipantOperation.Payment, "PAPSS"));
        Assert.Equal("PAPSS_DISABLED", error.Code);
    }

    [Fact]
    public void Missing_local_bic_fails_closed()
    {
        var router = new ParticipantOperationRouter(Options(), new XadesOptions(), NullLogger<ParticipantOperationRouter>.Instance);
        var error = Assert.Throws<ParticipantRailException>(() => router.ResolvePapss());
        Assert.Equal("PARTICIPANT_BIC_MISSING", error.Code);
    }

    private static ParticipantOperationRouter Router(PapssFacingOptions options) => new(options, new XadesOptions { BIC = "banksosixxx" }, NullLogger<ParticipantOperationRouter>.Instance);
    private static PapssFacingOptions Options() => new() { Enabled=true, LocalCountry="SO", SendingCurrencies=["SOS"], CallbackMappingProfile="bank", CallbackUrl="https://bank.test/callback" };
}
