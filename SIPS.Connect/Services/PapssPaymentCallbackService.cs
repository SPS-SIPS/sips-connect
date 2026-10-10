using SIPS.PostgreSQL.Enums;

namespace SIPS.Connect.Services;

public interface IPapssPaymentCallbackService
{
    /// <summary>pacs.002.001.12 for a PAPSS payment / return / status enquiry / recall: stored (de-duplicated), correlated, applied, bank push queued.</summary>
    Task<PapssIngestResult> HandleStatusReportAsync(PapssParticipantBinding participant, string signedPacs002, CancellationToken ct);
    /// <summary>pacs.004.001.11 (a return of a payment this participant sent): stored, linked, payment marked RETURNED, bank push queued.</summary>
    Task<PapssIngestResult> HandleReturnAsync(PapssParticipantBinding participant, string signedPacs004, CancellationToken ct);
    /// <summary>camt.029.001.09 (the beneficiary refused our recall): stored (de-duplicated), correlated to the recall, bank push queued.</summary>
    Task<PapssIngestResult> HandleRecallResolutionAsync(PapssParticipantBinding participant, string signedCamt029, CancellationToken ct)
        => throw new NotSupportedException("This PAPSS callback service does not handle recall resolutions.");
    /// <summary>R2: camt.056.001.09 (a counterparty recalled a payment this institution received): stored (de-duplicated on the PAPSS source message id), linked to the received payment, bank push queued.</summary>
    Task<PapssIngestResult> HandleInboundRecallAsync(PapssParticipantBinding participant, string signedCamt056, CancellationToken ct)
        => throw new NotSupportedException("This PAPSS callback service does not handle inbound recalls.");
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
        // OrgnlMsgNmId camt.056.*: PAPSS's answer to our recall. It carries the payment's TxId/EndToEndId but goes to the recall only.
        var result = PapssRecallMessages.IsRecallAnswer(report.OriginalMessageType)
            ? await store.IngestRecallStatusAsync(signedPacs002, report, ct)
            : await store.IngestPaymentStatusAsync(signedPacs002, report, ct);
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

    public async Task<PapssIngestResult> HandleRecallResolutionAsync(PapssParticipantBinding participant, string signedCamt029, CancellationToken ct)
    {
        var message = PapssRecallMessages.ParseResolution(signedCamt029);
        var result = await store.IngestRecallResolutionAsync(signedCamt029, message, ct);
        if (result.Pushed) signal.Notify();
        return result;
    }

    public async Task<PapssIngestResult> HandleInboundRecallAsync(PapssParticipantBinding participant, string signedCamt056, CancellationToken ct)
    {
        var message = PapssRecallMessages.ParseInboundRecall(signedCamt056);
        // The gateway resolves OriginalTxId/OriginalEndToEndId against ITS OWN admission of the received payment (never
        // PAPSS's OrgnlMsgId, the recalling bank's own wire id); a received payment wins over an outbound one with the
        // same TxId, same as R1's return path.
        var original = await store.FindOriginalPaymentAsync(message.OriginalTxId, message.OriginalEndToEndId, preferInbound: true, ct);
        var (operation, created) = await store.CreateInboundRecallAsync(signedCamt056, message, original, ct);
        // Not pushed to the bank yet in this stage (see CreateInboundRecallAsync); visible via GET Recall/Inbound/{recallId}.
        return new PapssIngestResult(!created, operation, original is null ? PapssCorrelation.None : PapssCorrelation.TransactionId, PapssEventDisposition.Applied, false, null);
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
