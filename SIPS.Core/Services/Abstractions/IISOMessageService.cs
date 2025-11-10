using System.Threading;
using System.Threading.Tasks;
using SIPS.PostgreSQL.Models;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Helpers;
using SIPS.PostgreSQL.Enums;

namespace SIPS.Core.Services.Abstractions;

public interface IISOMessageService
{
    Task<ISOMessage> RecordIncomingVerificationAsync(
        PayeeVerificationBuilder.Request request,
        string rawXml,
        CancellationToken ct);

    Task PersistResponseAsync(
        ISOMessage isoMessage,
        string status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct);

    Task<ISOMessageStatus> RecordIncomingStatusAsync(
        ISOMessage isoMessage,
        string rawXml,
        CancellationToken ct);

    Task PersistStatusResponseAsync(
        ISOMessageStatus isoMessageStatus,
        TransactionStatus status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct);

    // Transactions (pacs.008)
    Task<ISOMessage> RecordIncomingTransactionAsync(
        PaymentRequestBuilder.Request request,
        string rawXml,
        CancellationToken ct);

    Task PersistTransactionResponseAsync(
        ISOMessage isoMessage,
        TransactionStatus status,
        string reason,
        string? additionalInfo,
        string responseXml,
        string txId,
        string endToEndId,
        CancellationToken ct);

    // Returns (pacs.004)
    Task<ISOMessage> RecordIncomingReturnAsync(
        ReturnPaymentRequestBuilder.Request request,
        string rawXml,
        CancellationToken ct);

    Task PersistReturnResponseAsync(
        ISOMessage isoMessage,
        string status,
        string reason,
        string? additionalInfo,
        string responseXml,
        CancellationToken ct);
}
