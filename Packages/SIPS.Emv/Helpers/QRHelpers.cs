using System.ComponentModel.DataAnnotations;
namespace SIPS.Emv.Helpers;
public static class QRHelpers
{
    /// <summary>
    /// Decodes QR data into a <see cref="MerchantPayload"/> instance.
    /// </summary>
    /// <param name="data">The qr data.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="data"/> is <c>null</c> or an empty string.</exception>
    /// <exception cref="System.Security.SecurityException">If the CRC of the QR is invalid.</exception>
    /// <exception cref="ValidationException">If the payload is invalid.</exception>
    public static MerchantPayload ParseQR(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new ArgumentNullException(nameof(data));
        }

        var merchantDecoder = new PayloadDecoder();
        var crc = merchantDecoder.ValidateCrc(data);
        var tlvs = merchantDecoder.DecodeQR(data, true);
        var payload = merchantDecoder.BuildPayload(tlvs);
        payload.CRC = crc;

        var validationResults = new System.Collections.Generic.List<ValidationResult>();
        var validationContext = new ValidationContext(payload);
        if (!Validator.TryValidateObject(payload, validationContext, validationResults, true))
        {
            var errors = string.Join("; ", validationResults.Select(r => r.ErrorMessage));
            throw new ValidationException($"Merchant QR Payload validation failed: {errors}");
        }
        return payload;
    }

    public static P2PPayload ParseP2PQR(string data, bool containsChildren = true)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new ArgumentNullException(nameof(data));
        }

        var p2pDecoder = new PayloadDecoder();
        var crc = p2pDecoder.ValidateCrc(data);
        var tlvs = p2pDecoder.DecodeQR(data, containsChildren, true);
        var payload = p2pDecoder.BuildPayloadP2P(tlvs);
        payload.CRC = crc;

        var validationResults = new System.Collections.Generic.List<ValidationResult>();
        var validationContext = new ValidationContext(payload);
        if (!Validator.TryValidateObject(payload, validationContext, validationResults, true))
        {
            var errors = string.Join("; ", validationResults.Select(r => r.ErrorMessage));
            throw new ValidationException($"P2P QR Payload validation failed: {errors}");
        }
        return payload;
    }

    public static string CheckQRCodeType(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new ArgumentNullException(nameof(data));
        }

        // Check if the QR code is a P2P QR code or a Merchant QR code by using the Payload Format Indicator
        if (data.StartsWith("000201"))
        {
            return "Merchant";
        }
        if (data.StartsWith("000202"))
        {
            return "P2P";
        }
        
        // Fallback for non-standard beginnings
        if (data.Contains("so.somqr.sips"))
        {
            return "Merchant";
        }
        else
        {
            return "P2P";
        }
    }
}