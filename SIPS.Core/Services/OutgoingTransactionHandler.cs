using System.Text;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using Microsoft.Extensions.Logging;
namespace SIPS.Core.Services;
public sealed class OutgoingTransactionHandler(
    ISO20022Options options,
    ILogger<OutgoingTransactionHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IIncomingRecorder record
    ) : IOutgoingTransactionHandler
{
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ISO20022Options _configuration = options;
    private readonly ILogger<OutgoingTransactionHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly INativeVerifier _verifier = verifier;
    private readonly IIncomingRecorder _record = record;
    public async Task<Response<PaymentResponseDto>> HandleAsync(PaymentRequestDto message, CancellationToken ct)
    {
        _logger.LogInformation("Processing Outgoing Transaction Request");
        var url = _configuration.SIPS ?? throw new InvalidOperationException("SIPS not found in configuration.");
        var fromBIC = _configuration.BIC ?? throw new InvalidOperationException("BIC not found in configuration.");
        var ourAgentBic = _configuration.Agent ?? throw new InvalidOperationException("Agent BIC not found in configuration.");
        var txId = Transformers.GenerateId(_configuration.BIC!);

        try
        {
            // Step 1: Build, sign, and persist outgoing transaction request
            var (document, bizMsgIdr, type, msgId) = BuildRequest(message, fromBIC, ourAgentBic, txId);
            var signed = _signer.SignEnvelope(document);
            var entity = CreateISOMessage(message, fromBIC, ourAgentBic, txId, signed, bizMsgIdr, type, msgId);
            var record = await _record.ISOMessageAsync(entity, ct);

            // Step 2: Call SIPS and handle response
            var responseMessage = await CallSIPSAsync(url, signed, ct);
            var responseMessageStatus = await HandleSIPSCallExceptionAsync(record, responseMessage, ct);
            if (!responseMessageStatus.IsSuccess)
                return responseMessageStatus;

            // Step 3: Parse and persist SIPS response
            if (!TryParse(responseMessage.Data!, out var rs) || rs == null)
            {
                _logger.LogError("Failed to parse the message: {message}", responseMessage.Data);
                await PersistISOMessageAsync(record, RJCT, "Failed to parse the message", "Failed to parse the message", responseMessage.Data!, ct);
                return Response<PaymentResponseDto>.Fail("Failed to parse the message.", System.Net.HttpStatusCode.BadRequest);
            }
            await PersistISOMessageAsync(record, rs.Status ?? RJCT, rs.Reason ?? MISS, rs.AdditionalInfo ?? string.Empty, responseMessage.Data!, ct, rs.TxId ?? string.Empty, rs.Original?.EndToEndId ?? string.Empty);

            // Step 4: Return success response
            return Response<PaymentResponseDto>.Success(new PaymentResponseDto
            {
                Status = rs.Status ?? RJCT,
                AcceptanceDate = rs.AcceptanceDate,
                TxId = rs.TxId ?? string.Empty,
                EndToEndId = rs.Original?.EndToEndId ?? string.Empty,
                Reason = rs.Reason ?? string.Empty,
                AdditionalInfo = rs.AdditionalInfo ?? string.Empty
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to Send Request To SIPS Error: {Error}", ex);
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
                Issuer = "C"
            },
            Creditor = new ISO20022.Models.Person
            {
                Name = message.CreditorName,
                Account = message.CreditorAccount,
                AccountType = message.CreditorAccountType,
                AgentBIC = message.CreditorAgentBIC,
                Issuer = message.CreditorIssuer ?? "C"
            },
            Ustrd = message.RemittanceInformation,
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
            MsgId = msgId
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
            DebtorAccountType = message.DebtorAccountType,
            DebtorAgentBIC = agentBIC,
            DebtorIssuer = message.DebtorIssuer ?? "C",

            CreditorName = message.CreditorName,
            CreditorAccount = message.CreditorAccount,
            CreditorAccountType = message.CreditorAccountType,
            CreditorAgentBIC = message.CreditorAgentBIC,
            CreditorIssuer = message.CreditorIssuer ?? "C",
            RemittanceInformation = message.RemittanceInformation
        });

        return entity;
    }
    private async Task<Response<string>> CallSIPSAsync(string url, string signed, CancellationToken ct)
    {
        var content = new StringContent(signed, Encoding.UTF8, "application/xml");
        // Log the callback URL and payload
        _logger.LogInformation("Callback URL: {Url}", url);
        _logger.LogInformation("Callback Payload: {Payload}", signed);
        return await _httpClient.Send4XML(url, content, ct);
    }
    private async Task<Response<PaymentResponseDto>> HandleSIPSCallExceptionAsync(
    PostgreSQL.Models.ISOMessage record,
    Response<string>? responseMessage,
    CancellationToken ct)
    {
        // Check for null or missing data
        if (responseMessage == null || responseMessage.Data == null)
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

        // Handle timeout or bad gateway responses
        if (responseMessage.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
            responseMessage.StatusCode == System.Net.HttpStatusCode.BadGateway)
        {
            return await LogPersistAndReturnAsync(
                record,
                logMessage: responseMessage.Message,
                persistMessage: "Request to SIPS timed out!",
                data: responseMessage.Data,
                failMessage: "Request to SIPS timed out!",
                statusCode: responseMessage.StatusCode,
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
        if (!await VerifySignatureAsync(responseMessage.Data, ct))
        {
            _logger.LogError("Failed to verify the signature: {Verbose}", responseMessage.Data);
            await PersistISOMessageAsync(record, RJCT, "Failed to verify the signature", "Failed to verify the signature", responseMessage.Data, ct);
            return Response<PaymentResponseDto>.Fail("Failed to verify the signature from SIPS.", System.Net.HttpStatusCode.BadRequest);
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
        _logger.LogError("Failed to receive valid response from SIPS: {Message}", logMessage);
        await PersistISOMessageAsync(record, RJCT, persistMessage, persistMessage, data, ct);
        return Response<PaymentResponseDto>.Fail(failMessage, statusCode);
    }
    private static bool TryParse(string message, out PaymentRequestResponseBuilder.Response? response)
    {
        response = PaymentRequestResponseBuilder.Parse(message);

        if (response == null)
        {
            return false;
        }

        return true;
    }
    private async Task<bool> VerifySignatureAsync(string message, CancellationToken ct)
    {
        var (result, verbose) = await _verifier.VerifySignature(message, false, ct);

        if (!result)
        {
            var verboseJson = System.Text.Json.JsonSerializer.Serialize(verbose);
            _logger.LogError("Failed to verify the signature: verbose {verbose}", verboseJson);
        }

        return result;
    }
    private async Task PersistISOMessageAsync(PostgreSQL.Models.ISOMessage isoMessage, string status, string reason, string? additionalInfo, string rsp, CancellationToken ct, string txId = "", string endToEndId = "")
    {
        isoMessage.Response = Encoding.UTF8.GetBytes(rsp);
        isoMessage.Status = status == ACSC ? PostgreSQL.Enums.TransactionStatus.Success : PostgreSQL.Enums.TransactionStatus.Failed;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        if (txId != "")
        {
            isoMessage.TxId = txId;
        }
        if (endToEndId != "")
        {
            isoMessage.EndToEndId = endToEndId;
        }
        await _record.ISOMessageResponseAsync(isoMessage, ct);
    }
}