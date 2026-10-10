using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SIPS.Connect.Controllers;
using SIPS.ISO20022.Helpers;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Options;
using Xunit;

namespace SIPS.Connect.PostgresTests;

public sealed class UnifiedTransactionListTests
{
    [Fact]
    public async Task Both_lists_include_outbound_and_inbound_operations_without_audit_duplicates()
    {
        await using var scenario = await Scenario.Create();
        var transactions = await scenario.Transactions(new());
        Assert.Equal(5, transactions.Count);
        Assert.Equal(4, transactions.Count(x => x.Rail == "PAPSS"));
        Assert.Equal(2, transactions.Count(x => x.OperationType == "VERIFICATION"));
        Assert.Equal(5, transactions.Select(x => x.ListItemId).Distinct().Count());
        Assert.Equal(2, transactions.Count(x => x.TxId == "SHARED-TX")); // A domestic payment is not a PAPSS audit copy.
        Assert.All(transactions.Where(x => x.OperationType == "VERIFICATION"), x => Assert.Null(x.Type));
        Assert.Equal(scenario.InboundAuditId, transactions.Single(x => x.RequestMessageId == "P-IN").ISOMessageId);
        var messages = await scenario.Messages(new());
        Assert.Equal(6, messages.Count);
        Assert.Equal(4, messages.Count(x => x.Rail == "PAPSS"));
        var enquiry = messages.Single(x => x.RequestMessageId == "V-IN");
        Assert.Equal(scenario.VerificationAuditId, enquiry.Id);
        Assert.Equal("VERIFICATION", enquiry.OperationType);
        Assert.Equal("INBOUND", enquiry.Direction);
        Assert.Equal("acmt.023.001.03", enquiry.MsgDefIdr);
        Assert.Equal("VERIFY-IN", enquiry.VerificationId);
    }

    [Theory]
    [InlineData("PAPSS", "VERIFICATION", "INBOUND", 1)]
    [InlineData("papss", "verification", "outbound", 1)]
    [InlineData("PAPSS", "PAYMENT", null, 2)]
    [InlineData("SIPS", "PAYMENT", "OUTBOUND", 1)]
    [InlineData("SIPS", "VERIFICATION", "INBOUND", 1)]
    public async Task Rail_operation_and_direction_filters_apply_before_pagination(string rail, string operation, string? direction, int count)
    {
        await using var scenario = await Scenario.Create();
        var messages = await scenario.Messages(new() { Rail = rail, OperationType = operation, Direction = direction, PageSize = 1 });
        Assert.Single(messages);
        Assert.Equal(rail.ToUpperInvariant(), messages[0].Rail);
        Assert.Equal(operation.ToUpperInvariant(), messages[0].OperationType);
        if (direction != null) Assert.Equal(direction.ToUpperInvariant(), messages[0].Direction);
        Assert.Equal(count - 1, (await scenario.Messages(new() { Rail = rail, OperationType = operation, Direction = direction, Page = 1, PageSize = 1 })).Count);
    }

    [Fact]
    public async Task Pagination_is_global_and_stable_across_rails()
    {
        await using var scenario = await Scenario.Create();
        var all = await scenario.Messages(new() { PageSize = 20 });
        var pages = new List<ISOMessageDto>();
        for (var page = 0; page < 3; page++) pages.AddRange(await scenario.Messages(new() { Page = page, PageSize = 2 }));
        Assert.Equal(all.Select(x => x.ListItemId), pages.Select(x => x.ListItemId));
        Assert.Equal(new[] { "V-IN", "V-OUT", "P-IN", "P-OUT", null, null }, all.Select(x => x.RequestMessageId));
        Assert.Empty(await scenario.Messages(new() { Page = 3, PageSize = 2 }));
    }

    [Fact]
    public async Task Pagination_crosses_both_source_batch_boundaries_without_gaps_or_duplicates()
    {
        await using var scenario = await Scenario.Create();
        for (var i = 0; i < 150; i++)
        {
            scenario.Storage.PapssOperations.Add(new PapssOperation
            {
                Id = Guid.Parse($"{i + 1:x8}-0000-0000-0000-000000000000"), RequestMessageId = $"B-P-{i}",
                Operation = PapssOperationType.Verification, Direction = PapssDirection.Outbound,
                CreatedAt = scenario.Now.AddMinutes(1), UpdatedAt = scenario.Now
            });
            scenario.Storage.ISOMessages.Add(new ISOMessage
            {
                MessageType = ISOMessageType.VerificationRequest, MsgId = $"B-S-{i}", BizMsgIdr = $"B-S-{i}",
                FromBIC = PostgresHarness.LocalBic, ToBIC = PostgresHarness.ForeignBic,
                Date = scenario.Now.AddMinutes(1), Message = [], MsgDefIdr = "acmt.023.001.03"
            });
        }
        await scenario.Storage.SaveChangesAsync(CancellationToken.None);
        var rows = await scenario.Messages(new() { PageSize = 200 });
        rows.AddRange(await scenario.Messages(new() { PageSize = 200, Page = 1 }));
        Assert.Equal(306, rows.Count);
        Assert.Equal(306, rows.Select(x => x.ListItemId).Distinct().Count());
        var tied = rows.Where(x => x.RecordedAtUtc == scenario.Now.AddMinutes(1)).Select(x => x.ListItemId).ToList();
        Assert.Equal(tied.OrderByDescending(x => x, StringComparer.Ordinal), tied);
    }

    [Fact]
    public async Task Existing_identifier_account_date_and_status_filters_work_for_canonical_operations()
    {
        await using var scenario = await Scenario.Create();
        Assert.Single(await scenario.Transactions(new() { TransactionId = "SHARED-TX", Rail = "PAPSS" }));
        var byAccount = Assert.Single(await scenario.Transactions(new() { CreditorAccount = "P-CRED", Rail = "PAPSS" }));
        Assert.Equal("P-CRED", byAccount.CreditorAccount);
        Assert.Equal("P-DEBT", byAccount.DebtorAccount);
        Assert.Equal("USDP", byAccount.LocalInstrument);
        Assert.Equal("CASH", byAccount.CategoryPurpose);
        Assert.Single(await scenario.Transactions(new() { DebtorAccount = "P-DEBT", LocalInstrument = "USDP", CategoryPurpose = "CASH" }));
        Assert.Single(await scenario.Transactions(new() { ISOMessageId = scenario.InboundAuditId }));
        Assert.Single(await scenario.Transactions(new() { ISOMessageId = scenario.VerificationAuditId, OperationType = "VERIFICATION" }));
        Assert.Single(await scenario.Messages(new() { Rail = "PAPSS", Status = "CheckStatus" }));
        Assert.Single(await scenario.Messages(new() { MsgId = "VERIFY-IN-MSG", BizMsgIdr = "V-IN", Type = "VerificationRequest" }));
        var recent = await scenario.Messages(new() { FromDate = scenario.Now.AddMinutes(-2).ToString("O") });
        Assert.Equal(2, recent.Count);
        Assert.All(recent, x => Assert.Equal("VERIFICATION", x.OperationType));
    }

    [Fact]
    public async Task Missing_signed_request_is_still_listed_with_operation_metadata()
    {
        await using var scenario = await Scenario.Create();
        var operation = new PapssOperation
        {
            Operation = PapssOperationType.Verification, Direction = PapssDirection.Outbound,
            RequestMessageId = "HISTORICAL", CreatedAt = scenario.Now.AddMinutes(1), UpdatedAt = scenario.Now,
            AccountId = "OLD-ACCOUNT", GatewayState = PapssGatewayState.SubmissionUnknown, PapssOutcome = PapssOutcome.Unknown
        };
        scenario.Storage.PapssOperations.Add(operation);
        await scenario.Storage.SaveChangesAsync(CancellationToken.None);
        var row = Assert.Single(await scenario.Messages(new() { BizMsgIdr = "HISTORICAL" }));
        Assert.Equal(operation.Id, row.PapssOperationId);
        Assert.Equal(TransactionStatus.CheckStatus, row.Status);
        Assert.Null(row.Id);
        Assert.Equal("OLD-ACCOUNT", row.AccountId);
    }

    [Fact]
    public async Task Return_recall_and_status_enquiry_have_distinct_operation_metadata()
    {
        await using var scenario = await Scenario.Create();
        foreach (var type in new[] { PapssOperationType.Return, PapssOperationType.Recall, PapssOperationType.StatusEnquiry })
            scenario.Storage.PapssOperations.Add(new PapssOperation
            {
                RequestMessageId = "EXTRA-" + type, Operation = type, Direction = PapssDirection.Outbound,
                CreatedAt = scenario.Now, UpdatedAt = scenario.Now, OriginalTxId = "SHARED-TX",
                ReturnId = type == PapssOperationType.Return ? "RETURN-ID" : null
            });
        await scenario.Storage.SaveChangesAsync(CancellationToken.None);
        var returned = Assert.Single(await scenario.Transactions(new() { OperationType = "RETURN", Direction = "OUTBOUND" }));
        Assert.Equal(TransactionType.ReturnWithdrawal, returned.Type);
        Assert.Equal("RETURN-ID", returned.ReturnId);
        Assert.Equal("SHARED-TX", returned.TxId);
        Assert.Equal("camt.056.001.08", Assert.Single(await scenario.Messages(new() { OperationType = "RECALL" })).MsgDefIdr);
        Assert.Equal(SIPS.ISO20022.Enums.SupportedMessageTypes.CreditTransferStatusRequest.Id,
            Assert.Single(await scenario.Messages(new() { OperationType = "STATUS_ENQUIRY" })).MsgDefIdr);
    }

    [Theory]
    [InlineData("invalid", null, null)]
    [InlineData(null, "invalid", null)]
    [InlineData(null, null, "invalid")]
    public async Task Invalid_filters_return_bad_request(string? rail, string? operation, string? direction)
    {
        await using var scenario = await Scenario.Create();
        Assert.IsType<BadRequestObjectResult>(await scenario.Controller.GetTransactions(new() { Rail = rail, OperationType = operation, Direction = direction }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await scenario.Controller.GetMessages(new() { Rail = rail, OperationType = operation, Direction = direction }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await scenario.Controller.GetMessages(new() { PageSize = 0 }, CancellationToken.None));
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly PostgresHarness harness;
        private readonly ServiceProvider provider;
        private readonly IServiceScope scope;
        public IStorageBroker Storage { get; }
        public TransactionsController Controller { get; }
        public DateTimeOffset Now { get; } = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
        public int InboundAuditId { get; private set; }
        public int VerificationAuditId { get; private set; }

        private Scenario(PostgresHarness harness)
        {
            this.harness = harness; provider = harness.BuildProvider(); scope = provider.CreateScope();
            Storage = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
            Controller = new(Storage, new XadesOptions { BIC = PostgresHarness.LocalBic }, harness.Options);
        }
        public static async Task<Scenario> Create()
        {
            var scenario = new Scenario(await PostgresHarness.CreateAsync());
            await scenario.Seed(); return scenario;
        }
        public async Task<List<TransactionDto>> Transactions(TransactionQuery query)
            => Assert.IsType<List<TransactionDto>>(Assert.IsType<OkObjectResult>(await Controller.GetTransactions(query, CancellationToken.None)).Value);
        public async Task<List<ISOMessageDto>> Messages(MessageQuery query)
            => Assert.IsType<List<ISOMessageDto>>(Assert.IsType<OkObjectResult>(await Controller.GetMessages(query, CancellationToken.None)).Value);

        private async Task Seed()
        {
            var domestic = Audit("SIPS-PAY", "SHARED-TX", ISOMessageType.TransactionRequest, Now.AddMinutes(-5), "<domestic/>", outbound: true);
            domestic.Transactions.Add(Detail("SHARED-TX", TransactionType.Withdrawal));
            Storage.ISOMessages.Add(domestic);
            Storage.ISOMessages.Add(Audit("SIPS-VERIFY", "SIPS-VERIFY", ISOMessageType.VerificationRequest, Now.AddMinutes(-6), "<domestic-verification/>"));

            var outgoing = PaymentRequestBuilder.Build(new()
            {
                From = PostgresHarness.LocalBic, To = PostgresHarness.ForeignBic, MsgId = "PAY-OUT-MSG", BizMsgIdr = "P-OUT", TxId = "SHARED-TX", EndToEndId = "E2E-OUT",
                Amount = 30m, Currency = "USD", Ustrd = "remittance", LocalInstrument = "USDP", CategoryPurpose = "CASH", CreDt = Now.UtcDateTime,
                Debtor = new() { Name = "Sender", Account = "P-DEBT", AccountType = "ACCT", AgentBIC = PostgresHarness.LocalBic, Issuer = "C" },
                Creditor = new() { Name = "Receiver", Account = "P-CRED", AccountType = "ACCT", AgentBIC = PostgresHarness.ForeignBic, Issuer = "C" }
            }).document;
            Storage.PapssOperations.Add(Operation("P-OUT", outgoing, PapssOperationType.Payment, PapssDirection.Outbound, Now.AddMinutes(-4), "SHARED-TX"));

            var incoming = PostgresHarness.InboundPayment("P-IN", "TX-IN", "E2E-IN");
            var audit = Audit("P-IN", "TX-IN", ISOMessageType.TransactionRequest, Now.AddMinutes(10), incoming);
            audit.BusinessService = harness.Options.SecurityProfile; audit.Status = TransactionStatus.CheckStatus;
            audit.Transactions.Add(Detail("TX-IN", TransactionType.Deposit));
            Storage.ISOMessages.Add(audit); await Storage.SaveChangesAsync(CancellationToken.None); InboundAuditId = audit.Id;
            var inbound = Operation("P-IN", incoming, PapssOperationType.Payment, PapssDirection.Inbound, Now.AddMinutes(-3), "TX-IN");
            inbound.IsoMessageId = audit.Id; Storage.PapssOperations.Add(inbound);

            var verification = Verification("V-OUT", "VERIFY-OUT-MSG", "VERIFY-OUT", PostgresHarness.LocalBic, PostgresHarness.ForeignBic);
            Storage.PapssOperations.Add(Operation("V-OUT", verification, PapssOperationType.Verification, PapssDirection.Outbound, Now.AddMinutes(-2)));
            var enquiry = Verification("V-IN", "VERIFY-IN-MSG", "VERIFY-IN", "PAPSS", PostgresHarness.LocalBic);
            var verificationAudit = Audit("V-IN", "VERIFY-IN-MSG", ISOMessageType.VerificationRequest, Now.AddMinutes(11), enquiry);
            verificationAudit.MsgId = "VERIFY-IN-MSG"; // No BusinessService or IsoMessageId link on this best-effort audit copy.
            Storage.ISOMessages.Add(verificationAudit); await Storage.SaveChangesAsync(CancellationToken.None); VerificationAuditId = verificationAudit.Id;
            var inboundVerification = Operation("V-IN", enquiry, PapssOperationType.VerificationEnquiry, PapssDirection.Inbound, Now.AddMinutes(-1));
            inboundVerification.VerificationId = "VERIFY-IN"; Storage.PapssOperations.Add(inboundVerification);
            await Storage.SaveChangesAsync(CancellationToken.None);
        }
        private static string Verification(string source, string msgId, string verificationId, string from, string to)
            => PayeeVerificationBuilder.Build(new() { From = from, To = to, BizMsgIdr = source, MsgId = msgId, SIPSRequestId = verificationId, MsgDefIdr = "acmt.023.001.03", Alias = "ACCOUNT-VERIFY", Type = "ACCT", CreDt = DateTime.UtcNow }).document;
        private static ISOMessage Audit(string source, string txId, ISOMessageType type, DateTimeOffset date, string xml, bool outbound = false)
            => new() { MessageType = type, Status = TransactionStatus.Pending, MsgId = source, BizMsgIdr = source, TxId = txId, MsgDefIdr = type == ISOMessageType.VerificationRequest ? "acmt.023.001.03" : "pacs.008.001.10",
                Date = date, FromBIC = outbound ? PostgresHarness.LocalBic : "FP", ToBIC = outbound ? PostgresHarness.ForeignBic : PostgresHarness.LocalBic, Message = Encoding.UTF8.GetBytes(xml) };
        private static Transaction Detail(string txId, TransactionType type)
            => new() { Type = type, TxId = txId, EndToEndId = "E2E-" + txId, FromBIC = PostgresHarness.LocalBic, Amount = 10, Currency = "USD", LocalInstrument = "USDP", CategoryPurpose = "CASH",
                DebtorName = "Sender", DebtorAccount = "D", DebtorAccountType = "ACCT", DebtorAgentBIC = PostgresHarness.LocalBic, CreditorName = "Receiver", CreditorAccount = "C", CreditorAccountType = "ACCT", CreditorAgentBIC = PostgresHarness.ForeignBic, RemittanceInformation = "remittance" };
        private static PapssOperation Operation(string id, string xml, PapssOperationType type, PapssDirection direction, DateTimeOffset date, string? txId = null)
            => new() { RequestMessageId = id, Operation = type, Direction = direction, CreatedAt = date, UpdatedAt = date, TxId = txId, SignedRequest = Encoding.UTF8.GetBytes(xml), AccountId = type == PapssOperationType.Payment ? direction == PapssDirection.Outbound ? "P-CRED" : null : "ACCOUNT-VERIFY",
                Amount = type == PapssOperationType.Payment ? 30 : null, Currency = "USD", LocalInstrument = "USDP", GatewayState = PapssGatewayState.Admitted, PapssOutcome = PapssOutcome.Pending, BankDeliveryState = PapssDeliveryState.Pending };
        public async ValueTask DisposeAsync() { scope.Dispose(); await provider.DisposeAsync(); await harness.DisposeAsync(); }
    }
}
