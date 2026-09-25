using System.Text;
using System.Xml.Linq;
using SIPS.Connect.Config;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Correlation;
using SIPS.ISO20022.Helpers;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Models;

namespace SIPS.Connect.Services;

public interface IPapssInboundVerificationService
{
    /// <summary>
    /// Durably stores an inbound PAPSS acmt.023, answers it from the core bank and queues the signed
    /// acmt.024.001.03 reply for the gateway. Returns only after everything it did is committed.
    /// </summary>
    Task HandleAsync(PapssParticipantBinding participant, string signedAcmt023, CancellationToken ct);
}

/// <summary>
/// Inbound verification enquiry from another PAPSS country. The gateway delivers the acmt.023 with
/// AppHdr BizMsgIdr = Assgnmt/MsgId = the PAPSS source message id (P_MSG) and Vrfctn/Id = the PAPSS
/// verification id (P_VID). The answer is returned to the gateway ingress as a NEW signed acmt.024.001.03
/// (its own BizMsgIdr) whose Original.BizMsgIdr/MsgId = P_MSG and OrgnlId = P_VID.
/// </summary>
public sealed class PapssInboundVerificationService(
    PapssOperationStore store,
    ICoreBankVerificationClient coreBank,
    IPapssFacingSipsClient papss,
    PapssFacingOptions options,
    IPapssOutboxSignal signal,
    IISOMessageService isoMessages,
    ICorrelationService correlation,
    ILogger<PapssInboundVerificationService> logger) : IPapssInboundVerificationService
{
    public async Task HandleAsync(PapssParticipantBinding participant, string signedAcmt023, CancellationToken ct)
    {
        PayeeVerificationBuilder.Request request;
        try { request = PayeeVerificationBuilder.Parse(signedAcmt023); }
        catch (Exception error) when (error is not OperationCanceledException) { throw new InvalidDataException("The PAPSS acmt.023 could not be parsed.", error); }

        var sourceMessageId = Required(request.BizMsgIdr, "AppHdr BizMsgIdr");
        Required(request.MsgId, "Assgnmt/MsgId");
        Required(request.SIPSRequestId, "Vrfctn/Id");
        if (!string.Equals(request.BizMsgIdr, request.MsgId, StringComparison.Ordinal))
            logger.LogWarning("PAPSS acmt.023 {BizMsgIdr} carries a different Assgnmt/MsgId {MsgId}; keyed on BizMsgIdr", request.BizMsgIdr, request.MsgId);

        var (operation, created) = await store.CreateInboundEnquiryAsync(request, sourceMessageId, signedAcmt023, ct);
        if (!created)
        {
            // PAPSS redelivers until it is acknowledged. Never call the core bank again and never produce a
            // different answer: an existing reply is kept byte-identical (only re-queued if its retries ran out).
            var reply = await store.EnsureReplyQueuedAsync(operation.Id, ct);
            if (reply?.State == PapssResponseState.Pending) signal.Notify();
            logger.LogInformation(
                "PAPSS acmt.023 {SourceMessageId} redelivered; bank state {BankDeliveryState}, reply {ReplyState}; core bank not called again",
                sourceMessageId, operation.BankDeliveryState, reply?.State.ToString() ?? "none");
            return;
        }

        var cid = correlation.Create();
        if (operation.DeadlineAt is { } deadline)
            logger.LogInformation("[{CorrelationId}] PAPSS acmt.023 {SourceMessageId} recorded with response deadline {DeadlineAt:o} (record-only)", cid, sourceMessageId, deadline);

        // The inbound message is already durable. Do not let a gateway disconnect abandon the enquiry
        // half-way: the core-bank call is bounded by Core:CoreBankTimeoutSeconds.
        var durable = CancellationToken.None;
        var result = await coreBank.VerifyAsync(request, cid, durable);
        if (!result.Answered)
        {
            // Unresolved PAPSS behaviour: whether a participant may answer a core-bank timeout with a negative
            // acmt.024, and with which reason code, is not established. No reply is fabricated.
            await store.RecordInboundCoreBankFailureAsync(operation.Id, result.FailureReason ?? "CORE_BANK_FAILED", result.StatusCode?.ToString(), durable);
            logger.LogWarning(
                "[{CorrelationId}] Core bank did not answer PAPSS acmt.023 {SourceMessageId} ({FailureReason}); no acmt.024 is sent (negative-reply rules are unresolved)",
                cid, sourceMessageId, result.FailureReason);
            return;
        }

        var answer = result.Response;
        answer.Original = request;
        answer.From = participant.Bic;
        answer.To = request.From;
        answer.VerificationId = request.SIPSRequestId ?? string.Empty;
        // Pass the core bank's reason through unchanged; never substitute a default (PAPSS reason-code
        // rules are contradictory, see docs). The builder would otherwise default an empty reason to MISS.
        answer.Reason = result.BankReason;
        var unsigned = ApplyBankReason(PayeeVerificationResponseBuilder.Build(answer), result.BankReason, sourceMessageId);
        var replyId = PapssFacingSipsClient.Id();
        var signed = papss.SignForSubmission(unsigned, replyId);

        await store.RecordInboundAnswerAndQueueReplyAsync(operation.Id, answer, result.BankReason, replyId, signed, durable);
        signal.Notify();
        logger.LogInformation("[{CorrelationId}] PAPSS acmt.023 {SourceMessageId} answered (Verified={Verified}); acmt.024 {ReplyId} queued for the gateway", cid, sourceMessageId, answer.Verified, replyId);

        await RecordLegacyIsoMessageAsync(request, signedAcmt023, answer, result.BankReason, signed);
    }

    /// <summary>
    /// Makes Rpt/Rsn carry exactly the core-bank reason, or removes it when the bank gave none (or one
    /// that is not a valid Max35Text), instead of the builder's MISS/SUCC default.
    /// </summary>
    internal string ApplyBankReason(string xml, string bankReason, string sourceMessageId)
    {
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader, LoadOptions.None);
        var reasons = document.Descendants().Where(x => x.Name.LocalName == "Rsn" && x.Parent?.Name.LocalName == "Rpt").ToList();
        var trimmed = bankReason?.Trim() ?? string.Empty;
        var safe = IsoText.Max35Text(trimmed, string.Empty);
        if (safe.Length != 0 && safe == trimmed)
        {
            foreach (var reason in reasons)
                foreach (var code in reason.Elements()) code.Value = safe;
            return document.ToString(SaveOptions.DisableFormatting);
        }
        if (trimmed.Length != 0)
            logger.LogWarning("Core-bank reason for PAPSS acmt.023 {SourceMessageId} is not a valid Max35Text and is omitted from the acmt.024 (kept in the operation store)", sourceMessageId);
        else
            logger.LogWarning("Core bank gave no reason for PAPSS acmt.023 {SourceMessageId}; the acmt.024 carries no Rsn (no default is invented)", sourceMessageId);
        foreach (var reason in reasons) reason.Remove();
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private async Task RecordLegacyIsoMessageAsync(PayeeVerificationBuilder.Request request, string raw, PayeeVerificationResponseBuilder.Request answer, string bankReason, string signedReply)
    {
        // Keeps the existing isomessages VerificationRequest audit row (dashboards/reports). Best-effort:
        // the PAPSS operation store above is authoritative and already committed.
        try
        {
            var (record, outcome, _) = await isoMessages.TryRecordIncomingVerificationAsync(new ISOMessage
            {
                MessageType = ISOMessageType.VerificationRequest,
                Date = DateTimeOffset.UtcNow,
                FromBIC = request.From,
                ToBIC = request.To,
                Message = Encoding.UTF8.GetBytes(raw),
                Status = TransactionStatus.Pending,
                BizMsgIdr = request.BizMsgIdr,
                MsgDefIdr = request.MsgDefIdr,
                MsgId = request.MsgId,
                TxId = request.MsgId,
                // BusinessService deliberately left empty: the PAPSS payment-decision outbox scans
                // isomessages rows carrying the PAPSS security profile.
                UETR = request.MsgId
            }, CancellationToken.None);
            if (outcome == DedupOutcome.Owner)
                await isoMessages.PersistResponseAsync(record, answer.Verified ? TransactionStatus.Success : TransactionStatus.Failed, bankReason, answer.AdditionalInfo, signedReply, CancellationToken.None);
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "PAPSS acmt.023 {MsgId} could not be mirrored into isomessages (non-fatal)", request.MsgId);
        }
    }

    private static string Required(string? value, string name) => !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"The PAPSS acmt.023 is missing {name}.");
}
