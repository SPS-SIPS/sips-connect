using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using SIPS.Adapter;
using SIPS.Adapter.Models;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.Core.Interfaces;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Verification;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Models.WpSips;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Persistence;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Options;

namespace SIPS.Connect.PostgresTests;

/// <summary>
/// Creates a fresh, migrated PostgreSQL database per test and wires the PAPSS operation store the way
/// SIPS Connect does, with fakes only at the process boundaries (bank HTTP, core bank, WP-SIPS gateway).
/// </summary>
public sealed class PostgresHarness : IAsyncDisposable
{
    public const string EnvironmentVariable = "SIPS_TEST_POSTGRES_CONNECTION";
    public const string LocalBic = "ZKBASOS0";
    public const string ForeignBic = "PHBXSLFR";
    public const string Gateway = "WPSIPSGW";
    public const string CallbackUrl = "https://bank.test/papss/callback";

    private readonly string _adminConnection;
    public string ConnectionString { get; }
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero));
    public FakeBank Bank { get; } = new();
    public FakeGateway Gateway_ { get; } = new();
    public FakeCoreBank CoreBank { get; } = new();
    public PapssFacingOptions Options { get; }

    private PostgresHarness(string adminConnection, string connectionString, PapssFacingOptions options)
    {
        _adminConnection = adminConnection;
        ConnectionString = connectionString;
        Options = options;
    }

    public static async Task<PostgresHarness> CreateAsync(Action<PapssFacingOptions>? configure = null)
    {
        var admin = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(admin))
            throw new InvalidOperationException(
                $"{EnvironmentVariable} is not set. These tests need a real PostgreSQL server, e.g. " +
                $"{EnvironmentVariable}=\"Host=localhost;Database=sips;Username=postgres;Password=test\". " +
                "See docs/PAPSS_OPERATION_STORE_CONFIG.md, section 'Running the PostgreSQL tests'.");
        var database = "sips_t_" + Guid.NewGuid().ToString("N")[..16];
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }
        var options = new PapssFacingOptions
        {
            Enabled = true,
            SecurityProfile = "papss-sec-v1",
            RemoteWpSipsIdentity = Gateway,
            LocalCountry = "SO",
            SendingCurrencies = ["USD"],
            CallbackMappingProfile = "papss-callback-v1",
            CallbackUrl = CallbackUrl
        };
        options.Delivery.InitialBackoffSeconds = 5;
        options.Delivery.MaxBackoffSeconds = 60;
        options.Delivery.MaxAttempts = 5;
        configure?.Invoke(options);
        var harness = new PostgresHarness(admin, new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString, options);
        await using var provider = harness.BuildProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StorageBroker>().Database.MigrateAsync();
        return harness;
    }

    /// <summary>A new "process": its own DI container, DbContexts and connection pool usage.</summary>
    public ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<StorageBroker>(o => o.UseNpgsql(ConnectionString).UseLowerCaseNamingConvention());
        services.AddScoped<IStorageBroker>(sp => sp.GetRequiredService<StorageBroker>());
        services.AddSingleton(Options);
        services.AddSingleton(new XadesOptions { BIC = LocalBic });
        services.AddSingleton<TimeProvider>(Clock);
        services.AddScoped<PapssOperationStore>();
        services.AddSingleton<IPapssOutboxSignal, PapssOutboxSignal>();
        services.AddSingleton<ParticipantCallbackContext>();
        services.AddSingleton<IParticipantCallbackContext>(sp => sp.GetRequiredService<ParticipantCallbackContext>());
        services.AddSingleton<IInboundAuthenticationContext>(sp => sp.GetRequiredService<ParticipantCallbackContext>());
        var mappings = LoadRepositoryMappings();
        services.AddSingleton(mappings);
        services.AddSingleton(sp => new JsonAdapter(mappings, NullLogger<JsonAdapter>.Instance));
        services.AddSingleton<IJsonAdapter>(sp => new ParticipantCallbackJsonAdapter(sp.GetRequiredService<JsonAdapter>(), mappings, sp.GetRequiredService<IParticipantCallbackContext>()));
        services.AddSingleton<ICorrelationService, CorrelationService>();
        services.AddSingleton<ICallbackOrchestrator, CallbackOrchestrator>();
        services.AddSingleton<IInterfaceHttpClient>(Bank);
        services.AddSingleton<ICallbackClient>(sp => new ParticipantCallbackClient(
            new CallbackClient(Bank, NullLogger<CallbackClient>.Instance, sp.GetRequiredService<ICorrelationService>()), sp.GetRequiredService<IParticipantCallbackContext>()));
        services.AddSingleton(new ISO20022Options { Verification = "https://legacy.test/verify", CompletionNotification = "https://legacy.test/complete", Return = "https://legacy.test/return", Key = "key", Secret = "secret" });
        var signer = new Mock<INativeSigner>();
        signer.Setup(x => x.SignEnvelope(It.IsAny<string>(), It.IsAny<string>())).Returns<string, string>((xml, _) => xml);
        services.AddSingleton(signer.Object);
        services.AddSingleton(Mock.Of<ISignatureService>());
        services.AddScoped<IVerificationResultInbox, PapssVerificationResultInbox>();
        services.AddScoped(sp => new IncomingVerificationResponseHandler(
            sp.GetRequiredService<ISO20022Options>(), NullLogger<IncomingVerificationResponseHandler>.Instance,
            sp.GetRequiredService<INativeSigner>(), sp.GetRequiredService<IJsonAdapter>(), sp.GetRequiredService<ISignatureService>(),
            sp.GetRequiredService<ICorrelationService>(), sp.GetRequiredService<ICallbackClient>(), sp.GetRequiredService<ICallbackOrchestrator>(),
            sp.GetRequiredService<IInboundAuthenticationContext>(), sp.GetRequiredService<IVerificationResultInbox>()));
        services.AddScoped<IIncomingVerificationResponseHandler>(sp => sp.GetRequiredService<IncomingVerificationResponseHandler>());
        services.AddScoped<IVerificationResultDelivery>(sp => sp.GetRequiredService<IncomingVerificationResponseHandler>());
        services.AddSingleton<IPapssFacingSipsClient>(Gateway_);
        services.AddSingleton<ICoreBankVerificationClient>(CoreBank);
        services.AddSingleton(Mock.Of<IISOMessageService>());
        services.AddScoped<IPapssInboundVerificationService, PapssInboundVerificationService>();
        services.AddScoped<IPapssPaymentService, PapssPaymentService>();
        services.AddScoped<IPapssPaymentCallbackService, PapssPaymentCallbackService>();
        services.AddScoped<PapssPaymentEventDelivery>();
        services.AddSingleton<PapssBankPushWorker>();
        services.AddSingleton<PapssResponseOutboxWorker>();
        services.AddSingleton<PapssStoreRetentionWorker>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public PapssParticipantBinding Binding() => new(LocalBic, "SO", ["USD"], Options.CallbackMappingProfile, Options.CallbackUrl);

    public async Task<T> WithStorageAsync<T>(Func<IStorageBroker, Task<T>> action)
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IStorageBroker>());
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        var database = new NpgsqlConnectionStringBuilder(ConnectionString).Database;
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private static JsonAdapterOptions LoadRepositoryMappings()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "jsonAdapter.json")))
            current = current.Parent;
        var path = Path.Combine(current?.FullName ?? throw new DirectoryNotFoundException("Could not locate the SIPS repository root."), "jsonAdapter.json");
        return JsonSerializer.Deserialize<JsonAdapterOptions>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    // ---------------------------------------------------------------------------------------------
    // Messages
    // ---------------------------------------------------------------------------------------------

    /// <summary>acmt.024 result as the gateway delivers it for our outbound verification.</summary>
    public static string VerificationResult(string requestMessageId, bool verified, string? bizMsgIdr = null)
    {
        var xml = PayeeVerificationResponseBuilder.Build(new PayeeVerificationResponseBuilder.Request
        {
            From = ForeignBic,
            To = LocalBic,
            CreDt = DateTime.UtcNow,
            Original = new PayeeVerificationBuilder.Request
            {
                From = LocalBic, To = ForeignBic, BizMsgIdr = requestMessageId, MsgDefIdr = "acmt.023.001.03",
                MsgId = requestMessageId, CreDt = DateTime.UtcNow.AddSeconds(-5), Alias = "0012030321735", Type = "BBAN",
                SIPSRequestId = requestMessageId
            },
            Verified = verified,
            VerificationId = requestMessageId,
            Id = "0012030321735",
            Type = "BBAN",
            Name = verified ? "FORTRESS GLOBAL" : string.Empty,
            Currency = verified ? "SLE" : string.Empty,
            Reason = verified ? "MATCH" : "MISS"
        });
        return bizMsgIdr is null ? xml : SetHeader(xml, "BizMsgIdr", bizMsgIdr);
    }

    /// <summary>Inbound acmt.023 as the gateway delivers it: BizMsgIdr = MsgId = P_MSG, Vrfctn/Id = P_VID.</summary>
    public static string InboundEnquiry(string sourceMessageId, string verificationId, DateTime? created = null)
    {
        var xml = PayeeVerificationBuilder.Build(new PayeeVerificationBuilder.Request
        {
            From = ForeignBic, To = LocalBic, Alias = "0012030321735", Type = "BBAN", MsgId = sourceMessageId, SIPSRequestId = verificationId
        }).document;
        xml = SetHeader(xml, "BizMsgIdr", sourceMessageId);
        return created is null ? xml : SetHeader(xml, "CreDt", created.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }

    /// <summary>
    /// pacs.002.001.12 exactly as the gateway contract sends it: AppHdr BizMsgIdr = PAPSS source message id,
    /// OrgnlMsgId = the bank's SIPS MsgId, OrgnlMsgNmId = pacs.008.001.10 / pacs.004.001.11, OrgnlTxId = payment TxId.
    /// </summary>
    public static string StatusReport(string sourceMessageId, string originalMessageId, string originalMessageType, string txId, string endToEndId, string status, string? reason = null, decimal amount = 10m)
    {
        var xml = PaymentRequestResponseBuilder.Build(new PaymentRequestResponseBuilder.Response
        {
            From = Gateway,
            To = LocalBic,
            Original = new PaymentRequestBuilder.Request
            {
                From = LocalBic, To = ForeignBic, BizMsgIdr = originalMessageId, MsgDefIdr = originalMessageType, MsgId = originalMessageId,
                CreDt = DateTime.UtcNow.AddMinutes(-1), EndToEndId = endToEndId, TxId = txId, Amount = amount, Currency = "USD"
            },
            Status = status,
            Reason = reason,
            AdditionalInfo = reason is null ? null : "reported by PAPSS"
        });
        return SetHeader(xml, "BizMsgIdr", sourceMessageId);
    }

    /// <summary>Inbound pacs.004.001.11 (a return of a payment we sent).</summary>
    public static string InboundReturn(string sourceMessageId, string returnId, string originalTxId, string originalEndToEndId, decimal amount = 10m)
        => SetHeader(ReturnPaymentRequestBuilder.Build(new()
        {
            From = Gateway, To = LocalBic, CreDt = DateTime.UtcNow, NumberOfTransactions = 1, LocalInstrument = "USDP", CategoryPurpose = "CASH",
            ReturnId = returnId, OrgnlTxId = originalTxId, OriginalEndToEnd = originalEndToEndId, OriginalCurrency = "USD", OriginalAmount = amount,
            ReturnReason = "FOCR", AdditionalInfo = "returned by beneficiary bank", DebtorAgent = ForeignBic, CreditorAgent = LocalBic
        }).document, "BizMsgIdr", sourceMessageId);

    /// <summary>Inbound pacs.008.001.10 (a credit transfer received from PAPSS).</summary>
    public static string InboundPayment(string sourceMessageId, string txId, string endToEndId, decimal amount = 25m)
        => SetHeader(PaymentRequestBuilder.Build(new()
        {
            From = Gateway, To = LocalBic, LocalInstrument = "USDP", CategoryPurpose = "CASH", EndToEndId = endToEndId, TxId = txId, Amount = amount, Currency = "USD", Ustrd = "invoice 7",
            Debtor = new() { Name = "AMINA ALI", Account = "100200", AccountType = "BBAN", AgentBIC = ForeignBic, Issuer = "C" },
            Creditor = new() { Name = "FORTRESS GLOBAL", Account = "0012030321735", AccountType = "BBAN", AgentBIC = LocalBic, Issuer = "C" }
        }).document, "BizMsgIdr", sourceMessageId);

    /// <summary>The bank decision the legacy pacs.008 handler stores in isomessages.Response.</summary>
    public static string Decision(string txId, string endToEndId, string status, string? reason = null)
        => PaymentRequestResponseBuilder.Build(new PaymentRequestResponseBuilder.Response
        {
            From = LocalBic, To = Gateway,
            Original = new PaymentRequestBuilder.Request { From = Gateway, To = LocalBic, BizMsgIdr = "CT02-SRC", MsgDefIdr = "pacs.008.001.10", MsgId = "CT02-MSG", CreDt = DateTime.UtcNow, EndToEndId = endToEndId, TxId = txId, Amount = 25m, Currency = "USD" },
            Status = status, Reason = reason
        });

    public static string SetHeader(string xml, string element, string value)
    {
        var document = XDocument.Parse(xml);
        document.Descendants().First(x => x.Name.LocalName == "AppHdr").Elements().First(x => x.Name.LocalName == element).Value = value;
        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static string Header(string xml, string element)
        => XDocument.Parse(xml).Descendants().First(x => x.Name.LocalName == "AppHdr").Elements().First(x => x.Name.LocalName == element).Value;
}

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>The participant bank callback endpoint.</summary>
public sealed class FakeBank : IInterfaceHttpClient
{
    private readonly object _gate = new();
    public List<(string Url, Dictionary<string, string>? Headers, JsonObject Body)> Calls { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public async Task<Response<JsonObject?>> Send(string url, Dictionary<string, string>? headers, StringContent content, CancellationToken ct)
    {
        var body = JsonNode.Parse(await content.ReadAsStringAsync(ct))!.AsObject();
        lock (_gate) Calls.Add((url, headers is null ? null : new(headers), body));
        return new Response<JsonObject?>(new JsonObject()) { StatusCode = Status, IsSuccess = Status == HttpStatusCode.OK };
    }

    public int Count { get { lock (_gate) return Calls.Count; } }
    public Task<Response<string>> Send4XML(string url, StringContent content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public void AddAuthHeaders(string accessToken) { }
}

/// <summary>The core bank verification endpoint (inbound acmt.023).</summary>
public sealed class FakeCoreBank : ICoreBankVerificationClient
{
    private int _calls;
    public int Calls => _calls;
    public bool Answer { get; set; } = true;
    public bool Verified { get; set; } = true;
    public string BankReason { get; set; } = "MATCH";
    public TimeSpan Delay { get; set; }

    public async Task<CoreBankVerificationResult> VerifyAsync(PayeeVerificationBuilder.Request request, string correlationId, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        var response = CoreBankVerificationClient.InitialResponse(request);
        if (!Answer) return new(false, response, HttpStatusCode.RequestTimeout, "CORE_BANK_TIMEOUT");
        response.Verified = Verified;
        response.Id = request.Alias;
        response.Type = "BBAN";
        response.Name = Verified ? "AMINA ALI" : string.Empty;
        response.Currency = Verified ? "USD" : string.Empty;
        response.Reason = string.IsNullOrEmpty(BankReason) ? (Verified ? "SUCC" : "MISS") : BankReason;
        return new(true, response, HttpStatusCode.OK, null, BankReason);
    }
}

/// <summary>The WP-SIPS gateway ingress (/sips/messages).</summary>
public sealed class FakeGateway : IPapssFacingSipsClient
{
    private readonly object _gate = new();
    public List<string> Submitted { get; } = [];
    /// <summary>(call number starting at 1, signed xml) -> admission code, or throw.</summary>
    public Func<int, string, string> OnSubmit { get; set; } = (_, _) => "RECEIVED_AND_DURABLY_ADMITTED";

    public PapssSignedMessage PrepareVerification(PapssParticipantBinding participant, VerificationRequestDto request)
    {
        var id = request.MsgId ?? PapssFacingSipsClient.Id();
        var unsigned = PayeeVerificationBuilder.Build(new() { From = participant.Bic, To = request.ToBIC, Alias = request.Alias, Type = request.Type, MsgId = id, SIPSRequestId = id }).document;
        return new(id, SignForSubmission(unsigned, id), DateTimeOffset.UtcNow);
    }

    public string SignForSubmission(string unsignedXml, string businessMessageId)
    {
        var xml = PostgresHarness.SetHeader(unsignedXml, "BizMsgIdr", businessMessageId);
        var document = XDocument.Parse(xml);
        document.Descendants().First(x => x.Name.LocalName == "AppHdr").Elements().First(x => x.Name.LocalName == "To").Descendants().First(x => x.Name.LocalName == "Id").Value = PostgresHarness.Gateway;
        return document.ToString(SaveOptions.DisableFormatting) + "<!--signed-->";
    }

    public Task<PapssAdmissionResponse> SubmitSignedAsync(PapssParticipantBinding participant, string signedXml, CancellationToken ct)
    {
        int call;
        lock (_gate) { Submitted.Add(signedXml); call = Submitted.Count; }
        var code = OnSubmit(call, signedXml);
        return Task.FromResult(new PapssAdmissionResponse(PostgresHarness.Header(signedXml.Replace("<!--signed-->", string.Empty), "BizMsgIdr"), code, true));
    }

    public int Count { get { lock (_gate) return Submitted.Count; } }
    public int Prepared;

    public Task<PapssSignedMessage> PreparePaymentAsync(PapssParticipantBinding participant, PaymentRequestDto request, CancellationToken ct)
    {
        Interlocked.Increment(ref Prepared);
        var built = PaymentRequestBuilder.Build(new()
        {
            From = participant.Bic, To = request.ToBIC, LocalInstrument = request.LocalInstrument, CategoryPurpose = request.CategoryPurpose, EndToEndId = request.EndToEndId,
            TxId = request.TxId!, Amount = request.Amount, Currency = request.Currency, Ustrd = request.RemittanceInformation,
            Debtor = new() { Name = request.DebtorName, Account = request.DebtorAccount, AccountType = request.DebtorAccountType, AgentBIC = participant.Bic, Issuer = "C" },
            Creditor = new() { Name = request.CreditorName, Account = request.CreditorAccount, AccountType = request.CreditorAccountType, AgentBIC = request.ToBIC, Issuer = "C" }
        });
        return Task.FromResult(new PapssSignedMessage(built.bizMsgIdr, SignForSubmission(built.document, built.bizMsgIdr), DateTimeOffset.UtcNow, built.msgId));
    }

    public PapssSignedMessage PrepareReturn(PapssParticipantBinding participant, ReturnPaymentRequestDto request)
    {
        Interlocked.Increment(ref Prepared);
        var built = ReturnPaymentRequestBuilder.Build(new()
        {
            From = participant.Bic, To = request.ToBIC, CreDt = DateTime.UtcNow, NumberOfTransactions = 1, LocalInstrument = request.LocalInstrument, CategoryPurpose = request.CategoryPurpose,
            ReturnId = request.ReturnId, OrgnlTxId = request.OriginalTxId, OriginalEndToEnd = request.OriginalEndToEndId, OriginalCurrency = request.OriginalCurrency,
            OriginalAmount = request.OriginalAmount, ReturnReason = request.Reason, AdditionalInfo = request.AdditionalInfo, DebtorAgent = participant.Bic, CreditorAgent = request.ToBIC
        });
        return new(built.bizMsgIdr, SignForSubmission(built.document, built.bizMsgIdr), DateTimeOffset.UtcNow, built.msgId);
    }

    public PapssSignedMessage PrepareStatus(PapssParticipantBinding participant, StatusRequestDto request)
    {
        Interlocked.Increment(ref Prepared);
        var msgId = PapssFacingSipsClient.Id();
        var unsigned = PaymentStatusRequestBuilder.Build(new() { From = participant.Bic, To = request.ToBIC, MsgId = msgId, CreDt = DateTime.UtcNow, OrgnlTxId = request.TxId, OriginalEndToEnd = request.EndToEnd });
        var bizMsgIdr = PostgresHarness.Header(unsigned, "BizMsgIdr");
        return new(bizMsgIdr, SignForSubmission(unsigned, bizMsgIdr), DateTimeOffset.UtcNow, msgId);
    }

    public Task<PapssAdmissionResponse> VerifyAsync(PapssParticipantBinding participant, VerificationRequestDto request, CancellationToken ct) => throw new NotSupportedException();
    public Task<PapssAdmissionResponse> PayAsync(PapssParticipantBinding participant, PaymentRequestDto request, CancellationToken ct) => throw new NotSupportedException();
    public Task<PapssAdmissionResponse> SubmitPaymentDecisionAsync(PapssParticipantBinding participant, string signedPacs002, CancellationToken ct) => throw new NotSupportedException();
    public Task<PapssAdmissionResponse> GetStatusAsync(PapssParticipantBinding participant, StatusRequestDto request, CancellationToken ct) => throw new NotSupportedException();
    public Task<PapssAdmissionResponse> ReturnAsync(PapssParticipantBinding participant, ReturnPaymentRequestDto request, CancellationToken ct) => throw new NotSupportedException();
    public Task<ReadinessResponse> GetReadinessAsync(PapssParticipantBinding participant, ReadinessRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<ParticipantDiscoveryResponse> DiscoverAsync(PapssParticipantBinding participant, ParticipantDiscoveryRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<FxRateResponse> GetFxAsync(PapssParticipantBinding participant, FxRateRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<PositionResponse> GetPositionsAsync(PapssParticipantBinding participant, PositionRequest request, CancellationToken ct) => throw new NotSupportedException();
}
