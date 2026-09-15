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
    PapssParticipantBinding ResolvePapss(ParticipantOperation operation, string? authenticatedPrincipal = null);
}

public sealed record PapssParticipantBinding(string Principal, string Bic, string LocalCountry, IReadOnlyList<string> SendingCurrencies, string? CallbackMappingProfile, string? CallbackUrl);

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
        logger.LogInformation("PAPSS rail selected for {Operation}; authenticated participant resolution follows", operation);
        return DownstreamRail.Papss;
    }

    public PapssParticipantBinding ResolvePapss(ParticipantOperation operation, string? authenticatedPrincipal = null)
    {
        if (!options.Enabled) throw new ParticipantRailException("PAPSS_DISABLED", "PAPSS is not enabled.");
        var localBic = xades.BIC?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(localBic))
            throw new ParticipantRailException("PARTICIPANT_BIC_MISSING", "Xades:BIC is required for PAPSS.");

        if (string.IsNullOrWhiteSpace(authenticatedPrincipal))
            throw new ParticipantRailException("PARTICIPANT_IDENTITY_MISSING", "An authenticated participant principal is required for PAPSS.");
        var matches = options.Participants
            .Where(entry => entry.Value.Enabled && string.Equals(entry.Key, authenticatedPrincipal.Trim(), StringComparison.Ordinal) && string.Equals(entry.Value.Bic?.Trim(), localBic, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
            throw new ParticipantRailException("PAPSS_NOT_ENABLED", "The local Xades:BIC does not resolve to exactly one enabled PAPSS participant configuration.");
        var (participant, capability) = matches[0];
        if (!capability.AllowedOperations.Contains(operation.ToString(), StringComparer.OrdinalIgnoreCase))
            throw new ParticipantRailException("OPERATION_NOT_PERMITTED", "The local participant is not permitted to perform this PAPSS operation.");
        return new(participant, localBic, capability.LocalCountry.Trim().ToUpperInvariant(), capability.SendingCurrencies.Select(x => x.Trim().ToUpperInvariant()).ToArray(), capability.CallbackMappingProfile, capability.CallbackUrl);
    }
}
