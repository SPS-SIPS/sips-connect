using System.Globalization;
using System.Reflection;
using System.Text;
using SIPS.Emv.Enums;
namespace SIPS.Emv.Models;
public class PayloadEncoder : IPayloadEncoding<MerchantPayload>
{
    public string GeneratePayload(MerchantPayload payload)
    {
        var sb = new StringBuilder();
        sb.Append(EncodeProperty(nameof(MerchantPayload.PayloadFormatIndicator), payload.PayloadFormatIndicator));
        sb.Append(EncodeProperty(nameof(MerchantPayload.PointOfInitializationMethod), payload.PointOfInitializationMethod));

        if (null != payload.MerchantAccount)
        {
            foreach (var merchantAccountInfo in payload.MerchantAccount)
            {
                var merchantInfoBuilder = new StringBuilder();

                var property = typeof(MerchantAccount).GetProperty("GlobalUniqueIdentifier");
                if (property != null)
                {
                    merchantInfoBuilder.Append(EncodeProperty(property, merchantAccountInfo.Value.GlobalUniqueIdentifier));
                }
                foreach (var paymentNetworkItem in merchantAccountInfo.Value.PaymentNetworkSpecific)
                {
                    merchantInfoBuilder.Append(EncodeKeyPair(paymentNetworkItem.Key, paymentNetworkItem.Value));
                }

                sb.AppendFormat("{0:D2}{1:D2}{2}", merchantAccountInfo.Key, merchantInfoBuilder.Length, merchantInfoBuilder);
            }
        }

        sb.Append(EncodeProperty(nameof(MerchantPayload.MerchantCategoryCode), payload.MerchantCategoryCode));
        sb.Append(EncodeProperty(nameof(MerchantPayload.TransactionCurrency), payload.TransactionCurrency));
        sb.Append(EncodeProperty(nameof(MerchantPayload.CountyCode), payload.CountyCode));
        sb.Append(EncodeProperty(nameof(MerchantPayload.MerchantName), payload.MerchantName));
        sb.Append(EncodeProperty(nameof(MerchantPayload.MerchantCity), payload.MerchantCity));
        sb.Append(EncodeProperty(nameof(MerchantPayload.PostalCode), payload.PostalCode));

        if (null != payload.MerchantInformation)
        {
            var languageTemplateBuilder = new StringBuilder();
            languageTemplateBuilder.Append(EncodeProperty(typeof(MerchantInfoLanguageTemplate).GetProperty(nameof(MerchantInfoLanguageTemplate.LanguagePreference)), payload.MerchantInformation.LanguagePreference));
            languageTemplateBuilder.Append(EncodeProperty(typeof(MerchantInfoLanguageTemplate).GetProperty(nameof(MerchantInfoLanguageTemplate.MerchantNameAlternateLanguage)), payload.MerchantInformation.MerchantNameAlternateLanguage));
            languageTemplateBuilder.Append(EncodeProperty(typeof(MerchantInfoLanguageTemplate).GetProperty(nameof(MerchantInfoLanguageTemplate.MerchantCityAlternateLanguage)), payload.MerchantInformation.MerchantCityAlternateLanguage));

            sb.Append(EncodeProperty(nameof(MerchantPayload.MerchantInformation), languageTemplateBuilder.ToString()));
        }

        sb.Append(EncodeProperty(nameof(MerchantPayload.TransactionAmount), payload.TransactionAmount));
        sb.Append(EncodeProperty(nameof(MerchantPayload.TipOrConvenienceIndicator), payload.TipOrConvenienceIndicator));
        sb.Append(EncodeProperty(nameof(MerchantPayload.ValueOfConvenienceFeeFixed), payload.ValueOfConvenienceFeeFixed));
        sb.Append(EncodeProperty(nameof(MerchantPayload.ValueOfConvenienceFeePercentage), payload.ValueOfConvenienceFeePercentage));

        if (null != payload.AdditionalData)
        {
            var additionalDataBuilder = new StringBuilder();
            additionalDataBuilder.Append(EncodeProperty(typeof(MerchantAdditionalData).GetProperty(nameof(MerchantAdditionalData.BillNumber)), payload.AdditionalData.BillNumber));
            additionalDataBuilder.Append(EncodeProperty(typeof(MerchantAdditionalData).GetProperty(nameof(MerchantAdditionalData.MobileNumber)), payload.AdditionalData.MobileNumber));
            additionalDataBuilder.Append(EncodeProperty(typeof(MerchantAdditionalData).GetProperty(nameof(MerchantAdditionalData.StoreLabel)), payload.AdditionalData.StoreLabel));
            additionalDataBuilder.Append(EncodeProperty(typeof(MerchantAdditionalData).GetProperty(nameof(MerchantAdditionalData.LoyaltyNumber)), payload.AdditionalData.LoyaltyNumber));
            additionalDataBuilder.Append(EncodeProperty(typeof(MerchantAdditionalData).GetProperty(nameof(MerchantAdditionalData.ReferenceLabel)), payload.AdditionalData.ReferenceLabel));
            additionalDataBuilder.Append(EncodeProperty(typeof(MerchantAdditionalData).GetProperty(nameof(MerchantAdditionalData.CustomerLabel)), payload.AdditionalData.CustomerLabel));
            additionalDataBuilder.Append(EncodeProperty(typeof(MerchantAdditionalData).GetProperty(nameof(MerchantAdditionalData.TerminalLabel)), payload.AdditionalData.TerminalLabel));
            additionalDataBuilder.Append(EncodeProperty(typeof(MerchantAdditionalData).GetProperty(nameof(MerchantAdditionalData.PurposeOfTransaction)), payload.AdditionalData.PurposeOfTransaction));
            additionalDataBuilder.Append(EncodeProperty(typeof(MerchantAdditionalData).GetProperty(nameof(MerchantAdditionalData.AdditionalConsumerDataRequest)), payload.AdditionalData.AdditionalConsumerDataRequest));

            sb.Append(EncodeProperty(nameof(MerchantPayload.AdditionalData), additionalDataBuilder.ToString()));
        }

        if (null != payload.UnreservedTemplate)
        {
            foreach (var unreservedTemplateItem in payload.UnreservedTemplate)
            {
                var merchantInfoBuilder = new StringBuilder();

                merchantInfoBuilder.Append(EncodeProperty(typeof(MerchantAccount).GetProperty("GlobalUniqueIdentifier"), unreservedTemplateItem.Value.GlobalUniqueIdentifier));
                foreach (var dataItem in unreservedTemplateItem.Value.ContextSpecificData)
                {
                    merchantInfoBuilder.Append(EncodeKeyPair(dataItem.Key, dataItem.Value));
                }

                sb.AppendFormat("{0:D2}{1:D2}{2}", unreservedTemplateItem.Key, merchantInfoBuilder.Length, merchantInfoBuilder);
            }
        }

        sb.Append("6304");
        var crc16ccittFalseParameters = CrcStandardParams.StandardParameters[CrcAlgorithms.Crc16CcittFalse];
        var crc = new Crc(crc16ccittFalseParameters).ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        sb.Append(crc.ToHex(true).GetLast(4));

        return sb.ToString();
    }

    public string GeneratePayload(P2PPayload payload)
    {
        var sb = new StringBuilder();
        sb.Append(EncodePropertyP2P(nameof(P2PPayload.PayloadFormatIndicator), payload.PayloadFormatIndicator));
        sb.Append(EncodePropertyP2P(nameof(P2PPayload.PointOfInitializationMethod), payload.PointOfInitializationMethod));
        sb.Append(EncodePropertyP2P(nameof(P2PPayload.SchemeIdentifier), payload.SchemeIdentifier, true));
        sb.Append(EncodePropertyP2P(nameof(P2PPayload.FiName), payload.FiName));
        sb.Append(EncodePropertyP2P(nameof(P2PPayload.AccountNumber), payload.AccountNumber));
        sb.Append(EncodePropertyP2P(nameof(P2PPayload.AccountName), payload.AccountName));
        if (payload.Amount > 0)
        {
            sb.Append(EncodePropertyP2P(nameof(P2PPayload.Amount), payload.Amount));
        }
        sb.Append(EncodePropertyP2P(nameof(P2PPayload.Particulars), payload.Particulars));

        sb.Append("1004");
        var crc16ccittFalseParameters = CrcStandardParams.StandardParameters[CrcAlgorithms.Crc16CcittFalse];
        var crc = new Crc(crc16ccittFalseParameters).ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        sb.Append(crc.ToHex(true).GetLast(4));

        return sb.ToString();
    }

    private string EncodeProperty<T>(string propertyName, T propertyValue)
    {
        var property = typeof(MerchantPayload)
            .GetProperty(propertyName);

        if (property == null)
        {
            return string.Empty;
        }

        return EncodeProperty(property, propertyValue);
    }
    private string EncodePropertyP2P<T>(string propertyName, T propertyValue, bool isP2P = false)
    {
        var property = typeof(P2PPayload)
            .GetProperty(propertyName);

        if (property == null)
        {
            return string.Empty;
        }

        return EncodeProperty(property, propertyValue, isP2P);
    }

    private string EncodeProperty<T>(PropertyInfo? property, T propertyValue, bool isP2P = false)
    {
        if (property == null)
        {
            return string.Empty;
        }
        var emvSpecAttribute = (EmvSpecificationAttribute)property
            .GetCustomAttributes(typeof(EmvSpecificationAttribute), false)
            .First();

        string value = EncodePropertyValue(propertyValue);

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string id = emvSpecAttribute.Id.ToString("D2");
        string length = value.Length.ToString("D2");

        if (property.Name == "SchemeIdentifier" && isP2P && id == "02")
        {
            return $"{id}27{length}{value}";
        }

        return $"{id}{length}{value}";
    }

    private string EncodeKeyPair(int id, string value) => string.Format(CultureInfo.InvariantCulture, "{0:D2}{1:D2}{2}", id, value.Length, value);

    private string EncodePropertyValue<T>(T propertyValue)
    {
        // Value can be  int, int?, decimal?, string or null
        if (typeof(T) == typeof(int))
        {
            if (propertyValue == null)
            {
                return string.Empty;
            }
            var intValue = (int)(object)propertyValue;
            return intValue.ToString("D2");
        }
        else if (propertyValue is int?)
        {
            var nullableInt = propertyValue as int?;
            if (nullableInt.HasValue)
            {
                return nullableInt.Value.ToString("D2");
            }
            else
            {
                return string.Empty;
            }
        }
        else if (propertyValue is decimal?)
        {
            var nullableDecimal = propertyValue as decimal?;
            if (nullableDecimal.HasValue)
            {
                return nullableDecimal.Value.ToString("#.00");
            }
            else
            {
                return string.Empty;
            }
        }
        else if (propertyValue is string)
        {
            return propertyValue.ToString() ?? "";
        }
        else if (propertyValue == null)
        {
            return string.Empty;
        }
        else
        {
            return string.Empty;
        }
    }
}