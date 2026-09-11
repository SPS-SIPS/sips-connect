using SIPS.Connect.Config;

namespace SIPS.Connect.Services;

public enum DownstreamRail { Sips, Papss }
public enum ParticipantOperation { Verification, Payment, Status, Return, Readiness, Discovery, Fx }

public sealed class ParticipantRailException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IParticipantOperationRouter
{
    DownstreamRail Select(string participantIdentity, ParticipantOperation operation, string? requestedRail);
    PapssParticipantBinding ResolvePapss(string participantIdentity, ParticipantOperation operation);
}

public sealed record PapssParticipantBinding(string Principal, string Bic, string? CallbackMappingProfile, string? CallbackUrl);

public sealed class ParticipantOperationRouter(PapssFacingOptions options, ILogger<ParticipantOperationRouter> logger)
    : IParticipantOperationRouter
{
    public DownstreamRail Select(string participantIdentity, ParticipantOperation operation, string? requestedRail)
    {
        var rail = requestedRail?.Trim();
        if (string.IsNullOrEmpty(rail))
        {
            if (operation is ParticipantOperation.Readiness or ParticipantOperation.Discovery or ParticipantOperation.Fx)
                throw new ParticipantRailException("RAIL_REQUIRED", "Rail=PAPSS is required for this operation.");
            logger.LogInformation("Participant {Participant} selected rail SIPS for {Operation}", participantIdentity, operation);
            return DownstreamRail.Sips;
        }

        if (rail.Equals("SIPS", StringComparison.OrdinalIgnoreCase))
        {
            if (operation is ParticipantOperation.Readiness or ParticipantOperation.Discovery or ParticipantOperation.Fx)
                throw new ParticipantRailException("OPERATION_NOT_SUPPORTED", "The operation is not available on the SIPS rail.");
            logger.LogInformation("Participant {Participant} selected rail SIPS for {Operation}", participantIdentity, operation);
            return DownstreamRail.Sips;
        }

        if (!rail.Equals("PAPSS", StringComparison.OrdinalIgnoreCase))
            throw new ParticipantRailException("UNKNOWN_RAIL", "Rail must be SIPS or PAPSS.");
        if (!options.Enabled)
            throw new ParticipantRailException("PAPSS_DISABLED", "PAPSS is not enabled.");
        ResolvePapss(participantIdentity, operation);

        logger.LogInformation("Participant {Participant} selected rail PAPSS for {Operation}", participantIdentity, operation);
        return DownstreamRail.Papss;
    }

    public PapssParticipantBinding ResolvePapss(string participantIdentity, ParticipantOperation operation)
    {
        if (!options.Enabled) throw new ParticipantRailException("PAPSS_DISABLED", "PAPSS is not enabled.");
        if (!options.Participants.TryGetValue(participantIdentity, out var capability) || !capability.Enabled)
            throw new ParticipantRailException("PAPSS_NOT_ENABLED", "The authenticated participant is not enabled for PAPSS.");
        if (string.IsNullOrWhiteSpace(capability.Bic))
            throw new ParticipantRailException("PARTICIPANT_BIC_MISSING", "The authenticated participant has no configured BIC.");
        if (!capability.AllowedOperations.Contains(operation.ToString(), StringComparer.OrdinalIgnoreCase))
            throw new ParticipantRailException("OPERATION_NOT_PERMITTED", "The authenticated participant is not permitted to perform this PAPSS operation.");
        return new(participantIdentity, capability.Bic.Trim().ToUpperInvariant(), capability.CallbackMappingProfile, capability.CallbackUrl);
    }
}
