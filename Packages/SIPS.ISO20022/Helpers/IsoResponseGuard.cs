namespace SIPS.ISO20022.Helpers;

/// <summary>
/// Enforces response values which are required for ISO 20022 correlation.
/// These values must come from the verified inbound message and must never be
/// fabricated merely to make XML serialization succeed.
/// </summary>
public static class IsoResponseGuard
{
    public static void RequireOriginalCorrelation(
        string? from,
        string? to,
        string? bizMsgIdr,
        string? msgDefIdr,
        string? msgId,
        DateTime createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizMsgIdr);
        ArgumentException.ThrowIfNullOrWhiteSpace(msgDefIdr);
        ArgumentException.ThrowIfNullOrWhiteSpace(msgId);

        if (createdAt == default)
            throw new ArgumentException("The original AppHdr creation time is required.", nameof(createdAt));
    }

    /// <summary>
    /// Returns a UTC acceptance timestamp only when it represents a possible
    /// sequence: no later than now and, when supplied, no earlier than the
    /// underlying transaction boundary.
    /// </summary>
    public static DateTime? ValidAcceptanceDate(DateTime? candidate, DateTime? earliestAllowedAt = null)
    {
        if (candidate is null || candidate == default)
            return null;

        var acceptanceUtc = ToUtc(candidate.Value);
        var nowUtc = DateTime.UtcNow;
        if (acceptanceUtc > nowUtc)
            return null;

        if (earliestAllowedAt is { } earliest && earliest != default && acceptanceUtc < ToUtc(earliest))
            return null;

        return acceptanceUtc;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
