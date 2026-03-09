using System.Text;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.XMLDsig.Xades.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Correlation;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
using SIPS.Core.Services.Metrics;
using static SIPS.Core.Constants;
namespace SIPS.Core.Services;
public sealed class OutgoingTransactionHandler(
    ISO20022Options options,
    ILogger<OutgoingTransactionHandler> logger,
    INativeSigner signer,
    ISignatureService signature,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    SIPS.Core.Services.Abstractions.ISipsRequestSender sips,
    SIPS.Core.Services.Abstractions.IISOMessageService isoService,
    SIPS.Core.Services.Abstractions.IStatusOrchestrator statusOrchestrator,
    IOptions<CoreOptions> coreOptions
    ) : IOutgoingTransactionHandler
{
    private readonly ISO20022Options _configuration = options;
    private readonly ILogger<OutgoingTransactionHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly ISignatureService _signature = signature;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly SIPS.Core.Services.Abstractions.ISipsRequestSender _sips = sips;
    private readonly SIPS.Core.Services.Abstractions.IISOMessageService _isoService = isoService;
    private readonly SIPS.Core.Services.Abstractions.IStatusOrchestrator _statusOrchestrator = statusOrchestrator;
    private readonly CoreOptions _core = coreOptions.Value;
    public async Task<Response<PaymentResponseDto>> HandleAsync(PaymentRequestDto message, CancellationToken ct)
    {
        using var _totalTrack = SipsMetrics.TrackStep("Outgoing", "Transaction", "Total");
        _logger.LogInformation("Processing Outgoing Transaction Request");
        _logger.LogDebug("[OutgoingTransactionHandler] HandleAsync start");
        var url = _configuration.SIPS ?? throw new InvalidOperationException("SIPS not found in configuration.");
        var fromBIC = _configuration.BIC ?? throw new InvalidOperationException("BIC not found in configuration.");
        var ourAgentBic = _configuration.Agent ?? throw new InvalidOperationException("Agent BIC not found in configuration.");
        
        // Step 0: Enforce TxId as the sovereign anchor (Delta 2)
        var txId = !string.IsNullOrWhiteSpace(message.TxId) ? message.TxId : Transformers.GenerateId(_configuration.BIC!);
        var cid = _correlation.Create(txId);

        try
        {
            // Step 1: Build, sign, and persist outgoing transaction request as Pending
            string document, bizMsgIdr, type, msgId, signed;
            using (SipsMetrics.TrackStep("Outgoing", "Transaction", "BuildAndSign"))
            {
                (document, bizMsgIdr, type, msgId) = BuildRequest(message, fromBIC, ourAgentBic, txId);
                _logger.LogDebug("[OutgoingTransactionHandler] after BuildRequest");
                signed = _signer.SignEnvelope(document);
                _logger.LogDebug("[OutgoingTransactionHandler] after SignEnvelope");
            }
            var entity = CreateISOMessage(message, fromBIC, ourAgentBic, txId, signed, bizMsgIdr, type, msgId);
            // Set initial status as Pending - completion will be determined by pacs.002
            entity.Status = PostgreSQL.Enums.TransactionStatus.Pending;
            _logger.LogDebug("[OutgoingTransactionHandler] after CreateISOMessage");
            using var dbCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            var dbCt = dbCts.Token;
            _logger.LogDebug("[OutgoingTransactionHandler] before persist - dbCt.CanBeCanceled={CanBeCanceled} IsCancellationRequested={IsCanceled}", dbCt.CanBeCanceled, dbCt.IsCancellationRequested);
            
            SIPS.PostgreSQL.Models.ISOMessage record;
            using (SipsMetrics.TrackStep("Outgoing", "Transaction", "DbSave"))
            {
                record = await _persistence.RecordISOMessageAsync(entity, dbCt);
            }
            _logger.LogDebug("[OutgoingTransactionHandler] after persist");

            // Step 2: Call SIPS and handle response
            SIPS.ISO20022.Models.DTOs.Response<string> responseMessage;
            using (SipsMetrics.TrackStep("Outgoing", "Transaction", "SipsCall"))
            {
                responseMessage = await _sips.SendAsync(url, signed, ct, cid);
            }
            var responseMessageStatus = await HandleSIPSCallExceptionAsync(record, responseMessage, dbCt, cid);
            // If handler returns a response (error or PDNG), return it immediately
            // Only continue if we got a valid parseable response (indicated by ACSC dummy status)
            if (!responseMessageStatus.IsSuccess || responseMessageStatus.Data?.Status == PDNG)
                return responseMessageStatus;

            // Step 3: Parse IPS response and finalize transaction
            // Note: For outgoing pacs.008, IPS responds immediately with final status
            // This is different from incoming flow where we wait for separate pacs.002
            if (!TryParse(responseMessage.Data!, out var rs) || rs == null)
            {
                _logger.LogError("[{CorrelationId}] Failed to parse IPS response: {message}", cid, responseMessage.Data);
                await _isoService.MarkForCheckStatusAsync(record, "Failed to parse IPS response", dbCt);

                // Return PDNG instead of FAIL - IPS received the request but response is invalid
                // SAF will check the actual status later
                return Response<PaymentResponseDto>.Success(new PaymentResponseDto
                {
                    Status = PDNG,
                    TxId = record.TxId ?? string.Empty,
                    EndToEndId = record.EndToEndId ?? string.Empty,
                    Reason = "Pending - IPS parse failed",
                    AdditionalInfo = "IPS received request but returned invalid response. Transaction marked for status verification. Do not reverse."
                });
            }
            _logger.LogInformation("[{CorrelationId}] Received response from IPS for TxId {TxId}: Status={Status}", cid, txId, rs.Status);
            _logger.LogDebug("[{CorrelationId}] IPS Response: {Response}", cid, JsonSerializer.Serialize(rs, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));

            // Use StatusOrchestrator to map IPS status to final status
            var finalStatus = _statusOrchestrator.MapSingleStatus(rs.Status ?? RJCT, "IPS");

            // Finalize transaction with IPS response
            record.Response = Encoding.UTF8.GetBytes(responseMessage.Data!);
            record.Status = finalStatus;
            record.Reason = rs.Reason ?? string.Empty;
            record.AdditionalInfo = rs.AdditionalInfo ?? string.Empty;

            await _persistence.ISOMessageResponseAsync(record, dbCt);

            // Step 4: Return final status to caller
            return Response<PaymentResponseDto>.Success(new PaymentResponseDto
            {
                Status = rs.Status ?? ACSC,
                AcceptanceDate = rs.AcceptanceDate,
                TxId = txId,
                EndToEndId = message.EndToEndId,
                Reason = rs.Reason ?? "Transaction completed",
                AdditionalInfo = rs.AdditionalInfo ?? string.Empty
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("[{CorrelationId}] Failed to Send Request To SIPS Error: {Error}", cid, ex);
            _logger.LogDebug(ex, "[OutgoingTransactionHandler] Exception: {Exception}", ex.Message);
            return Response<PaymentResponseDto>.Fail("Failed to Send Request To SIPS", System.Net.HttpStatusCode.InternalServerError);
        }
    }
    private static (string document, string bizMsgIdr, string type, string msgId) BuildRequest(PaymentRequestDto message, string fromBIC, string agentBIC, string txId)
    {
        return PaymentRequestBuilder.Build(new PaymentRequestBuilder.Request
        {
            From = fromBIC,
            To = message.ToBIC,
            CreDt = DateTime.UtcNow,
            LocalInstrument = message.LocalInstrument,
            CategoryPurpose = message.CategoryPurpose,
            EndToEndId = message.EndToEndId,
            Amount = message.Amount,
            Currency = message.Currency,
            Debtor = new ISO20022.Models.Person
            {
                Name = message.DebtorName,
                Account = message.DebtorAccount,
                AccountType = message.DebtorAccountType,
                AgentBIC = agentBIC,
                Address = message.DebtorAddress ?? string.Empty,
                Issuer = "C"
            },

            Creditor = new ISO20022.Models.Person
            {
                Name = message.CreditorName,
                Account = message.CreditorAccount,
                AccountType = message.CreditorAccountType,
                AgentBIC = message.CreditorAgentBIC,
                Address = message.CreditorAddress ?? string.Empty,
                Issuer = message.CreditorIssuer ?? "C"
            },
            Ustrd = message.RemittanceInformation ?? string.Empty,
            TxId = txId
        });
    }
    private static PostgreSQL.Models.ISOMessage CreateISOMessage(PaymentRequestDto message, string fromBIC, string agentBIC, string txId, string signedMessage, string bizMsgIdr, string msgDefIdr, string msgId)
    {
        var entity = new PostgreSQL.Models.ISOMessage
        {
            MessageType = PostgreSQL.Enums.ISOMessageType.TransactionRequest,
            Date = DateTimeOffset.Now.ToUniversalTime(),
            FromBIC = fromBIC,
            ToBIC = message.ToBIC,
            Message = Encoding.UTF8.GetBytes(signedMessage),
            BizMsgIdr = bizMsgIdr,
            MsgDefIdr = msgDefIdr,
            MsgId = msgId,
            EndToEndId = message.EndToEndId,
            TxId = txId
        };
        entity.Transactions.Add(new PostgreSQL.Models.Transaction
        {
            Type = PostgreSQL.Enums.TransactionType.Withdrawal,
            FromBIC = fromBIC,
            LocalInstrument = message.LocalInstrument,
            CategoryPurpose = message.CategoryPurpose,
            EndToEndId = message.EndToEndId,
            TxId = txId,
            Amount = message.Amount,
            Currency = message.Currency,
            DebtorName = message.DebtorName,
            DebtorAccount = message.DebtorAccount,
            DebtorAddress = message.DebtorAddress ?? string.Empty,
            DebtorAccountType = message.DebtorAccountType,
            DebtorAgentBIC = agentBIC,
            DebtorIssuer = message.DebtorIssuer ?? "C",

            CreditorName = message.CreditorName,
            CreditorAccount = message.CreditorAccount,
            CreditorAddress = message.CreditorAddress ?? string.Empty,
            CreditorAccountType = message.CreditorAccountType,
            CreditorAgentBIC = message.CreditorAgentBIC,
            CreditorIssuer = message.CreditorIssuer ?? "C",
            RemittanceInformation = message.RemittanceInformation
        });

        return entity;
    }

    private async Task<Response<PaymentResponseDto>> HandleSIPSCallExceptionAsync(
    PostgreSQL.Models.ISOMessage record,
    Response<string>? responseMessage,
    CancellationToken ct,
    string correlationId)
    {
        // Handle timeout, bad gateway, or connection errors FIRST - mark for SAF retry
        // CRITICAL: Return PDNG (Pending) to CoreBank instead of failure
        // This prevents CoreBank from auto-reversing while the transaction may have succeeded at IPS
        // Connection errors (null response or 500 InternalServerError from HttpClient) should be treated like timeouts
        if (responseMessage == null ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.BadGateway ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.InternalServerError)
        {
            var statusDescription = responseMessage?.StatusCode.ToString() ?? "Connection Error";
            _logger.LogWarning("[{CorrelationId}] IPS send timeout/connection error - marking for SAF retry. Status: {Status}",
                correlationId, statusDescription);
            await _isoService.MarkForCheckStatusAsync(
                record,
                $"IPS send timeout/connection error: {statusDescription}",
                ct);

            // Return PDNG status to CoreBank - transaction is pending final confirmation
            // CoreBank MUST NOT reverse the transaction on PDNG status
            // CoreBank should either:
            // 1. Wait for completion callback from SIPS when SAF resolves the status
            // 2. Implement their own status inquiry mechanism
            // 3. Mark transaction as pending and require manual reconciliation
            return Response<PaymentResponseDto>.Success(new PaymentResponseDto
            {
                Status = PDNG,
                TxId = record.TxId ?? string.Empty,
                EndToEndId = record.EndToEndId ?? string.Empty,
                Reason = "Pending - awaiting IPS confirm",
                AdditionalInfo = $"Network error occurred. Transaction marked for status verification. Do not reverse. Status: {statusDescription}"
            });
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
            _logger.LogWarning("[{CorrelationId}] Failed to verify IPS signature: {Verbose}. Marking for SAF status check.", correlationId, verbose);
            await _isoService.MarkForCheckStatusAsync(
                record,
                "Failed to verify IPS signature",
                ct);
            
            // Return PDNG instead of FAIL - request reached IPS, but response signature is suspect.
            // SAF will reconcile the actual status later.
            return Response<PaymentResponseDto>.Success(new PaymentResponseDto
            {
                Status = PDNG,
                TxId = record.TxId ?? string.Empty,
                EndToEndId = record.EndToEndId ?? string.Empty,
                Reason = "Pending - IPS signature verification failed",
                AdditionalInfo = "IPS received request but response signature is invalid. Marked for status verification."
            });
        }

        // If all checks pass, return a successful response.
        return Response<PaymentResponseDto>.Success(new PaymentResponseDto
        {
            Status = ACSC,
        });
    }
    private async Task<Response<PaymentResponseDto>> LogPersistAndReturnAsync(
        PostgreSQL.Models.ISOMessage record,
        string logMessage,
        string persistMessage,
        string data,
        string failMessage,
        System.Net.HttpStatusCode statusCode,
        CancellationToken ct)
    {
        _logger.LogError("Failed to receive valid response from IPS: {Message}", logMessage);
        await _isoService.MarkForCheckStatusAsync(record, persistMessage, ct);
        return Response<PaymentResponseDto>.Fail(failMessage, statusCode);
    }
    private static bool TryParse(string message, out PaymentRequestResponseBuilder.Response? response)
    {
        try
        {
            response = PaymentRequestResponseBuilder.Parse(message);

            if (response == null)
            {
                return false;
            }

            return true;
        }
        catch
        {
            // XML parsing exceptions (e.g., "Root element is missing")
            response = null;
            return false;
        }
    }
    // Note: PersistISOMessageAsync removed - we no longer finalize status here
    // Status is kept as Pending until pacs.002 is received
    // Completion is handled by IncomingPaymentStatusReportHandler
}