using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SIPS.Adapter;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using CoreConstants = SIPS.Core.Constants;

namespace SIPS.Connect.Services;

/// <summary>
/// Pushes stored PAPSS payment events to the bank, using the existing callback mappings:
/// PAYMENT_STATUS (pacs.002 for a payment or a return) -> CB_CompletionNotification;
/// RETURN_RECEIVED (inbound pacs.004) -> CB_ReturnRequest; RECALL_STATUS / RECALL_RESOLUTION / RECALL_RETURNED (answers to our
/// camt.056) / RECALL_CLOSED (a manual operator close) -> CB_RecallResult. The PAPSS callback mapping profile and
/// callback URL apply (the push worker establishes the participant binding). The domestic SmartVista
/// path is not involved.
/// </summary>
public sealed class PapssPaymentEventDelivery(
    IStorageBroker db,
    ISO20022Options links,
    IJsonAdapter jsonAdapter,
    ICallbackClient callback,
    ICallbackOrchestrator callbacks,
    ICorrelationService correlation)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Bank push of a recall answer; resolved under the PAPSS callback mapping profile ({profile}.CB_RecallResult).</summary>
    public const string RecallResultMapping = "CB_RecallResult";

    public sealed record Push(string Url, string Mapping, Dictionary<string, string> Headers, object Body, string Label);

    /// <summary>Builds the bank call for a stored event. Throws <see cref="InvalidDataException"/> when the event cannot be pushed (permanent).</summary>
    public async Task<Push> BuildAsync(long eventId, CancellationToken ct)
    {
        var e = await db.PapssOperationEvents.AsNoTracking().SingleAsync(x => x.Id == eventId, ct);
        var operation = e.OperationId is { } id ? await db.PapssOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        var raw = Encoding.UTF8.GetString(e.RawXml);
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(links.Key)) headers[CoreConstants.API_Key] = links.Key;
        if (!string.IsNullOrWhiteSpace(links.Secret)) headers[CoreConstants.API_Secret] = links.Secret;
        headers["X-Papss-Source-Message-Id"] = e.SourceMessageId;

        switch (e.EventType)
        {
            case PapssEventTypes.PaymentStatus:
            {
                var report = PapssPaymentMessages.ParseStatusReport(raw);
                var status = report.Status ?? throw new InvalidDataException("The stored pacs.002 has no status.");
                var isReturn = operation?.Operation == PapssOperationType.Return;
                // One push per applied status: ACSP and a later ACSC must not collapse on the bank's idempotency key.
                headers["X-Idempotency-Key"] = $"{(isReturn ? operation!.ReturnId : report.OriginalTxId)}:{status}";
                headers["X-Transaction-Id"] = report.OriginalTxId;
                headers["X-Papss-Operation"] = operation is null ? "UNKNOWN" : UpperSnakeEnumConverter<PapssOperationType>.Of(operation.Operation);
                if (isReturn && !string.IsNullOrWhiteSpace(operation!.ReturnId)) headers["X-Return-Id"] = operation.ReturnId;
                var body = new CBCompletionNotification
                {
                    OriginalTxId = report.OriginalTxId,
                    OriginalEndToEndId = report.OriginalEndToEndId,
                    Status = status,
                    Reason = report.ReasonCode,
                    AdditionalInfo = report.AdditionalInfo
                };
                return new Push(links.CompletionNotification ?? string.Empty, CoreConstants.CB_CompletionNotification, headers, body, $"pacs.002 {status} for {report.OriginalTxId}");
            }
            case PapssEventTypes.ReturnReceived:
            {
                var message = PapssPaymentMessages.ParseReturn(raw);
                headers["X-Idempotency-Key"] = message.ReturnId;
                headers["X-Return-Id"] = message.ReturnId;
                headers["X-Transaction-Id"] = message.OriginalTxId;
                var body = new CBReturnRequestDto
                {
                    OrgnlTxId = message.OriginalTxId,
                    OriginalEndToEnd = message.OriginalEndToEndId,
                    FromBIC = message.InstructingAgent ?? string.Empty,
                    ReturnId = message.ReturnId,
                    Reason = message.ReasonCode ?? string.Empty,
                    AdditionalInfo = message.AdditionalInfo
                };
                return new Push(links.Return ?? string.Empty, CoreConstants.CB_ReturnRequest, headers, body, $"pacs.004 {message.ReturnId} for {message.OriginalTxId}");
            }
            case PapssEventTypes.RecallStatus or PapssEventTypes.RecallResolution or PapssEventTypes.RecallReturned or PapssEventTypes.RecallClosed:
            {
                if (operation is not { Operation: PapssOperationType.Recall })
                    throw new InvalidDataException($"PAPSS recall event {e.SourceMessageId} is not attached to a recall.");
                // The outcome this event reported (not the recall's current one): each answer is pushed as it happened.
                var outcome = PapssRecallRules.OutcomeOfEvent(e.EventType, e.Status, e.ReasonCode) ?? throw new InvalidDataException($"PAPSS recall event {e.SourceMessageId} has no recall outcome.");
                var responderId = e.EventType switch
                {
                    PapssEventTypes.RecallStatus => PapssRecallMessages.StatusId(raw),
                    PapssEventTypes.RecallResolution => PapssRecallMessages.ParseResolution(raw).ResponderId,
                    // RECALL_CLOSED has no PAPSS/gateway message: the "raw" bytes are the operator audit record (JSON), not XML.
                    PapssEventTypes.RecallClosed => PapssRecallMessages.ParseCloseAudit(e.RawXml).ClosedBy,
                    _ => PapssPaymentMessages.ParseReturn(raw).ReturnId
                };
                var outcomeName = UpperSnakeEnumConverter<PapssOutcome>.Of(outcome);
                headers["X-Idempotency-Key"] = $"{operation.RequestMessageId}:{outcomeName}";
                headers["X-Recall-Id"] = operation.RequestMessageId;
                headers["X-Papss-Operation"] = UpperSnakeEnumConverter<PapssOperationType>.Of(PapssOperationType.Recall);
                if (!string.IsNullOrWhiteSpace(operation.OriginalTxId)) headers["X-Transaction-Id"] = operation.OriginalTxId;
                var body = new PapssRecallResultPush
                {
                    RecallId = operation.RequestMessageId,
                    TxId = operation.OriginalTxId,
                    EndToEndId = operation.OriginalEndToEndId,
                    Outcome = outcomeName,
                    ReasonCode = e.ReasonCode,
                    ResponderId = responderId,
                    SourceMessageId = e.SourceMessageId,
                    ReceivedAt = PapssOperationResult.Iso(e.ReceivedAt)!
                };
                return new Push(links.CompletionNotification ?? string.Empty, RecallResultMapping, headers, body, $"recall {operation.RequestMessageId} {outcomeName}");
            }
            default:
                throw new InvalidDataException($"PAPSS event type {e.EventType} is not pushed to the bank.");
        }
    }

    /// <summary>Throws <see cref="CallbackDeliveryException"/> unless the bank acknowledges with 2xx.</summary>
    public async Task SendAsync(Push push, CancellationToken ct)
    {
        var cid = correlation.Create();
        var result = await callbacks.SendJsonAsync(push.Url, push.Headers, push.Body, push.Mapping, jsonAdapter, correlation, SerializerOptions, callback, ct, cid);
        if (result is null || (int)result.StatusCode is < 200 or > 299)
            throw new CallbackDeliveryException($"The participant callback did not acknowledge {push.Label}.", result?.StatusCode);
    }
}
