using System.Text;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using Microsoft.Extensions.Logging;
using SIPS.PostgreSQL.Models;
using System.Text.Json;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Correlation;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
namespace SIPS.Core.Services;
public sealed class OutgoingReturnTransactionHandler(
    ISO20022Options options,
    ILogger<OutgoingReturnTransactionHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IIncomingRecorder record,
    ISignatureService signature,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    SIPS.Core.Services.Abstractions.IISOMessageService isoService,
    SIPS.Core.Services.Abstractions.IStatusOrchestrator statusOrchestrator,
    IOptions<CoreOptions> coreOptions
    ) : IOutgoingReturnTransactionHandler
{
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ISO20022Options _configuration = options;
    private readonly ILogger<OutgoingReturnTransactionHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly INativeVerifier _verifier = verifier;
    private readonly IIncomingRecorder _record = record;
    private readonly ISignatureService _signature = signature;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly SIPS.Core.Services.Abstractions.IISOMessageService _isoService = isoService;
    private readonly SIPS.Core.Services.Abstractions.IStatusOrchestrator _statusOrchestrator = statusOrchestrator;
    private readonly CoreOptions _core = coreOptions.Value;
    public async Task<Response<ReturnPaymentResponseDto>> HandleAsync(ReturnPaymentRequestDto message, CancellationToken ct)
    {
        var url = _configuration.SIPS ?? throw new InvalidOperationException("SIPS not found in configuration.");
        var fromBIC = _configuration.BIC ?? throw new InvalidOperationException("BIC not found in configuration.");
        var txId = Transformers.GenerateId(_configuration.BIC!);
        var cid = _correlation.Create(txId);

        try
        {
            using var dbCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            var dbCt = dbCts.Token;
            // Step 1: Retrieve original message and transaction
            var (isValid, originalMessage, transaction) = await GetOriginalTransactionAsync(message.OriginalTxId, dbCt);
            if (!isValid || originalMessage == null || transaction == null)
            {
                _logger.LogWarning("[{CorrelationId}] Return rejected: Original transaction {TxId} not found", cid, message.OriginalTxId);
                return Response<ReturnPaymentResponseDto>.Fail("Transaction not found: " + message.OriginalTxId, System.Net.HttpStatusCode.NotFound);
            }

            // Step 2: Validate original transaction was successfully completed (ACSC)
            // Only allow returns for transactions that were accepted and settled
            if (originalMessage.Status != PostgreSQL.Enums.TransactionStatus.Success &&
                originalMessage.Status != PostgreSQL.Enums.TransactionStatus.ReadyForReturn)
            {
                _logger.LogWarning("[{CorrelationId}] Return rejected: Original transaction {TxId} status is {Status}, not Success or ReadyForReturn",
                    cid, message.OriginalTxId, originalMessage.Status);
                return Response<ReturnPaymentResponseDto>.Fail(
                    $"Cannot return transaction with status {originalMessage.Status}. Only successful transactions can be returned.",
                    System.Net.HttpStatusCode.BadRequest);
            }

            // Step 3: Validate return request fields against original transaction
            // Validate OriginalEndToEndId matches the transaction's EndToEndId
            if (!string.IsNullOrWhiteSpace(message.OriginalEndToEndId) &&
                !string.Equals(message.OriginalEndToEndId, transaction.EndToEndId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[{CorrelationId}] Return rejected: OriginalEndToEndId mismatch for {TxId}. Return={ReturnEndToEnd}, Original={OriginalEndToEnd}",
                    cid, message.OriginalTxId, message.OriginalEndToEndId, transaction.EndToEndId);
                return Response<ReturnPaymentResponseDto>.Fail(
                    $"OriginalEndToEndId mismatch: return={message.OriginalEndToEndId}, original={transaction.EndToEndId}",
                    System.Net.HttpStatusCode.BadRequest);
            }

            // Step 4: Build, sign, and persist outgoing return request
            originalMessage.ReturnId = message.ReturnId;
            var (document, bizMsgIdr, type, msgId) = BuildRequest(transaction, fromBIC, message.ReturnId, reason: message.Reason, additionalInfo: message.AdditionalInfo);
            var signed = _signer.SignEnvelope(document);
            var entity = CreateISOMessage(message, transaction, fromBIC, txId, signed, msgId, type, bizMsgIdr);
            var record = await _persistence.RecordISOMessageAsync(entity, dbCt);

            // Step 5: Call IPS and handle response
            var responseMessage = await CallSIPSAsync(url, signed, ct, cid);
            var responseMessageStatus = await HandleSIPSCallExceptionAsync(record, originalMessage, responseMessage, dbCt, cid);
            if (!responseMessageStatus.IsSuccess)
                return responseMessageStatus;

            // Step 6: Parse and persist IPS response
            _logger.LogInformation("[{CorrelationId}] Received response from IPS: {Response}", cid, responseMessage.Data);
            if (!TryParse(responseMessage.Data!, out var rs) || rs == null)
            {
                _logger.LogError("[{CorrelationId}] Failed to parse IPS return response: {message}", cid, responseMessage.Data);
                await _isoService.MarkForCheckStatusAsync(originalMessage, "Failed to parse IPS return response", dbCt);
                return Response<ReturnPaymentResponseDto>.Fail("Failed to parse the message.", System.Net.HttpStatusCode.BadRequest);
            }
            _logger.LogDebug("[{CorrelationId}] Parsed response from IPS: {Response}", cid, JsonSerializer.Serialize(rs, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            // Use StatusOrchestrator to map IPS status code consistently
            var finalStatus = _statusOrchestrator.MapSingleStatus(rs.Status ?? RJCT, "IPS");

            // Persist return message with mapped status
            record.Response = Encoding.UTF8.GetBytes(responseMessage.Data!);
            record.Status = finalStatus;
            record.Reason = rs.Reason ?? MISS;
            record.AdditionalInfo = rs.AdditionalInfo ?? string.Empty;
            record.TxId = rs.TxId ?? string.Empty;
            record.EndToEndId = rs.Original?.OriginalEndToEnd ?? string.Empty;

            await _persistence.ISOMessageResponseAsync(record, dbCt);

            // Update original transaction to reflect return status
            if (finalStatus == PostgreSQL.Enums.TransactionStatus.Success)
            {
                // IPS accepted the return (ACSC) - update original transaction
                originalMessage.Status = PostgreSQL.Enums.TransactionStatus.Success;
                originalMessage.Reason = "Return completed successfully";
                originalMessage.AdditionalInfo = $"Return sent with ReturnId: {message.ReturnId}. IPS confirmed with ACSC.";
                _logger.LogInformation("[{CorrelationId}] Outgoing return completed for TxId {TxId} with ReturnId {ReturnId}",
                    cid, message.OriginalTxId, message.ReturnId);
            }
            else
            {
                // IPS rejected the return (RJCT) - update original transaction
                originalMessage.Reason = "Outgoing return rejected by IPS";
                originalMessage.AdditionalInfo = $"Return attempt with ReturnId: {message.ReturnId} was rejected. Reason: {rs.Reason ?? "Unknown"}";
                _logger.LogWarning("[{CorrelationId}] Outgoing return rejected for TxId {TxId} with ReturnId {ReturnId}. Reason: {Reason}",
                    cid, message.OriginalTxId, message.ReturnId, rs.Reason);
            }

            await _persistence.ISOMessageResponseAsync(originalMessage, dbCt);

            // Step 7: Return success response
            return Response<ReturnPaymentResponseDto>.Success(new ReturnPaymentResponseDto
            {
                Status = rs.Status ?? RJCT,
                TxId = rs.TxId ?? string.Empty,
                EndToEndId = rs.Original?.OriginalEndToEnd ?? string.Empty,
                Reason = rs.Reason ?? string.Empty,
                AdditionalInfo = rs.AdditionalInfo ?? string.Empty,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("[{CorrelationId}] Failed to Send Request To SIPS Error: {Error}", cid, ex);
            return Response<ReturnPaymentResponseDto>.Fail("Failed to Send Request To SIPS", System.Net.HttpStatusCode.InternalServerError);
        }
    }

    private async Task<(bool isValid, ISOMessage? originalMessage, Transaction? transaction)> GetOriginalTransactionAsync(string originalTxId, CancellationToken ct)
    {
        var originalMessage = await _persistence.GetISOMessageWithTransactionsByTxIdAsync(originalTxId, ct);
        if (originalMessage == null || originalMessage.Transactions.Count == 0)
            return (false, null, null);
        var transaction = originalMessage.Transactions.FirstOrDefault();
        if (transaction == null)
            return (false, originalMessage, null);
        return (true, originalMessage, transaction);
    }
    private static (string document, string bizMsgIdr, string type, string msgId) BuildRequest(Transaction transaction, string fromBIC, string returnId, string reason, string additionalInfo)
    {
        return ReturnPaymentRequestBuilder.Build(new ReturnPaymentRequestBuilder.Request
        {
            From = fromBIC,
            To = transaction.FromBIC,
            CreDt = DateTime.UtcNow,
            LocalInstrument = transaction.LocalInstrument,
            CategoryPurpose = transaction.CategoryPurpose,
            OriginalEndToEnd = transaction.EndToEndId,
            OrgnlTxId = transaction.TxId,
            OriginalAmount = transaction.Amount,
            OriginalCurrency = transaction.Currency,
            SettlementMethod = ISO20022.Schemas.RPDocument.SettlementMethod1Code.CLRG,
            NumberOfTransactions = 1,
            ReturnId = returnId,
            ReturnReason = reason,
            AdditionalInfo = additionalInfo
        });
    }
    private static ISOMessage CreateISOMessage(ReturnPaymentRequestDto message, Transaction transaction, string fromBIC, string txId, string signedMessage, string msgId, string msgDefIdr, string bizMsgIdr)
    {
        var entity = new ISOMessage
        {
            MessageType = PostgreSQL.Enums.ISOMessageType.ReturnRequest,
            Date = DateTimeOffset.Now.ToUniversalTime(),
            FromBIC = fromBIC,
            ToBIC = transaction.FromBIC,
            Message = Encoding.UTF8.GetBytes(signedMessage),
            BizMsgIdr = bizMsgIdr,
            MsgDefIdr = msgDefIdr,
            MsgId = msgId
        };
        entity.Transactions.Add(new Transaction
        {
            Type = PostgreSQL.Enums.TransactionType.ReturnDeposit,
            FromBIC = fromBIC,
            LocalInstrument = transaction.LocalInstrument,
            CategoryPurpose = transaction.CategoryPurpose,
            EndToEndId = transaction.EndToEndId,
            TxId = txId,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            RemittanceInformation = message.Reason + " " + message.AdditionalInfo,
            DebtorAccount = string.Empty,
            CreditorAccount = string.Empty,
            DebtorAccountType = string.Empty,
            DebtorAgentBIC = string.Empty,
            DebtorIssuer = string.Empty,
            DebtorName = string.Empty,
            CreditorAccountType = string.Empty,
            CreditorAgentBIC = string.Empty,
            CreditorIssuer = string.Empty,
            CreditorName = string.Empty,
        });

        return entity;
    }
    private async Task<Response<string>> CallSIPSAsync(string url, string signed, CancellationToken ct, string cid)
    {
        var content = new StringContent(signed, Encoding.UTF8, "application/xml");
        // Log the callback URL and payload
        _logger.LogInformation("[{CorrelationId}] Callback URL: {Url}", cid, url);
        _logger.LogInformation("[{CorrelationId}] Callback Payload: {Payload}", cid, signed);
        return await _httpClient.Send4XML(url, content, ct);
    }
    private async Task<Response<ReturnPaymentResponseDto>> HandleSIPSCallExceptionAsync(
    PostgreSQL.Models.ISOMessage record,
    PostgreSQL.Models.ISOMessage originalMessage,
    Response<string>? responseMessage,
    CancellationToken ct,
    string correlationId)
    {
        // Handle timeout, bad gateway, or connection errors FIRST - mark for SAF retry
        if (responseMessage == null ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.BadGateway ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.InternalServerError)
        {
            var statusDescription = responseMessage?.StatusCode.ToString() ?? "Connection Error";
            _logger.LogWarning("[{CorrelationId}] IPS return request timeout/connection error - marking for SAF retry. Status: {Status}",
                correlationId, statusDescription);
            await _isoService.MarkForCheckStatusAsync(
                originalMessage,
                $"IPS return request timeout/connection error: {statusDescription}",
                ct);
            return Response<ReturnPaymentResponseDto>.Fail(
                "Request to IPS timed out or connection error - transaction marked for retry",
                responseMessage?.StatusCode ?? System.Net.HttpStatusCode.InternalServerError);
        }

        // Check for missing data (after timeout/connection error check)
        if (responseMessage.Data == null)
        {
            return await LogPersistAndReturnAsync(
                record,
                logMessage: "Failed to get valid response from SIPS",
                persistMessage: "Failed to get valid response from SIPS",
                data: "",
                failMessage: "Failed to get Valid Response from SIPS",
                statusCode: System.Net.HttpStatusCode.BadRequest,
                ct: ct);
        }

        // Handle bad request or unauthorized responses
        if (responseMessage.StatusCode == System.Net.HttpStatusCode.BadRequest ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return await LogPersistAndReturnAsync(
                record,
                logMessage: responseMessage.Message,
                persistMessage: "Failed to get valid response from SIPS",
                data: responseMessage.Data,
                failMessage: "Failed to get Valid Response from SIPS",
                statusCode: responseMessage.StatusCode,
                ct: ct);
        }

        // Verify signature
        var (ok, verbose) = await _signature.VerifyAsync(responseMessage.Data, ct);
        if (!ok)
        {
            _logger.LogError("[{CorrelationId}] Failed to verify IPS signature: {Verbose}", correlationId, verbose);
            await _isoService.MarkForCheckStatusAsync(
                originalMessage,
                "Failed to verify IPS signature on return response",
                ct);
            return Response<ReturnPaymentResponseDto>.Fail("Failed to verify the signature from IPS.", System.Net.HttpStatusCode.BadRequest);
        }

        // If all checks pass, return a successful response.
        return Response<ReturnPaymentResponseDto>.Success(new ReturnPaymentResponseDto
        {
            Status = ACSC,
        });
    }
    private Task<Response<ReturnPaymentResponseDto>> LogPersistAndReturnAsync(
        ISOMessage record,
        string logMessage,
        string persistMessage,
        string data,
        string failMessage,
        System.Net.HttpStatusCode statusCode,
        CancellationToken ct)
    {
        _logger.LogError("Failed to receive valid response from IPS: {Message}", logMessage);
        // Note: SAF marking handled in HandleSIPSCallExceptionAsync
        return Task.FromResult(Response<ReturnPaymentResponseDto>.Fail(failMessage, statusCode));
    }
    private static bool TryParse(string message, out ReturnPaymentResponseBuilder.Response? response)
    {
        response = ReturnPaymentResponseBuilder.Parse(message);

        if (response == null)
        {
            return false;
        }

        return true;
    }
    // Note: PersistISOMessageAsync removed - we now use inline persist with StatusOrchestrator
    // Status mapping handled by StatusOrchestrator.MapSingleStatus
}