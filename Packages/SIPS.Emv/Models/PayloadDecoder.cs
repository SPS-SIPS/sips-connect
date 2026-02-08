using System.Reflection;
using System.Security;
using SIPS.Emv.Enums;
using static SIPS.Emv.Helpers.Coders;
namespace SIPS.Emv.Models;

public class PayloadDecoder : IPayloadDecoder<MerchantPayload>
{
    private static readonly int[] _parentTagsIdentifiers =
    [
            // Merchant Account Information
            02, 03, 04, 05, 06, 07, 08, 09, 10,
            11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
            21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
            31, 32, 33, 34, 35, 36, 37, 38, 39, 40,
            41, 42, 43, 44, 45, 46, 47, 48, 49, 50,
            51,
            // Additional Data Template
            62,
            // Language Template
            64,
            // Unreserved Template
            80, 81, 82, 83, 84, 85, 86, 87, 88, 89,
            90, 91, 92, 93, 94, 95, 96, 97, 98, 99,
    ];

    private static readonly string[] _schemaIds = "26,27,28,30,32,51".Split(',');


    public MerchantPayload BuildPayload(ICollection<Tlv> tlvs)
    {
        var merchantPayload = new MerchantPayload
        {
            AdditionalData = new MerchantAdditionalData(),
            MerchantInformation = new MerchantInfoLanguageTemplate(),
        };

        var properties = typeof(MerchantPayload).GetProperties();
        foreach (var property in properties)
        {
            ReflectAndBind(merchantPayload, tlvs, property);
        }

        DecodeAccountInformation(tlvs, merchantPayload);
        DecodeUnreservedTemplate(tlvs, merchantPayload);

        if (null == merchantPayload.AdditionalData.AdditionalConsumerDataRequest
            && null == merchantPayload.AdditionalData.BillNumber
            && null == merchantPayload.AdditionalData.CustomerLabel
            && null == merchantPayload.AdditionalData.LoyaltyNumber
            && null == merchantPayload.AdditionalData.MobileNumber
            && null == merchantPayload.AdditionalData.PurposeOfTransaction
            && null == merchantPayload.AdditionalData.ReferenceLabel
            && null == merchantPayload.AdditionalData.StoreLabel
            && null == merchantPayload.AdditionalData.TerminalLabel)
        {
            merchantPayload.AdditionalData = null;
        }

        if (null == merchantPayload.MerchantInformation.LanguagePreference
            && null == merchantPayload.MerchantInformation.MerchantCityAlternateLanguage
            && null == merchantPayload.MerchantInformation.MerchantNameAlternateLanguage)
        {
            merchantPayload.MerchantInformation = null;
        }

        return merchantPayload;
    }
    public P2PPayload BuildPayloadP2P(ICollection<Tlv> tlvs)
    {
        var p2p = new P2PPayload();

        var properties = typeof(P2PPayload).GetProperties();
        foreach (var property in properties)
        {
            ReflectAndBind(p2p, tlvs, property);
        }

        return p2p;
    }

    public string ValidateCrc(string qrData)
    {
        var data = qrData[..^4];
        var crc = new Crc(CrcStandardParams.StandardParameters[CrcAlgorithms.Crc16CcittFalse]).ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        var crcValue = crc.ToHex(true).GetLast(4);
        var qrDataCrc = qrData.GetLast(4);

        if (0 != StringComparer.Ordinal.Compare(crcValue, qrDataCrc.ToUpperInvariant()))
        {
            throw new SecurityException("QR data has an invalid CRC: " + crcValue);
        }

        return crcValue;
    }

    public ICollection<Tlv> DecodeQR(string qrData, bool containsChildren, bool isP2P = false)
    {
        var collection = new List<Tlv>();

        /// Remove CRC
        var data = qrData[..^8];

        // Parse root nodes
        ParseTLVs(data, collection, isP2P);

        // Parse Child Nodes
        if (containsChildren)
        {
            var allowedParentNodes = collection.Where(e => _parentTagsIdentifiers.Contains(e.Tag));
            foreach (var item in allowedParentNodes)
            {
                ParseTLVs(item.Value, item.ChildNodes);
            }
        }

        return collection.AsReadOnly();
    }

    private static void ParseTLVs(string data, ICollection<Tlv> collection, bool isP2P = false)
    {
        for (int index = 0; index < data.Length; index++)
        {
            var tag = data.Substring(index, 2);
            index += 2;

            if (isP2P && tag == "02")
            {
                var schemaId = data.Substring(index, 2);
                if (_schemaIds.Contains(schemaId))
                {
                    collection.Add(new Tlv("11", 2, schemaId));
                    index += 2;
                }
            }

            var schemaLength = data.Substring(index, 2);
            if (!int.TryParse(schemaLength, out int length))
            {
                throw new InvalidOperationException("Failed to decode the QR code");
            }
            index += 2;

            if (data.Length - 4 < length)
            {
                break;
            }

            var value = data.Substring(index, length);
            index += length - 1;

            collection.Add(new Tlv(tag, length, value));
        }
    }

    private void ReflectAndBind<T>(T instance, ICollection<Tlv> collection, PropertyInfo property)
    {
        var customAttribute = property
                            .GetCustomAttributes(typeof(EmvSpecificationAttribute), false)
                            .FirstOrDefault();
        if (customAttribute != null && customAttribute is EmvSpecificationAttribute emvSpecification)
        {
            var tlv = collection.FirstOrDefault(e => e.Tag == emvSpecification.Id);
            if (null != tlv)
            {
                if (!emvSpecification.IsParent)
                {
                    property.SetAndCastValue(instance!, tlv.Value);
                }
                else
                {
                    var properties = property.PropertyType.GetProperties();
                    var itemInstance = property.GetValue(instance);

                    foreach (var childProperty in properties)
                    {
                        ReflectAndBind(itemInstance, tlv.ChildNodes, childProperty);
                    }
                }
            }
        }
    }


}