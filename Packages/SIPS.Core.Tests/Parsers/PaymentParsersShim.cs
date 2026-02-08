using System;
using System.Linq;
using System.Xml.Linq;
using SIPS.Core.Services.ISOParsers;
using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Tests.Parsers;

public sealed class PaymentStatusRequestParserShim : IPaymentStatusRequestParser
{
    public bool TryParse(string message, out PaymentStatusRequestBuilder.Request request)
    {
        try
        {
            var doc = XDocument.Parse(message);
            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            string Find(string local)
            {
                return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == local)?.Value ?? string.Empty;
            }
            var from = Find("From");
            var to = Find("To");
            var msgId = Find("MsgId");
            var biz = Find("OrgnlMsgId");
            var def = Find("OrgnlMsgNmId");
            var e2e = Find("OrgnlEndToEndId");
            var tx = Find("OrgnlTxId");
            var cre = Find("CreDtTm");
            if (string.IsNullOrEmpty(cre)) cre = Find("CreDt");
            DateTime.TryParse(cre, out var creDt);

            request = new PaymentStatusRequestBuilder.Request
            {
                From = string.IsNullOrEmpty(from) ? "BICA" : from,
                To = string.IsNullOrEmpty(to) ? "BICB" : to,
                MsgDefIdr = string.IsNullOrEmpty(def) ? "CTST" : def,
                BizMsgIdr = string.IsNullOrEmpty(biz) ? "BIZ" : biz,
                MsgId = string.IsNullOrEmpty(msgId) ? "MSG" : msgId,
                CreDt = creDt == default ? DateTime.UtcNow : creDt,
                OriginalEndToEnd = string.IsNullOrEmpty(e2e) ? "E2E1234" : e2e,
                OrgnlTxId = string.IsNullOrEmpty(tx) ? "TX1234" : tx,
            };
            return true;
        }
        catch
        {
            request = null!;
            return false;
        }
    }
}
