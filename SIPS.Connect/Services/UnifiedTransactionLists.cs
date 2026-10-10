using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SIPS.Connect.Controllers;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;

namespace SIPS.Connect.Services;

public abstract class UnifiedListQuery
{
    public int Page { get; set; }
    public int PageSize { get; set; } = 10;
    public string? Rail { get; set; }
    public string? OperationType { get; set; }
    public string? Direction { get; set; }
}

public abstract class UnifiedListItem
{
    public string ListItemId { get; set; } = string.Empty;
    public string Rail { get; set; } = "SIPS";
    public string OperationType { get; set; } = string.Empty;
    public string Direction { get; set; } = "UNKNOWN";
    public DateTimeOffset RecordedAtUtc { get; set; }
    public TransactionStatus Status { get; set; }
    public Guid? PapssOperationId { get; set; }
    public string? RequestMessageId { get; set; }
    public string? VerificationId { get; set; }
    public string? ReturnId { get; set; }
    public string? AccountId { get; set; }
    public string? AccountType { get; set; }
    public bool? Verified { get; set; }
    public string? PaymentStatus { get; set; }
    public string? PapssOutcome { get; set; }
    public string? GatewayState { get; set; }
    public string? BankDeliveryState { get; set; }
}

/// <summary>Read-only union of legacy rows and canonical PAPSS operations. No audit copies are inserted.</summary>
public sealed class UnifiedTransactionLists(IStorageBroker db, string? localBic, string? securityProfile)
{
    private const int BatchSize = 128;
    public string? LocalBic { get; } = string.IsNullOrWhiteSpace(localBic) ? null : localBic;
    public string SecurityProfile { get; } = string.IsNullOrWhiteSpace(securityProfile) ? "WP-SIPS-XADES-SHA256RSA" : securityProfile;

    public static string? Validate(UnifiedListQuery request)
    {
        request.Rail = Normalize(request.Rail);
        request.OperationType = Normalize(request.OperationType);
        request.Direction = Normalize(request.Direction);
        if (request.OperationType == "VERIFICATION_ENQUIRY") request.OperationType = "VERIFICATION";
        if (request.Page < 0 || request.PageSize is < 1 or > 200 || (long)request.Page * request.PageSize > 100000)
            return "Page must be non-negative, PageSize must be 1–200, and the page offset must not exceed 100000.";
        if (request.Rail is not (null or "SIPS" or "PAPSS")) return "Rail must be SIPS or PAPSS.";
        if (request.Direction is not (null or "INBOUND" or "OUTBOUND")) return "Direction must be INBOUND or OUTBOUND.";
        if (request.OperationType is not (null or "PAYMENT" or "VERIFICATION" or "RETURN" or "STATUS_ENQUIRY" or "RECALL"))
            return "OperationType must be PAYMENT, VERIFICATION, RETURN, STATUS_ENQUIRY or RECALL.";
        var (status, from, to) = Filters(request);
        if (status is { Length: > 0 } && (!Enum.TryParse<TransactionStatus>(status, true, out var parsedStatus) || !Enum.IsDefined(parsedStatus))) return "Invalid Status.";
        if (from is { Length: > 0 } && !TryDate(from, out _)) return "Invalid FromDate.";
        if (to is { Length: > 0 } && !TryDate(to, out _)) return "Invalid ToDate.";
        if (request is MessageQuery m && m.Type is { Length: > 0 } && (!Enum.TryParse<ISOMessageType>(m.Type, true, out var parsedType) || !Enum.IsDefined(parsedType))) return "Invalid Type.";
        return null;
    }

    // The explicit link is authoritative. Unlinked verification audit mirrors retain the original
    // signed bytes and source identity. TxId alone is never sufficient to suppress a SIPS record.
    public IQueryable<ISOMessage> LegacyMessages(UnifiedListQuery request)
    {
        var query = db.ISOMessages.AsNoTracking().Where(m => !db.PapssOperations.Any(o =>
            o.IsoMessageId == m.Id || (o.SignedRequest != null && o.SignedRequest == m.Message &&
                (o.RequestMessageId == m.BizMsgIdr || (o.MsgId != null && o.MsgId == m.MsgId)))));
        if (request.Rail == "SIPS") query = query.Where(m => m.BusinessService == null || m.BusinessService != SecurityProfile);
        if (request.Rail == "PAPSS") query = query.Where(m => m.BusinessService == SecurityProfile);
        return query;
    }

    public Task<List<TransactionDto>> TransactionsAsync(IQueryable<TransactionDto> legacy, TransactionQuery request, CancellationToken ct)
        => MergeAsync(LegacyStream(Filter(legacy, request), ct), PapssStream(request, ToTransaction, ct), request, ct);

    public Task<List<ISOMessageDto>> MessagesAsync(IQueryable<ISOMessageDto> legacy, MessageQuery request, CancellationToken ct)
        => MergeAsync(LegacyStream(Filter(legacy, request), ct), PapssStream(request, ToMessage, ct), request, ct);

    private static IQueryable<T> Filter<T>(IQueryable<T> query, UnifiedListQuery request) where T : UnifiedListItem
    {
        if (request.OperationType != null) query = query.Where(x => x.OperationType == request.OperationType);
        if (request.Direction != null) query = query.Where(x => x.Direction == request.Direction);
        return query.OrderByDescending(x => x.RecordedAtUtc).ThenByDescending(x => x.ListItemId);
    }

    // Materialize bounded batches before yielding: two open readers on a single DbContext/connection
    // are not supported. The merge holds at most two batches plus the requested page, not whole tables.
    private static async IAsyncEnumerable<T> LegacyStream<T>(IQueryable<T> query, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var offset = 0; ; offset += BatchSize)
        {
            var batch = await query.Skip(offset).Take(BatchSize).ToListAsync(ct);
            foreach (var row in batch) yield return row;
            if (batch.Count < BatchSize) yield break;
        }
    }

    private async IAsyncEnumerable<T> PapssStream<T>(UnifiedListQuery request, Func<PapssOperation, XML, T> map,
        [EnumeratorCancellation] CancellationToken ct) where T : UnifiedListItem
    {
        if (request.Rail == "SIPS") yield break;
        var query = db.PapssOperations.AsNoTracking().AsQueryable();
        if (request.Direction != null)
        {
            var direction = request.Direction == "INBOUND" ? PapssDirection.Inbound : PapssDirection.Outbound;
            query = query.Where(o => o.Direction == direction);
        }
        if (request.OperationType == "VERIFICATION")
            query = query.Where(o => o.Operation == PapssOperationType.Verification || o.Operation == PapssOperationType.VerificationEnquiry);
        else if (request.OperationType != null)
        {
            var operation = Enum.Parse<PapssOperationType>(request.OperationType.Replace("_", ""), true);
            query = query.Where(o => o.Operation == operation);
        }
        var (status, from, to) = Filters(request);
        if (!string.IsNullOrEmpty(from)) { TryDate(from, out var date); query = query.Where(o => o.CreatedAt >= date); }
        if (!string.IsNullOrEmpty(to)) { TryDate(to, out var date); query = query.Where(o => o.CreatedAt <= date); }
        var txId = request is TransactionQuery t ? t.TransactionId : ((MessageQuery)request).TransactionId;
        var endToEnd = request is TransactionQuery t2 ? t2.EndToEndId : ((MessageQuery)request).EndToEndId;
        if (!string.IsNullOrEmpty(txId)) query = query.Where(o => o.TxId == txId || o.OriginalTxId == txId);
        if (!string.IsNullOrEmpty(endToEnd)) query = query.Where(o => o.EndToEndId == endToEnd || o.OriginalEndToEndId == endToEnd);
        var isoId = request is TransactionQuery t3 ? t3.ISOMessageId : 0;
        if (isoId > 0) query = query.Where(o => o.IsoMessageId == isoId || db.ISOMessages.Any(m => m.Id == isoId &&
            o.SignedRequest != null && o.SignedRequest == m.Message && o.RequestMessageId == m.BizMsgIdr));
        var relatedId = request is TransactionQuery t4 ? t4.RelatedToISOMessageId : ((MessageQuery)request).RelatedToISOMessageId;
        if (relatedId > 0)
        {
            var related = await db.ISOMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == relatedId, ct);
            if (related == null) yield break;
            if (!string.IsNullOrEmpty(related.TxId)) query = query.Where(o => o.TxId == related.TxId || o.OriginalTxId == related.TxId);
            if (!string.IsNullOrEmpty(related.EndToEndId)) query = query.Where(o => o.EndToEndId == related.EndToEndId || o.OriginalEndToEndId == related.EndToEndId);
        }
        query = query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id);
        for (var offset = 0; ; offset += BatchSize)
        {
            var batch = await query.Skip(offset).Take(BatchSize).ToListAsync(ct);
            var ids = batch.Where(o => o.IsoMessageId != null).Select(o => o.IsoMessageId!.Value).ToArray();
            var sourceIds = batch.Select(o => o.RequestMessageId).ToArray();
            var messageIds = batch.Where(o => o.MsgId != null).Select(o => o.MsgId!).ToArray();
            var audits = await db.ISOMessages.AsNoTracking().Include(m => m.Transactions)
                .Where(m => ids.Contains(m.Id) || sourceIds.Contains(m.BizMsgIdr) || messageIds.Contains(m.MsgId)).ToListAsync(ct);
            foreach (var operation in batch)
            {
                var xml = XML.Read(operation.SignedRequest);
                var item = map(operation, xml);
                var audit = audits.FirstOrDefault(m => operation.IsoMessageId == m.Id) ?? audits.FirstOrDefault(m =>
                    (m.BizMsgIdr == operation.RequestMessageId || (operation.MsgId != null && m.MsgId == operation.MsgId)) &&
                    operation.SignedRequest != null && m.Message.AsSpan().SequenceEqual(operation.SignedRequest));
                if (audit?.Status == TransactionStatus.CheckStatus) item.Status = TransactionStatus.CheckStatus;
                if (item is TransactionDto transaction)
                {
                    transaction.ISOMessageId = audit?.Id;
                    transaction.Id = audit?.Transactions.FirstOrDefault()?.Id;
                }
                if (item is ISOMessageDto message)
                {
                    message.ISOMessageId = audit?.Id;
                    message.Id = audit?.Id;
                    if (string.IsNullOrEmpty(message.Response)) message.Response = Decode(audit?.PapssDecision ?? audit?.Response);
                }
                if (!string.IsNullOrEmpty(status) && item.Status != Enum.Parse<TransactionStatus>(status, true)) continue;
                if (!Matches(request, item, xml)) continue;
                yield return item;
            }
            if (batch.Count < BatchSize) yield break;
        }
    }

    private static async Task<List<T>> MergeAsync<T>(IAsyncEnumerable<T> legacy, IAsyncEnumerable<T> papss,
        UnifiedListQuery request, CancellationToken ct) where T : UnifiedListItem
    {
        await using var left = legacy.GetAsyncEnumerator(ct);
        await using var right = papss.GetAsyncEnumerator(ct);
        var hasLeft = await left.MoveNextAsync();
        var hasRight = await right.MoveNextAsync();
        var result = new List<T>(request.PageSize);
        var skip = request.Page * request.PageSize;
        while ((hasLeft || hasRight) && result.Count < request.PageSize)
        {
            ct.ThrowIfCancellationRequested();
            var takeLeft = hasLeft && (!hasRight || Compare(left.Current, right.Current) >= 0);
            var row = takeLeft ? left.Current : right.Current;
            if (skip > 0) skip--; else result.Add(row);
            if (result.Count == request.PageSize) break;
            if (takeLeft) hasLeft = await left.MoveNextAsync(); else hasRight = await right.MoveNextAsync();
        }
        return result;
    }

    private static int Compare(UnifiedListItem left, UnifiedListItem right)
    {
        var date = left.RecordedAtUtc.CompareTo(right.RecordedAtUtc);
        return date != 0 ? date : StringComparer.Ordinal.Compare(left.ListItemId, right.ListItemId);
    }

    private static bool Matches(UnifiedListQuery query, UnifiedListItem row, XML xml)
    {
        var debtor = query is TransactionQuery t ? t.DebtorAccount : ((MessageQuery)query).DebtorAccount;
        var creditor = query is TransactionQuery t2 ? t2.CreditorAccount : ((MessageQuery)query).CreditorAccount;
        if (!string.IsNullOrEmpty(debtor) && xml.Account("DbtrAcct") != debtor) return false;
        if (!string.IsNullOrEmpty(creditor) && xml.Account("CdtrAcct") != creditor && row.AccountId != creditor) return false;
        if (query is TransactionQuery tr)
        {
            if (!string.IsNullOrEmpty(tr.LocalInstrument) && xml.Value("LclInstrm") != tr.LocalInstrument && row is TransactionDto dto && dto.LocalInstrument != tr.LocalInstrument) return false;
            if (!string.IsNullOrEmpty(tr.CategoryPurpose) && xml.Value("CtgyPurp") != tr.CategoryPurpose) return false;
        }
        if (query is MessageQuery m && row is ISOMessageDto message)
        {
            if (!string.IsNullOrEmpty(m.MsgId) && message.MsgId != m.MsgId) return false;
            if (!string.IsNullOrEmpty(m.BizMsgIdr) && message.BizMsgIdr != m.BizMsgIdr) return false;
            if (!string.IsNullOrEmpty(m.MsgDefIdr) && message.MsgDefIdr != m.MsgDefIdr) return false;
            if (!string.IsNullOrEmpty(m.Type) && message.MessageType != Enum.Parse<ISOMessageType>(m.Type, true)) return false;
        }
        return true;
    }

    private TransactionDto ToTransaction(PapssOperation op, XML xml)
    {
        var dto = new TransactionDto
        {
            ISOMessageId = op.IsoMessageId,
            Type = op.Operation == PapssOperationType.Payment ? (op.Direction == PapssDirection.Inbound ? TransactionType.Deposit : TransactionType.Withdrawal)
                : op.Operation == PapssOperationType.Return ? (op.Direction == PapssDirection.Inbound ? TransactionType.ReturnDeposit : TransactionType.ReturnWithdrawal) : null,
            FromBIC = xml.HeaderParty("Fr") ?? (op.Direction == PapssDirection.Outbound ? LocalBic : op.CounterpartyBic),
            TxId = op.TxId ?? op.OriginalTxId, EndToEndId = op.EndToEndId ?? op.OriginalEndToEndId,
            Amount = op.Amount, Currency = op.Currency, LocalInstrument = op.LocalInstrument ?? xml.Value("LclInstrm"),
            CategoryPurpose = xml.Value("CtgyPurp"), DebtorName = xml.Value("Dbtr", "Nm"), CreditorName = xml.Value("Cdtr", "Nm"),
            DebtorAccount = xml.Account("DbtrAcct"), CreditorAccount = xml.Account("CdtrAcct"),
            DebtorAccountType = xml.Value("DbtrAcct", "SchmeNm"), CreditorAccountType = xml.Value("CdtrAcct", "SchmeNm"),
            DebtorAgentBIC = xml.Value("DbtrAgt", "Id"), CreditorAgentBIC = xml.Value("CdtrAgt", "Id"),
            DebtorAddress = xml.Value("Dbtr", "AdrLine"), CreditorAddress = xml.Value("Cdtr", "AdrLine"), RemittanceInformation = xml.Value("RmtInf", "Ustrd")
        };
        Fill(dto, op);
        return dto;
    }

    private ISOMessageDto ToMessage(PapssOperation op, XML xml)
    {
        var dto = new ISOMessageDto
        {
            ISOMessageId = op.IsoMessageId, MessageType = op.Operation is PapssOperationType.Verification or PapssOperationType.VerificationEnquiry ? ISOMessageType.VerificationRequest
                : op.Operation == PapssOperationType.Return ? ISOMessageType.ReturnRequest : op.Operation is PapssOperationType.StatusEnquiry or PapssOperationType.Recall ? ISOMessageType.StatusRequest : ISOMessageType.TransactionRequest,
            MsgId = op.MsgId ?? xml.BodyValue("MsgId") ?? op.RequestMessageId, BizMsgIdr = op.RequestMessageId,
            MsgDefIdr = xml.HeaderValue("MsgDefIdr") ?? Definition(op.Operation),
            Date = op.CreatedAt, MessageCreatedAtUtc = op.SourceCreatedAt, Reason = op.StatusReasonCode ?? op.ReasonCode ?? op.Reason, AdditionalInfo = op.AdditionalInfo,
            FromBIC = xml.HeaderParty("Fr") ?? (op.Direction == PapssDirection.Outbound ? LocalBic : op.CounterpartyBic) ?? string.Empty,
            ToBIC = xml.HeaderParty("To") ?? (op.Direction == PapssDirection.Inbound ? LocalBic : op.CounterpartyBic) ?? string.Empty,
            Request = Decode(op.SignedRequest), Response = Decode(op.SignedResponse)
        };
        Fill(dto, op);
        return dto;
    }

    private static string Definition(PapssOperationType operation) => operation switch
    {
        PapssOperationType.Verification or PapssOperationType.VerificationEnquiry => "acmt.023.001.03",
        PapssOperationType.Return => "pacs.004.001.11",
        PapssOperationType.StatusEnquiry => SIPS.ISO20022.Enums.SupportedMessageTypes.CreditTransferStatusRequest.Id,
        PapssOperationType.Recall => PapssRecallMessages.Camt056,
        _ => "pacs.008.001.10"
    };

    private static void Fill(UnifiedListItem dto, PapssOperation op)
    {
        dto.ListItemId = "PAPSS:" + op.Id.ToString("N"); dto.PapssOperationId = op.Id; dto.Rail = "PAPSS";
        dto.OperationType = op.Operation is PapssOperationType.Verification or PapssOperationType.VerificationEnquiry ? "VERIFICATION" : UpperSnakeEnumConverter<PapssOperationType>.Of(op.Operation);
        dto.Direction = UpperSnakeEnumConverter<PapssDirection>.Of(op.Direction); dto.RecordedAtUtc = op.CreatedAt;
        dto.RequestMessageId = op.RequestMessageId; dto.VerificationId = op.VerificationId; dto.ReturnId = op.ReturnId; dto.AccountId = op.AccountId; dto.AccountType = op.AccountType; dto.Verified = op.Verified;
        dto.PaymentStatus = op.PaymentStatus; dto.PapssOutcome = UpperSnakeEnumConverter<PapssOutcome>.Of(op.PapssOutcome);
        dto.GatewayState = UpperSnakeEnumConverter<PapssGatewayState>.Of(op.GatewayState); dto.BankDeliveryState = UpperSnakeEnumConverter<PapssDeliveryState>.Of(op.BankDeliveryState);
        dto.Status = op.PapssOutcome is PapssOutcome.VerifiedMatch or PapssOutcome.Settled or PapssOutcome.Returned or PapssOutcome.RecallReturned ? TransactionStatus.Success
            : op.PapssOutcome is PapssOutcome.VerifiedNoMatch or PapssOutcome.Rejected or PapssOutcome.RecallRejectedByPapss or PapssOutcome.RecallRejectedByBeneficiary || op.GatewayState == PapssGatewayState.Rejected ? TransactionStatus.Failed
            : op.PapssOutcome is PapssOutcome.Unknown or PapssOutcome.RecallOutcomeUnresolved || op.BankDeliveryState == PapssDeliveryState.Failed ? TransactionStatus.CheckStatus : TransactionStatus.Pending;
    }

    private static (string? Status, string? From, string? To) Filters(UnifiedListQuery query) => query switch
    {
        TransactionQuery t => (t.Status, t.FromDate, t.ToDate), MessageQuery m => (m.Status, m.FromDate, m.ToDate), _ => (null, null, null)
    };
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    private static bool TryDate(string value, out DateTimeOffset date) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
    private static string Decode(byte[]? data) => data == null ? string.Empty : Encoding.UTF8.GetString(data);

    private sealed class XML(XDocument? document)
    {
        private XElement? Header => document?.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "AppHdr");
        private XElement? Body => document?.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Document");
        public string? HeaderValue(string name) => Header?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
        public string? HeaderParty(string name) => Header?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Id")?.Value;
        public string? BodyValue(string name) => Body?.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
        public string? Value(string parent, string? name = null)
        {
            var element = Body?.Descendants().FirstOrDefault(e => e.Name.LocalName == parent);
            var target = name == null ? element : element?.Descendants().FirstOrDefault(e => e.Name.LocalName == name);
            // Choice containers (Cd/Prtry, account scheme) can carry formatting whitespace.
            // Read their leaf value, rather than concatenating the container's text nodes.
            return target?.HasElements == true ? target.Descendants().FirstOrDefault(e => !e.HasElements)?.Value : target?.Value;
        }
        public string? Account(string name)
        {
            var account = Body?.Descendants().FirstOrDefault(e => e.Name.LocalName == name);
            var id = account?.Elements().FirstOrDefault(e => e.Name.LocalName == "Id");
            return id?.Elements().FirstOrDefault(e => e.Name.LocalName == "IBAN")?.Value
                ?? id?.Elements().FirstOrDefault(e => e.Name.LocalName == "Othr")?.Elements().FirstOrDefault(e => e.Name.LocalName == "Id")?.Value;
        }
        public static XML Read(byte[]? data)
        {
            if (data == null || data.Length == 0) return new(null);
            try { using var stream = new MemoryStream(data); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }); return new(XDocument.Load(reader)); }
            catch (XmlException) { return new(null); } // Historical incomplete audit data must still be listable.
        }
    }
}
