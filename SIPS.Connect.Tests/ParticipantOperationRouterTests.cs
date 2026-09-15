using Microsoft.Extensions.Logging.Abstractions;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.XMLDsig.Xades.Options;
using Microsoft.Extensions.Configuration;
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
        Assert.Equal("BANKSOSIXXX", router.ResolvePapss(ParticipantOperation.Payment, "bank-a").Bic);
    }

    [Fact]
    public void Operation_mismatch_fails_closed()
    {
        var error = Assert.Throws<ParticipantRailException>(() => Router(Options()).ResolvePapss(ParticipantOperation.Return, "bank-a"));
        Assert.Equal("OPERATION_NOT_PERMITTED", error.Code);
    }

    [Fact]
    public void Papss_with_no_participant_authority_fails_closed()
    {
        var error = Assert.Throws<ParticipantRailException>(() => Router(new() { Enabled = true }).ResolvePapss(ParticipantOperation.Verification, "bank-a"));
        Assert.Equal("PAPSS_NOT_ENABLED", error.Code);
    }

    [Fact]
    public void Configured_participants_that_do_not_match_local_bic_fail_closed()
    {
        var options = Options();
        options.Participants["bank-a"].Bic = "OTHERBIC";
        var error = Assert.Throws<ParticipantRailException>(() => Router(options).ResolvePapss(ParticipantOperation.Payment, "bank-a"));
        Assert.Equal("PAPSS_NOT_ENABLED", error.Code);
    }

    [Fact]
    public void Authenticated_principal_must_match_configuration_key_exactly()
    {
        var error = Assert.Throws<ParticipantRailException>(() => Router(Options()).ResolvePapss(ParticipantOperation.Payment, "BANK-A"));
        Assert.Equal("PAPSS_NOT_ENABLED", error.Code);
    }

    [Fact]
    public void Startup_rejects_duplicate_enabled_bics_after_normalization()
    {
        var options = new PapssFacingOptions
        {
            Enabled=true, IsoIngressUrl="https://papss.test/sips/messages", AllowedHosts=["papss.test"], Environment="UAT",
            RemoteWpSipsIdentity="PAPSS", SecurityProfile="SPS.PAPSS.FINANCIAL.001", ReadinessStaleSeconds=300,
            Participants = new()
            {
                ["bank-a"] = Participant("BANKSOSIXXX"),
                ["bank-b"] = Participant(" BANKSOSIXXX ")
            }
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Xades:BIC"]="BANKSOSIXXX", ["Endpoints:bank-a.CB_PaymentRequest:Url"]="https://bank.test/a",
            ["Endpoints:bank-b.CB_PaymentRequest:Url"]="https://bank.test/b"
        }).Build();
        var method = typeof(SIPS.Connect.Config.DI).GetMethod("ValidatePapssFacing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [options, configuration]));
        Assert.Contains("BIC must be unique", error.InnerException!.Message);

        static PapssParticipantCapability Participant(string bic) => new() { Enabled=true, Bic=bic, LocalCountry="SO", SendingCurrencies=["SOS"], AllowedOperations=["Payment"], CallbackMappingProfile=bic.Trim() == "BANKSOSIXXX" ? "bank-a" : "bank-b", CallbackUrl="https://bank.test/callback" };
    }

    private static ParticipantOperationRouter Router(PapssFacingOptions options) => new(options, new XadesOptions { BIC = "banksosixxx" }, NullLogger<ParticipantOperationRouter>.Instance);
    private static PapssFacingOptions Options() => new() { Enabled = true, Participants = new(StringComparer.OrdinalIgnoreCase) { ["bank-a"] = new() { Enabled = true, Bic = "banksosixxx", LocalCountry = "SO", SendingCurrencies = ["SOS"], AllowedOperations = ["Payment"] } } };
}
