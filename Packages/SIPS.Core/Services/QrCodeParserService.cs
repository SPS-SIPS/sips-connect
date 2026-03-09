using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using SIPS.Core.Interfaces;
using SIPS.Core.Models;
using SIPS.Emv.Helpers;
using SIPS.Emv.Models;

namespace SIPS.Core.Services;

public class QrCodeParserService : IQrCodeParserService
{
    private readonly IConfiguration _configuration;

    public QrCodeParserService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public QrCodeData Parse(string qrCode)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
            throw new ArgumentNullException(nameof(qrCode));

        var decoder = new PayloadDecoder();
        // Validation throws if CRC is invalid
        decoder.ValidateCrc(qrCode);

        // Decode root TLVs to check format indicator (Tag 00)
        var rootTlvs = decoder.DecodeQR(qrCode, false);
        var formatIndicatorTlv = rootTlvs.FirstOrDefault(t => t.Tag == 0);

        if (formatIndicatorTlv == null)
            throw new Exception("Payload Format Indicator (Tag 00) not found in QR Code");

        string formatIndicator = formatIndicatorTlv.Value;

        string acquirerId;
        string accountId;
        string accountType;

        if (formatIndicator == "02")
        {
            // P2P
            var payload = QRHelpers.ParseP2PQR(qrCode, false);
            accountId = payload.AccountNumber ?? string.Empty;
            
            if (accountId.Length < 8)
                throw new Exception("Invalid AccountNumber in P2P QR Code");
            
            // Assume format SOXX<ACQUIRER_ID>... (usually characters 4 to 7 are acquirer ID)
            acquirerId = accountId.Substring(4, 4);
            accountType = "IBAN";
        }
        else if (formatIndicator == "01")
        {
            // P2M
            var payload = QRHelpers.ParseQR(qrCode);
            
            var merchantIdentifierStr = _configuration["Emv:Tags:MerchantIdentifier"] ?? "26";
            var merchantIdentifier = int.Parse(merchantIdentifierStr);
            var merchantAccountObj = payload.MerchantAccount?.FirstOrDefault(x => x.Key == merchantIdentifier).Value;

            if (merchantAccountObj == null)
                throw new Exception($"MerchantAccount Tag {merchantIdentifier} is required for P2M extraction.");

            var acquirerTagStr = _configuration["Emv:Tags:AcquirerTag"] ?? "1";
            var merchantIdTagStr = _configuration["Emv:Tags:MerchantIdTag"] ?? "44";

            if (!merchantAccountObj.PaymentNetworkSpecific.TryGetValue(int.Parse(acquirerTagStr), out string? value01) || value01 == null)
                throw new Exception($"Acquirer tag {acquirerTagStr} not found in MerchantAccount {merchantIdentifier}.");

            if (!merchantAccountObj.PaymentNetworkSpecific.TryGetValue(int.Parse(merchantIdTagStr), out string? value02) || value02 == null)
                throw new Exception($"Merchant ID tag {merchantIdTagStr} not found in MerchantAccount {merchantIdentifier}.");

            if (value01.Length < 6)
                throw new Exception($"Invalid Acquirer network specific format in Tag {merchantIdentifier}. Expected at least 6 characters.");

            var institutionType = value01.Substring(0, 2);
            acquirerId = value01.Substring(2, 4);
            accountId = value02;

            // Per instructions: ACCT for P2M when it's a bank. Using "ACCT" as standard.
            accountType = "ACCT";
        }
        else
        {
            throw new Exception($"Unsupported Payload Format Indicator: {formatIndicator}");
        }

        var bic = ConvertAcquirerIdToBic(acquirerId);

        return new QrCodeData
        {
            AccountId = accountId,
            BankBICCode = bic,
            AccountType = accountType,
            AcquirerId = acquirerId
        };
    }

    private string ConvertAcquirerIdToBic(string acquirerId)
    {
        var acquirers = _configuration.GetSection("Emv:Acquirers").Get<System.Collections.Generic.List<AcquirerConfig>>();
        if (acquirers == null || !acquirers.Any())
        {
             throw new Exception("No Acquirers configured in appsettings (Emv:Acquirers list is empty).");
        }

        var match = acquirers.FirstOrDefault(a => a.Id == acquirerId);
        if (match == null)
             throw new Exception($"No Acquirer mapped for AcquirerId '{acquirerId}'. Please check configuration.");

        var bic = match.Bic;
        if (string.IsNullOrWhiteSpace(bic))
             throw new Exception($"Configuration for AcquirerId '{acquirerId}' missing Bic.");

        return bic;
    }
}
