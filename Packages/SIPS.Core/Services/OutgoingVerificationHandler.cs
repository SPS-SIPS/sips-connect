using System.Text;
using SIPS.Core.Interfaces;
using SIPS.ISO20022.Helpers;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Options;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using SIPS.XMLDsig.Xades.Interfaces;
using Microsoft.Extensions.Logging;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Correlation;
using Microsoft.Extensions.Options;
using SIPS.Core.Options;
using SIPS.Core.Services.Metrics;
using SIPS.ISO20022.Models;
using static SIPS.Core.Constants;

namespace SIPS.Core.Services;

public sealed class OutgoingVerificationHandler(
    ISO20022Options options,
    ILogger<OutgoingVerificationHandler> logger,
    INativeSigner signer,
    ISignatureService signature,
    IPersistenceGateway persistence,
    ICorrelationService correlation,
    SIPS.Core.Services.Abstractions.ISipsRequestSender sips,
    SIPS.Core.Services.Abstractions.IStatusOrchestrator statusOrchestrator,
    IOptions<CoreOptions> coreOptions,
    IQrCodeParserService qrCodeParserService
    ) : IOutgoingVerificationHandler
{
    private readonly ISO20022Options _configuration = options;
    private readonly ILogger<OutgoingVerificationHandler> _logger = logger;
    private readonly INativeSigner _signer = signer;
    private readonly ISignatureService _signature = signature;
    private readonly IPersistenceGateway _persistence = persistence;
    private readonly ICorrelationService _correlation = correlation;
    private readonly SIPS.Core.Services.Abstractions.ISipsRequestSender _sips = sips;
    private readonly SIPS.Core.Services.Abstractions.IStatusOrchestrator _statusOrchestrator = statusOrchestrator;
    private readonly CoreOptions _core = coreOptions.Value;
    private readonly IQrCodeParserService _qrCodeParserService = qrCodeParserService;

    public async Task<Response<VerificationResponseDto>> HandleAsync(VerificationRequestDto message, CancellationToken ct)
    {
        using var _totalTrack = SipsMetrics.TrackStep("Outgoing", "Verification", "Total");
        // Parse QR Code if present
        QrCodeData? qrData = null;
        if (!string.IsNullOrWhiteSpace(message.Code))
        {
            try
            {
                qrData = _qrCodeParserService.Parse(message.Code);
                message.Alias = qrData.AccountId;
                message.Type = qrData.AccountType;
                message.ToBIC = qrData.BankBICCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse QR Code for verification request: {Error}", ex.Message);
                return Response<VerificationResponseDto>.Fail("failed to verify the qr code", System.Net.HttpStatusCode.BadRequest);
            }
        }

        // Step 1: Validate configuration and request values
        var fromBIC = _configuration.BIC ?? throw new InvalidOperationException("BIC not found in configuration.");
        var url = _configuration.SIPS ?? throw new InvalidOperationException("SIPS not found in configuration.");
        
        // Step 0: Enforce MsgId as the sovereign anchor (Delta 4)
        var msgIdAnchor = !string.IsNullOrWhiteSpace(message.MsgId) ? message.MsgId : Transformers.GenerateId(_configuration.BIC!);
        var cid = _correlation.Create(msgIdAnchor);

        if (string.IsNullOrEmpty(message.Alias) ||
            string.IsNullOrEmpty(message.Type) ||
            string.IsNullOrEmpty(message.ToBIC))
        {
            return Response<VerificationResponseDto>.Fail("Failed to get proper values from the request!", System.Net.HttpStatusCode.BadRequest);
        }

        try
        {
            // Step 2: Build and sign the verification request
            string signedRequest, bizMsgIdr, type, msgId;
            using (SipsMetrics.TrackStep("Outgoing", "Verification", "BuildAndSign"))
            {
                if (!BuildRequest(message, fromBIC, msgIdAnchor, out signedRequest, out bizMsgIdr, out type, out msgId))
                {
                    return Response<VerificationResponseDto>.Fail("Failed to build acmt.023 message from your request", System.Net.HttpStatusCode.BadRequest);
                }
            }

            // Step 3: Create and persist the initial ISO message record
            var isoMessage = new ISOMessage
            {
                MessageType = PostgreSQL.Enums.ISOMessageType.VerificationRequest,
                Date = DateTimeOffset.UtcNow,
                FromBIC = fromBIC,
                ToBIC = message.ToBIC,
                Message = Encoding.UTF8.GetBytes(signedRequest),
                Status = PostgreSQL.Enums.TransactionStatus.Pending,
                BizMsgIdr = bizMsgIdr,
                MsgDefIdr = type,
                MsgId = msgId,
                TxId = msgId, // Anchor MsgId in TxId for status requests
                UETR = msgId
            };
            using var dbCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            var dbCt = dbCts.Token;
            ISOMessage record;
            using (SipsMetrics.TrackStep("Outgoing", "Verification", "DbSave"))
            {
                record = await _persistence.RecordISOMessageAsync(isoMessage, dbCt);
            }

            // Step 4: Send the verification request to SIPS
            Response<string> responseMessage;
            using (SipsMetrics.TrackStep("Outgoing", "Verification", "SipsCall"))
            {
                responseMessage = await _sips.SendAsync(url, signedRequest, ct, cid);
            }
            
            using var updateCts = new CancellationTokenSource(TimeSpan.FromSeconds(_core.DbPersistTimeoutSeconds > 0 ? _core.DbPersistTimeoutSeconds : 10));
            var updateCt = updateCts.Token;
            
            record.Response = Encoding.UTF8.GetBytes(responseMessage.Data ?? "");

            // Step 5: Validate the IPS response
            if (!responseMessage.IsSuccess || string.IsNullOrEmpty(responseMessage.Data))
            {
                var failedStatus = _statusOrchestrator.MapSingleStatus("RJCT", "IPS");
                await PersistISOMessageAsync(record, failedStatus, "Failed to receive valid response from IPS", responseMessage.Message, responseMessage.Data ?? string.Empty, updateCt);
                return Response<VerificationResponseDto>.Fail(Transformers.TransformSIPSHttpError(responseMessage.StatusCode), responseMessage.StatusCode);
            }

            // Step 6: Verify the signature on the IPS response
            var (ok, verbose) = await _signature.VerifyAsync(responseMessage.Data, ct);
            if (!ok)
            {
                var failedStatus = _statusOrchestrator.MapSingleStatus(RJCT, "IPS");
                await PersistISOMessageAsync(record, failedStatus, "Failed to verify the signature from IPS", "Signature verification failed", responseMessage.Data, updateCt);
                _logger.LogWarning("[{CorrelationId}] Failed to verify the IPS response signature: {Verbose}", cid, verbose);
                return Response<VerificationResponseDto>.Fail("Failed to verify the signature from IPS.", System.Net.HttpStatusCode.BadRequest);
            }

            // Step 7: Parse and persist the IPS response
            var parsedResponse = PayeeVerificationResponseBuilder.Parse(responseMessage.Data);

            // Use StatusOrchestrator to map verification result consistently
            // Verified=true → SUCC, Verified=false → MISS
            var statusCode = parsedResponse.Verified ? SUCC : MISS;
            var finalStatus = _statusOrchestrator.MapSingleStatus(statusCode, "IPS");

            await PersistISOMessageAsync(record, finalStatus, parsedResponse.Reason ?? string.Empty, parsedResponse.VerificationId ?? string.Empty, responseMessage.Data, updateCt);

            // Step 8: Return success response
            var accountNo = parsedResponse.Verified ? parsedResponse.Id : null;
            var accountType = parsedResponse.Verified ? parsedResponse.Type : null;
            var name = parsedResponse.Verified ? parsedResponse.Name : null;
            P2GNameDescriptor? p2g = null;
            if (parsedResponse.Verified && P2GNameDescriptorParser.TryParse(parsedResponse.Name, out var parsedP2G))
            {
                p2g = parsedP2G;
            }

            var isP2G = p2g is not null;
            string? p2gBillReference = null;
            if (isP2G && !TryGetOriginalP2GBillReference(message, out p2gBillReference))
            {
                return Response<VerificationResponseDto>.Fail(
                    "P2G descriptor requires original BILL/INVOICE/UPR lookup input",
                    System.Net.HttpStatusCode.BadRequest);
            }

            var creditorName = isP2G
                ? FirstNonEmpty(parsedResponse.Address, parsedResponse.Name)
                : name;
            var paymentCurrency = isP2G ? p2g!.Currency : parsedResponse.Currency;
            var amountPayable = isP2G ? p2g!.Amount : parsedResponse.AmountPayable;
            var billReference = isP2G
                ? p2gBillReference!
                : FirstNonEmpty(
                    parsedResponse.BillReference,
                    parsedResponse.Upr,
                    parsedResponse.InvoiceId,
                    message.Alias);

            return Response<VerificationResponseDto>.Success(new VerificationResponseDto
            {
                IsVerified = parsedResponse.Verified,
                SIPSRequestId = parsedResponse.MsgId ?? string.Empty, // Anchor to MsgId
                Reason = parsedResponse.Reason ?? string.Empty,
                AccountNo = accountNo,
                AccountType = accountType,
                Name = isP2G ? creditorName : name,
                Address = parsedResponse.Verified ? parsedResponse.Address : null,
                Currency = parsedResponse.Verified ? paymentCurrency : null,
                PaymentCurrency = parsedResponse.Verified ? paymentCurrency : null,
                IsP2G = isP2G,
                InvoiceId = isP2G ? p2g!.InvoiceId : parsedResponse.Verified ? parsedResponse.InvoiceId : null,
                Upr = parsedResponse.Verified ? parsedResponse.Upr : null,
                BillReference = parsedResponse.Verified ? billReference : null,
                Mda = parsedResponse.Verified ? parsedResponse.Mda : null,
                MdaId = parsedResponse.Verified ? parsedResponse.MdaId : null,
                MdaCode = isP2G ? p2g!.MdaCode : parsedResponse.Verified ? parsedResponse.MdaCode : null,
                ServiceCode = isP2G ? p2g!.ServiceCode : null,
                AmountPayable = parsedResponse.Verified ? amountPayable : null,
                Amount = parsedResponse.Verified ? amountPayable : null,
                DueDate = isP2G ? p2g!.DueDate.ToString("yyyyMMdd") : null,
                PayerReference = isP2G ? p2g!.PayerReference : null,
                CreditorAccount = accountNo,
                CreditorName = creditorName,
                CreditorAccountType = accountType,
                AmountLocked = parsedResponse.Verified && amountPayable.HasValue,
                CreditorLocked = parsedResponse.Verified && !string.IsNullOrWhiteSpace(accountNo),
                RemittanceInformation = parsedResponse.Verified && !string.IsNullOrWhiteSpace(billReference) ? $"BILL:{billReference}" : null,
                Parsed = qrData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Failed to process the verification request.", cid);
            return Response<VerificationResponseDto>.Fail("Failed to process the request", System.Net.HttpStatusCode.InternalServerError);
        }
    }

    private static bool IsInvoiceOrUprLookup(string? type) =>
        string.Equals(type, "BILL", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "INVOICE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "UPR", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetOriginalP2GBillReference(VerificationRequestDto message, out string billReference)
    {
        billReference = string.Empty;

        if (!IsInvoiceOrUprLookup(message.Type) || string.IsNullOrWhiteSpace(message.Alias))
        {
            return false;
        }

        billReference = message.Alias;
        return true;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private bool BuildRequest(VerificationRequestDto message, string fromBIC, string? anchorMsgId, out string signedMessage, out string bizMsgIdr, out string type, out string msgId)
    {
        var req = new PayeeVerificationBuilder.Request
        {
            From = fromBIC,
            Alias = message.Alias,
            Type = message.Type,
            To = message.ToBIC,
            MsgId = anchorMsgId ?? string.Empty
        };
        (string document, bizMsgIdr, type) = PayeeVerificationBuilder.Build(req);
        msgId = req.MsgId;

        if (document != null)
        {
            signedMessage = _signer.SignEnvelope(document);
            return !string.IsNullOrEmpty(signedMessage);
        }
        else
        {
            signedMessage = string.Empty;
            return false;
        }
    }



    private async Task PersistISOMessageAsync(ISOMessage isoMessage, PostgreSQL.Enums.TransactionStatus status, string reason, string additionalInfo, string response, CancellationToken ct)
    {
        isoMessage.Status = status;
        isoMessage.Reason = reason;
        isoMessage.AdditionalInfo = additionalInfo;
        isoMessage.Response = Encoding.UTF8.GetBytes(response);
        await _persistence.ISOMessageResponseAsync(isoMessage, ct);
    }
}
