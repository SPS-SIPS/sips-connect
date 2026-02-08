
namespace SIPS.Emv.Helpers;
public static class Coders
{
    public static void DecodeAccountInformation(ICollection<Tlv> tlvs, MerchantPayload merchantPayload)
    {
        var merchantAccountInfoTlvs = tlvs.Where(e => e.Tag >= 2 && e.Tag <= 51);
        if (merchantAccountInfoTlvs.Any())
        {
            merchantPayload.MerchantAccount = [];
            foreach (var tlv in merchantAccountInfoTlvs)
            {
                var accountInfo = new MerchantAccount();

                if (!tlv.ChildNodes.Any())
                {
                    accountInfo.GlobalUniqueIdentifier = tlv.Value;
                }
                else
                {
                    var globalUniqueIdentifierTlv = tlv.ChildNodes.FirstOrDefault(t => t.Tag == 0);
                    if (null != globalUniqueIdentifierTlv)
                    {
                        accountInfo.GlobalUniqueIdentifier = globalUniqueIdentifierTlv.Value;

                        var paymentNetworkSpecificTlvs = tlv.ChildNodes.Where(e => e.Tag >= 1 && e.Tag <= 99);
                        if (paymentNetworkSpecificTlvs.Any())
                        {
                            accountInfo.PaymentNetworkSpecific = new Dictionary<int, string>();
                            foreach (var item in paymentNetworkSpecificTlvs)
                            {
                                accountInfo.PaymentNetworkSpecific.Add(item.Tag, item.Value);
                            }
                        }
                    }
                }

                merchantPayload.MerchantAccount.Add(tlv.Tag, accountInfo);
            }
        }
    }

    public static void DecodeUnreservedTemplate(ICollection<Tlv> tlvs, MerchantPayload merchantPayload)
    {
        var unreservedTemplateTlvs = tlvs.Where(e => e.Tag >= 80 && e.Tag <= 99);
        if (unreservedTemplateTlvs.Any())
        {
            merchantPayload.UnreservedTemplate = [];
            foreach (var tlv in unreservedTemplateTlvs)
            {
                var unreservedTemplate = new MerchantUnreservedTemplate();
                var globalUniqueIdentifierTlv = tlv.ChildNodes.FirstOrDefault(t => t.Tag == 0);
                if (null != globalUniqueIdentifierTlv)
                {
                    unreservedTemplate.GlobalUniqueIdentifier = globalUniqueIdentifierTlv.Value;

                    var contextSpecificTlvs = tlv.ChildNodes.Where(e => e.Tag >= 1 && e.Tag <= 99);
                    if (contextSpecificTlvs.Any())
                    {
                        unreservedTemplate.ContextSpecificData = [];
                        foreach (var item in contextSpecificTlvs)
                        {
                            unreservedTemplate.ContextSpecificData.Add(item.Tag, item.Value);
                        }
                    }

                    merchantPayload.UnreservedTemplate.Add(tlv.Tag, unreservedTemplate);
                }
            }
        }
    }

}