using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIPS.Adapter;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Services;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Correlation;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Implementations;
using SIPS.PostgreSQL.Enums;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
namespace SIPS.Core.Services;
public sealed class IncomingTransactionHandler(
    ISO20022Options options,
    ILogger<IncomingTransactionHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IJsonAdapter jsonAdapter,
    IIncomingRecorder record,
    ISignatureService signature,
    IPaymentRequestParser parser,
    ICallbackClient callback,
    IResponseFactory responseFactory,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    IInboundMessageService inbound,
    ICallbackOrchestrator callbacks,
    IISOMessageService isoService,
    IOptions<CoreOptions> coreOptions
    ) : IIncomingTransactionHandler
{
    private readonly ISO20022Options _callbackLinks = options;
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ILogger<IncomingTransactionHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly INativeVerifier _verifier = verifier;
    private readonly IJsonAdapter _jsonAdapter = jsonAdapter;
    private readonly IIncomingRecorder _record = record;
    private readonly ISignatureService _signature = signature;
    private readonly IPaymentRequestParser _parser = parser;
    private readonly ICallbackClient _callback = callback;
    private readonly IResponseFactory _responses = responseFactory;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly IInboundMessageService _inbound = inbound;
    private readonly ICallbackOrchestrator _callbacks = callbacks;
    private readonly IISOMessageService _isoService = isoService;
    private readonly CoreOptions _core = coreOptions.Value;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Compatibility constructor for tests and existing code paths
    public IncomingTransactionHandler(
        ISO20022Options options,
        ILogger<IncomingTransactionHandler> logger,
        IInterfaceHttpClient httpClient,
        INativeSigner signer,
        INativeVerifier verifier,
        IJsonAdapter jsonAdapter,
        IIncomingRecorder record,
        ISignatureService signature,
        IPaymentRequestParser parser,
        ICallbackClient callback,
        IResponseFactory responseFactory,
        IPersistenceGateway persistence,
        ICorrelationService correlation,
        IOptions<CoreOptions> coreOptions)
        : this(options, logger, httpClient, signer, verifier, jsonAdapter, record, signature, parser, callback, responseFactory, persistence, correlation,
              new InboundMessageService(signature),
              new CallbackOrchestrator(),
              new ISOMessageService(persistence),
              coreOptions)
    {
    }
    public async Task<string> HandleAsync(string message, CancellationToken ct)
    {
        // correlation id
        string cid = _correlation.Create();

        // Step 1: Verify signature and parse message via helper
        var (isValid, request) = await _inbound.VerifyAndParseAsync(
            message,
            (xml) =>
            {
                if (!_parser.TryParse(xml, out var req)) return (false, (PaymentRequestBuilder.Request?)null);
                return (true, req);
            },
            ct,
            cid);
        if (!isValid || request == null)
            return AdminMessage.Generate("Failed to verify the signature or parse the message.");
        // Step 2: Record the incoming ISO message via service
        var record = await _isoService.RecordIncomingTransactionAsync(request, message, ct);

        // Step 3: Prepare response object
        var response = _responses.BuildPaymentInitial(request);

        try
        {
            using var dbCts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            var dbCt = dbCts.Token;
            // Step 4: Immediately acknowledge with ACSC to the sender; CoreBank processing will occur upon status report
            response.Status = ACSC;
            response.Reason = null;
            response.AdditionalInfo = null;

            // Step 5: Build, persist initial ACK response and forward to CoreBank
            var rsp = PaymentRequestResponseBuilder.Build(response);
            await _isoService.PersistTransactionResponseAsync(record,
                TransactionStatus.Pending,
                "Transaction Is Pending For Approval",
                null,
                rsp,
                request.TxId ?? string.Empty,
                request.EndToEndId ?? string.Empty,
                dbCt);

            // Attempt to forward the transaction to CoreBank via orchestrator and persist final status
            try
            {
                var transaction = record.Transactions.FirstOrDefault();
                if (transaction != null)
                {
                    var headers = new Dictionary<string, string>() {
                        { API_Key, _callbackLinks.Key! },
                        { API_Secret, _callbackLinks.Secret! }
                    };
                    var idem = transaction?.TxId ?? request.TxId;
                    if (!string.IsNullOrWhiteSpace(idem))
                        headers["X-Idempotency-Key"] = idem!;
                    if (!string.IsNullOrWhiteSpace(transaction?.TxId ?? request.TxId))
                        headers["X-Transaction-Id"] = (transaction?.TxId ?? request.TxId)!;

                    var dto = new CBPaymentRequestDto
                    {
                        FromBIC = transaction.FromBIC ?? string.Empty,
                        LocalInstrument = transaction.LocalInstrument ?? string.Empty,
                        CategoryPurpose = transaction.CategoryPurpose ?? string.Empty,
                        EndToEndId = transaction.EndToEndId ?? string.Empty,
                        TxId = transaction.TxId ?? string.Empty,
                        Amount = transaction.Amount,
                        Currency = transaction.Currency ?? string.Empty,
                        DebtorName = transaction.DebtorName ?? string.Empty,
                        DebtorAccount = transaction.DebtorAccount ?? string.Empty,
                        DebtorAccountType = transaction.DebtorAccountType ?? string.Empty,
                        DebtorAgentBIC = transaction.DebtorAgentBIC ?? string.Empty,
                        DebtorIssuer = transaction.DebtorIssuer ?? string.Empty,
                        CreditorName = transaction.CreditorName ?? string.Empty,
                        CreditorAccount = transaction.CreditorAccount ?? string.Empty,
                        CreditorAccountType = transaction.CreditorAccountType ?? string.Empty,
                        CreditorAgentBIC = transaction.CreditorAgentBIC ?? string.Empty,
                        CreditorIssuer = transaction.CreditorIssuer ?? string.Empty,
                        RemittanceInformation = transaction.RemittanceInformation ?? string.Empty,
                        Date = DateTime.UtcNow,
                        ToBIC = record.FromBIC ?? string.Empty,
                        SettlementMethod = "CLRG",
                        ChargeBearer = "SLEV",
                        BizMsgIdr = record.BizMsgIdr ?? string.Empty,
                        MsgDefIdr = record.MsgDefIdr ?? string.Empty,
                        ClearingSystem = string.Empty,
                        MsgId = record.MsgId ?? string.Empty
                    };

                    var result = await _callbacks.SendJsonAsync(
                        _callbackLinks.Transfer!,
                        headers,
                        dto,
                        CB_PaymentRequest,
                        _jsonAdapter,
                        _correlation,
                        _jsonSerializerOptions,
                        _callback,
                        ct,
                        cid
                    );

                        var crResponse = ParseCallbackResult(result.Data!);
                    var cbProcessed = crResponse.Status == ACSC;

                    record.Status = cbProcessed ? TransactionStatus.Success : TransactionStatus.ReadyForReturn;
                    record.Reason = cbProcessed ? "Processed Transaction" : "Transaction is ready for return";
                    record.AdditionalInfo = cbProcessed ? "Processed" : "Queued For Return!";
                    record.CoreBankResponse = JsonSerializer.Serialize(result, _jsonSerializerOptions);

                    // Persist final status to the parent message
                    await _isoService.PersistTransactionResponseAsync(record,
                        cbProcessed ? TransactionStatus.Success : TransactionStatus.Failed,
                        record.Reason,
                        record.AdditionalInfo,
                        rsp,
                        request.TxId ?? string.Empty,
                        request.EndToEndId ?? string.Empty,
                        dbCt);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{CorrelationId}] Failed to forward transaction to CoreBank for TxId {TxId}", cid, request.TxId);
                // best-effort: mark as failed
                record.Status = TransactionStatus.Failed;
                record.Reason = "Failed to forward to CoreBank";
                await _isoService.PersistTransactionResponseAsync(record, TransactionStatus.Failed, record.Reason, null, rsp, request.TxId ?? string.Empty, request.EndToEndId ?? string.Empty, dbCt);
            }

            return _signer.SignEnvelope(rsp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] INCOMING PS Handler Exception for TxId {TxId}", cid, request?.TxId);
            response.AdditionalInfo = "Failed to process Transaction";
            var rsp = PaymentRequestResponseBuilder.Build(response);
            using var dbCts2 = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            await _isoService.PersistTransactionResponseAsync(record,
                TransactionStatus.Failed,
                "Failed to process Transaction",
                response.AdditionalInfo,
                rsp,
                request?.TxId ?? string.Empty,
                request?.EndToEndId ?? string.Empty,
                dbCts2.Token);
            return _signer.SignEnvelope(rsp);
        }
    }

    // convert callback result to payment response DTO (mirrors logic in PaymentStatus handler)
    private PaymentResponseDto ParseCallbackResult(JsonObject data)
    {
        // convert the responseContent to a JsonObject
        var js = JsonSerializer.Deserialize<JsonObject>(data, _jsonSerializerOptions);
        var md = _jsonAdapter.Transform(js!, "CB_PaymentResponse");
        var deserializedContent = _jsonAdapter.ToObject<PaymentResponseDto>(md);

        return deserializedContent;
    }
}