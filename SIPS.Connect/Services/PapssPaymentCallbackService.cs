namespace SIPS.Connect.Services;

public interface IPapssPaymentCallbackService
{
    /// <summary>pacs.002.001.12 for a PAPSS payment / return / status enquiry: stored (de-duplicated), correlated, applied, bank push queued.</summary>
    Task<PapssIngestResult> HandleStatusReportAsync(PapssParticipantBinding participant, string signedPacs002, CancellationToken ct);
    /// <summary>pacs.004.001.11 (a return of a payment this participant sent): stored, linked, payment marked RETURNED, bank push queued.</summary>
    Task<PapssIngestResult> HandleReturnAsync(PapssParticipantBinding participant, string signedPacs004, CancellationToken ct);
    /// <summary>pacs.008.001.10 after the legacy handler answered it: records the inbound PAYMENT operation and links its isomessages decision.</summary>
    Task RecordInboundPaymentAsync(PapssParticipantBinding participant, string signedPacs008, CancellationToken ct);
    /// <summary>Re-reads the decision outbox state of an inbound payment into its operation.</summary>
    Task SyncInboundPaymentAsync(string signedPacs008, CancellationToken ct);
}

/// <summary>
/// PAPSS-rail payment callbacks validated by the PAPSS callback guard. Each method returns only after
/// everything it did is committed, so the 2xx the gateway receives means "durably stored".
/// </summary>
public sealed class PapssPaymentCallbackService(PapssOperationStore store, IPapssOutboxSignal signal) : IPapssPaymentCallbackService
{
    public async Task<PapssIngestResult> HandleStatusReportAsync(PapssParticipantBinding participant, string signedPacs002, CancellationToken ct)
    {
        var report = PapssPaymentMessages.ParseStatusReport(signedPacs002);
        var result = await store.IngestPaymentStatusAsync(signedPacs002, report, ct);
        if (result.Pushed) signal.Notify();
        return result;
    }

    public async Task<PapssIngestResult> HandleReturnAsync(PapssParticipantBinding participant, string signedPacs004, CancellationToken ct)
    {
        var message = PapssPaymentMessages.ParseReturn(signedPacs004);
        var result = await store.IngestInboundReturnAsync(signedPacs004, message, ct);
        if (result.Pushed) signal.Notify();
        return result;
    }

    public async Task RecordInboundPaymentAsync(PapssParticipantBinding participant, string signedPacs008, CancellationToken ct)
        => await store.RecordInboundPaymentAsync(signedPacs008, PapssPaymentMessages.ParsePayment(signedPacs008), ct);

    public async Task SyncInboundPaymentAsync(string signedPacs008, CancellationToken ct)
    {
        var message = PapssPaymentMessages.ParsePayment(signedPacs008);
        if (await store.FindInboundPaymentAsync(message.SourceMessageId, message.TxId, ct) is { } operation)
            await store.SyncInboundPaymentDecisionAsync(operation.Id, ct);
    }
}
