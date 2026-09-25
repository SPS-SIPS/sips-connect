using SIPS.Core.Interfaces;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs.CB;

namespace SIPS.Connect.Services;

/// <summary>Wakes the PAPSS outbox workers so newly queued work does not wait for the next poll.</summary>
public interface IPapssOutboxSignal
{
    void Notify();
    Task WaitAsync(TimeSpan timeout, CancellationToken ct);
}

public sealed class PapssOutboxSignal : IPapssOutboxSignal
{
    // One semaphore per waiting worker kind would be more precise; a shared broadcast is enough here
    // because both workers re-scan the database on wake-up (the database is the source of truth).
    private readonly object _gate = new();
    private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Notify()
    {
        TaskCompletionSource previous;
        lock (_gate)
        {
            previous = _signal;
            _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        previous.TrySetResult();
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        Task signal;
        lock (_gate) signal = _signal.Task;
        await Task.WhenAny(signal, Task.Delay(timeout, ct));
        ct.ThrowIfCancellationRequested();
    }
}

/// <summary>
/// Durable inbox for PAPSS acmt.024 results: used only for callbacks validated by the PAPSS callback
/// guard (a participant binding is pushed). Other callers keep the legacy inline delivery.
/// </summary>
public sealed class PapssVerificationResultInbox(
    IParticipantCallbackContext context,
    PapssOperationStore store,
    IPapssOutboxSignal signal) : IVerificationResultInbox
{
    public async Task<VerificationResultInboxOutcome> AcceptAsync(string rawXml, PayeeVerificationResponseBuilder.Request report, CBVerificationResultDto result, CancellationToken ct)
    {
        if (context.Binding is null) return VerificationResultInboxOutcome.NotHandled;
        // Committed before returning, so the 2xx that follows is only sent after the result is durable.
        var outcome = await store.IngestVerificationResultAsync(rawXml, report, result, ct);
        if (outcome == VerificationResultInboxOutcome.Stored) signal.Notify();
        return outcome;
    }
}
