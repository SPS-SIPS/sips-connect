using SIPS.Emv.Helpers;
using SIPS.Emv.Models;
using Microsoft.AspNetCore.Mvc;

namespace SIPS.Connect.Controllers;
[ApiController]
[Route("api/v1/[controller]")]
public class SomQRController(IConfiguration configuration) : ControllerBase
{
    private readonly IConfiguration _configuration = configuration;
    [HttpPost("GenerateMerchantQR")]
    public IResult GenerateCode(SomQRMerchantRequest payload)
    {
        if (!ModelState.IsValid)
            return Results.BadRequest(ModelState);

        if (!GetKeyFromConfiguration(_configuration, "Emv:AcquirerId", out var acquirerId))
            return Results.BadRequest(new { message = "AcquirerId not found!" });

        if (!GetKeyFromConfiguration(_configuration, "Emv:Tags:MerchantIdentifier", out var merchantIdentifier))
            return Results.BadRequest(new { message = "MerchantIdentifier not found!" });

        if (!GetKeyFromConfiguration(_configuration, "Emv:Tags:AcquirerTag", out var acquirerTag))
            return Results.BadRequest(new { message = "Som QR Acquirer Tag not found!" });

        if (!GetKeyFromConfiguration(_configuration, "Emv:Tags:MerchantIdTag", out var merchantIdTag))
            return Results.BadRequest(new { message = "Som QR Acquirer Tag not found!" });

        if (!GetKeyFromConfiguration(_configuration, "Emv:CountryCode", out var countryCode))
            return Results.BadRequest(new { message = "Som QR countryCode not found!" });

        if (!GetKeyFromConfiguration(_configuration, "Emv:Version", out var version))
            return Results.BadRequest(new { message = "Som QR Version not found!" });

        var mp = new MerchantPayload
        {
            PayloadFormatIndicator = version,
            PointOfInitializationMethod = $"{payload.Method}{payload.Type}",
            MerchantAccount = new MerchantAccountDictionary {
                {
                    int.Parse(merchantIdentifier),
                    new MerchantAccount
                    {
                        GlobalUniqueIdentifier = Constants.DomainId,
                        PaymentNetworkSpecific = new Dictionary<int, string>{
                            { int.Parse(acquirerTag), acquirerId },
                            { int.Parse(merchantIdTag), payload.MerchantId! }
                        }
                    }
                }
            },
            MerchantCategoryCode = payload.MerchantCategoryCode,
            TransactionCurrency = payload.CurrencyCode,
            CountyCode = countryCode,
            MerchantName = payload.MerchantName,
            MerchantCity = payload.MerchantCity,
            AdditionalData = new MerchantAdditionalData
            {
                StoreLabel = payload.StoreLabel,
                TerminalLabel = payload.TerminalLabel
            },
            PostalCode = payload.PostalCode,
        };

        try
        {
            var result = mp.GeneratePayload();
            return Results.Ok(new
            {
                data = result
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("GeneratePersonQR")]
    public IResult GenerateP2PCode(SomQRPersonRequest payload)
    {
        if (!ModelState.IsValid)
        {
            return Results.BadRequest(ModelState);
        }

        if (!GetKeyFromConfiguration(_configuration, "Emv:FIType", out var fiType))
            return Results.BadRequest(new { message = "FIType not found!" });
        if (!GetKeyFromConfiguration(_configuration, "Emv:FIName", out var fiName))
            return Results.BadRequest(new { message = "FIName not found!" });
        if (!GetKeyFromConfiguration(_configuration, "Emv:Version", out var version))
            return Results.BadRequest(new { message = "Som QR Version not found!" });



        var mp = new P2PPayload
        {
            PayloadFormatIndicator = Constants.PayloadFormatIndicator,
            PointOfInitializationMethod = PointOfInitializationMethod.GetQRType(payload.Amount),
            SchemeIdentifier = fiType,
            FiName = fiName,
            AccountNumber = payload.IBAN,
            AccountName = payload.AccountName,
            Amount = payload.Amount,
            Particulars = payload.Particulars
        };
        try
        {
            var result = mp.GeneratePayload();
            return Results.Ok(new
            {
                data = result
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }


    [HttpGet("ParseMerchantQR")]
    public IResult ParseCode([FromQuery] string code)
    {
        try
        {
            var merchantPayload = QRHelpers.ParseQR(code);
            return Results.Ok(new { data = merchantPayload });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("ParsePersonQR")]
    public IResult ParseCodeP2P([FromQuery] string code)
    {
        try
        {
            var p2p = QRHelpers.ParseP2PQR(code, false);
            return Results.Ok(new { data = p2p });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static bool GetKeyFromConfiguration(IConfiguration configuration, string key, out string value)
    {
        value = configuration[key] ?? string.Empty;
        return configuration[key] != null;
    }
}