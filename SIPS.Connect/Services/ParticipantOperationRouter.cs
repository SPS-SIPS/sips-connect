using SIPS.Connect.Config;

namespace SIPS.Connect.Services;

using SIPS.XMLDsig.Xades.Options;

public enum DownstreamRail { Sips, Papss }
public enum ParticipantOperation { Verification, Payment, Status, Return, Readiness, Discovery, Fx }

public sealed class ParticipantRailException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IParticipantOperationRouter
{
    DownstreamRail Select(ParticipantOperation operation, string? requestedRail);
    PapssParticipantBinding ResolvePapss();
}

public sealed record PapssParticipantBinding(string Bic, string LocalCountry, IReadOnlyList<string> SendingCurrencies, string? CallbackMappingProfile, string? CallbackUrl);

public sealed class ParticipantOperationRouter(PapssFacingOptions options, XadesOptions xades, ILogger<ParticipantOperationRouter> logger)
    : IParticipantOperationRouter
{
    public DownstreamRail Select(ParticipantOperation operation, string? requestedRail)
    {
        var rail = requestedRail?.Trim();
        if (string.IsNullOrEmpty(rail))
        {
            if (operation is ParticipantOperation.Readiness or ParticipantOperation.Discovery or ParticipantOperation.Fx)
                throw new ParticipantRailException("RAIL_REQUIRED", "Rail=PAPSS is required for this operation.");
            logger.LogInformation("Local SIPS Connect selected rail SIPS for {Operation}", operation);
            return DownstreamRail.Sips;
        }

        if (rail.Equals("SIPS", StringComparison.OrdinalIgnoreCase))
        {
            if (operation is ParticipantOperation.Readiness or ParticipantOperation.Discovery or ParticipantOperation.Fx)
                throw new ParticipantRailException("OPERATION_NOT_SUPPORTED", "The operation is not available on the SIPS rail.");
            logger.LogInformation("Local SIPS Connect selected rail SIPS for {Operation}", operation);
            return DownstreamRail.Sips;
        }

        if (!rail.Equals("PAPSS", StringComparison.OrdinalIgnoreCase))
            throw new ParticipantRailException("UNKNOWN_RAIL", "Rail must be SIPS or PAPSS.");
        if (!options.Enabled)
            throw new ParticipantRailException("PAPSS_DISABLED", "PAPSS is not enabled.");
        logger.LogInformation("Local SIPS Connect selected rail PAPSS for {Operation}", operation);
        return DownstreamRail.Papss;
    }

    public PapssParticipantBinding ResolvePapss()
    {
        if (!options.Enabled) throw new ParticipantRailException("PAPSS_DISABLED", "PAPSS is not enabled.");
        var localBic = xades.BIC?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(localBic))
            throw new ParticipantRailException("PARTICIPANT_BIC_MISSING", "Xades:BIC is required for PAPSS.");

        return new(localBic, options.LocalCountry.Trim().ToUpperInvariant(), options.SendingCurrencies.Select(x => x.Trim().ToUpperInvariant()).ToArray(), options.CallbackMappingProfile, options.CallbackUrl);
    }
}
