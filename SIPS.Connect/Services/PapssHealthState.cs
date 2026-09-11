namespace SIPS.Connect.Services;

public interface IPapssHealthState
{
    DateTimeOffset? LastSuccessfulObservation { get; }
    DateTimeOffset? LastFailure { get; }
    string? LastFailureReason { get; }
    void RecordSuccess();
    void RecordFailure(Exception error);
}

public sealed class PapssHealthState : IPapssHealthState
{
    private long _lastSuccessTicks;
    private long _lastFailureTicks;
    private string? _lastFailureReason;
    public DateTimeOffset? LastSuccessfulObservation => Interlocked.Read(ref _lastSuccessTicks) is var ticks && ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    public DateTimeOffset? LastFailure => Interlocked.Read(ref _lastFailureTicks) is var ticks && ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    public string? LastFailureReason => Volatile.Read(ref _lastFailureReason);
    public void RecordSuccess() => Interlocked.Exchange(ref _lastSuccessTicks, DateTimeOffset.UtcNow.Ticks);
    public void RecordFailure(Exception error) { Volatile.Write(ref _lastFailureReason, error.GetType().Name); Interlocked.Exchange(ref _lastFailureTicks, DateTimeOffset.UtcNow.Ticks); }
}
