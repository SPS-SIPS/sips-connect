using System.Xml.Linq;
using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Services.ISOParsers;

public interface IPaymentStatusReportParser
{
    bool TryParse(string xml, out PaymentRequestResponseBuilder.Response? request);
}

public sealed class PaymentStatusReportParser : IPaymentStatusReportParser
{
    public bool TryParse(string xml, out PaymentRequestResponseBuilder.Response? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try
        {
            if (xml.Contains("urn:iso:std:iso:20022:tech:xsd:returnPayment_response"))
            {
                var rpt = ReturnPaymentResponseBuilder.Parse(xml);
                if (rpt != null)
                {
                    request = new PaymentRequestResponseBuilder.Response
                    {
                        From = rpt.From,
                        To = rpt.To,
                        BizMsgIdr = rpt.BizMsgIdr,
                        MsgDefIdr = rpt.MsgDefIdr,
                        CreDt = rpt.CreDt,
                        MsgId = rpt.MsgId,
                        AcceptanceDate = rpt.AcceptanceDate,
                        TxId = rpt.TxId,
                        Status = rpt.Status,
                        Reason = rpt.Reason,
                        AdditionalInfo = rpt.AdditionalInfo,
                        Original = new PaymentRequestBuilder.Request
                        {
                            MsgId = rpt.Original.MsgId,
                            BizMsgIdr = rpt.Original.BizMsgIdr,
                            MsgDefIdr = rpt.Original.MsgDefIdr,
                            CreDt = rpt.Original.CreDt,
                            TxId = rpt.Original.OrgnlTxId,
                            EndToEndId = rpt.Original.OriginalEndToEnd,
                            Amount = rpt.Original.OriginalAmount,
                            Currency = rpt.Original.OriginalCurrency
                        },
                        Role = rpt.Status == "ACSC" ? SIPS.ISO20022.Enums.Pacs002Role.CompletionNotify : SIPS.ISO20022.Enums.Pacs002Role.StatusUpdate
                    };
                    return true;
                }
            }

            var parsed = PaymentRequestResponseBuilder.Parse(xml);
            if (parsed == null) return false;

            request = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

}
