using System.Text;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Interfaces;
using Microsoft.Extensions.Logging;
using SIPS.PostgreSQL.Models;
namespace SIPS.Core.Services;
public sealed class OutgoingReturnTransactionHandler(
    ISO20022Options options,
    ILogger<OutgoingReturnTransactionHandler> logger,
    IInterfaceHttpClient httpClient,
    INativeSigner signer,
    INativeVerifier verifier,
    IIncomingRecorder record
    ) : IOutgoingReturnTransactionHandler
{
    private readonly IInterfaceHttpClient _httpClient = httpClient;
    private readonly ISO20022Options _configuration = options;
    private readonly ILogger<OutgoingReturnTransactionHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly INativeVerifier _verifier = verifier;
    private readonly IIncomingRecorder _record = record;
    public async Task<Response<ReturnPaymentResponseDto>> HandleAsync(ReturnPaymentRequestDto message, CancellationToken ct)
    {
        var url = _configuration.SIPS ?? throw new InvalidOperationException("SIPS not found in configuration.");
        var fromBIC = _configuration.BIC ?? throw new InvalidOperationException("BIC not found in configuration.");
        var txId = Transformers.GenerateId(_configuration.BIC!);

        try
        {
            var originalMessage = await _record.GetISOMessageWithTransactionsByTxIdAsync(message.OriginalTxId, ct);
            if (originalMessage == null || originalMessage.Transactions.Count == 0)
            {
                return Response<ReturnPaymentResponseDto>.Fail("Transaction not found" + message.OriginalTxId, System.Net.HttpStatusCode.NotFound);
            }
            var transaction = originalMessage.Transactions.FirstOrDefault();
            if (transaction == null)
            {
                return Response<ReturnPaymentResponseDto>.Fail("Transaction not found" + message.OriginalTxId, System.Net.HttpStatusCode.NotFound);
            }
            originalMessage.ReturnId = message.ReturnId;
            var (document, bizMsgIdr, type, msgId) = BuildRequest(transaction!, fromBIC, message.ReturnId, reason: message.Reason, additionalInfo: message.AdditionalInfo);
            var signed = _signer.SignEnvelope(document);
            var entity = CreateISOMessage(message, transaction!, fromBIC, txId, signed, msgId, type, bizMsgIdr);
            var record = await _record.ISOMessageAsync(entity, ct);
            // Call the API to get the account details
            var responseMessage = await CallSIPSAsync(url, signed, ct);
            var responseMessageStatus = await HandleSIPSCallExceptionAsync(record, responseMessage, ct);
            if (!responseMessageStatus.IsSuccess)
            {
                return responseMessageStatus;
            }
            if (!TryParse(responseMessage.Data!, out var rs))
            {
                _logger.LogError("Failed to parse the message: {message}", responseMessage.Data);
                await PersistISOMessageAsync(record, RJCT, "Failed to parse the message", "Failed to parse the message", responseMessage.Data!, ct);
                return Response<ReturnPaymentResponseDto>.Fail("Failed to parse the message.", System.Net.HttpStatusCode.BadRequest);
            }
            await PersistISOMessageAsync(record, rs!.Status!, rs.Reason!, rs.AdditionalInfo, responseMessage.Data!, ct, rs.TxId, rs.Original?.OriginalEndToEnd ?? "");

            return Response<ReturnPaymentResponseDto>.Success(new ReturnPaymentResponseDto
            {
                Status = rs!.Status!,
                TxId = rs.TxId,
                EndToEndId = rs?.Original?.OriginalEndToEnd ?? "",
                Reason = rs!.Reason,
                AdditionalInfo = rs.AdditionalInfo,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to Send Request To SIPS Error: {Error}", ex);
            return Response<ReturnPaymentResponseDto>.Fail("Failed to Send Request To SIPS", System.Net.HttpStatusCode.InternalServerError);
        }
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
    private async Task<Response<string>> CallSIPSAsync(string url, string signed, CancellationToken ct)
    {
        var content = new StringContent(signed, Encoding.UTF8, "application/xml");
        return await _httpClient.Send4XML(url, content, ct);
    }
    private async Task<Response<ReturnPaymentResponseDto>> HandleSIPSCallExceptionAsync(
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
            return Response<ReturnPaymentResponseDto>.Fail("Failed to verify the signature from SIPS.", System.Net.HttpStatusCode.BadRequest);
        }

        // If all checks pass, return a successful response.
        return Response<ReturnPaymentResponseDto>.Success(new ReturnPaymentResponseDto
        {
            Status = ACSC,
        });
    }
    private async Task<Response<ReturnPaymentResponseDto>> LogPersistAndReturnAsync(
        ISOMessage record,
        string logMessage,
        string persistMessage,
        string data,
        string failMessage,
        System.Net.HttpStatusCode statusCode,
        CancellationToken ct)
    {
        _logger.LogError("Failed to receive valid response from SIPS: {Message}", logMessage);
        await PersistISOMessageAsync(record, RJCT, persistMessage, persistMessage, data, ct);
        return Response<ReturnPaymentResponseDto>.Fail(failMessage, statusCode);
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
    private async Task<bool> VerifySignatureAsync(string message, CancellationToken ct)
    {
        var (result, verbose) = await _verifier.VerifySignature(message, false, ct);

        if (!result)
        {
            _logger.LogError("Failed to verify the signature: verbose {verbose}", verbose);
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