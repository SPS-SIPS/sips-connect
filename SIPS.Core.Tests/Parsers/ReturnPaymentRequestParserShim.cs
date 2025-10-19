using System;
using System.Linq;
using System.Xml.Linq;
using SIPS.Core.Services.ISOParsers;
using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Tests.Parsers;

public sealed class ReturnPaymentRequestParserShim : IReturnPaymentRequestParser
{
    public bool TryParse(string xml, out ReturnPaymentRequestBuilder.Request request)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            string Find(string local) => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == local)?.Value ?? string.Empty;
            var from = Find("From");
            var to = Find("To");
            var msgId = Find("MsgId");
            var biz = Find("BizMsgIdr");
            var def = Find("MsgDefIdr");
            var cre = Find("CreDtTm");
            if (string.IsNullOrEmpty(cre)) cre = Find("CreDt");
            DateTime.TryParse(cre, out var creDt);
            var orgnlTxId = Find("OrgnlTxId");
            var e2e = Find("OriginalEndToEnd");
            var retId = Find("ReturnId");

            request = new ReturnPaymentRequestBuilder.Request
            {
                From = string.IsNullOrEmpty(from) ? "BICA" : from,
                To = string.IsNullOrEmpty(to) ? "BICB" : to,
                MsgDefIdr = string.IsNullOrEmpty(def) ? "PACS004" : def,
                BizMsgIdr = string.IsNullOrEmpty(biz) ? "BIZ" : biz,
                MsgId = string.IsNullOrEmpty(msgId) ? "MSG" : msgId,
                CreDt = creDt == default ? DateTime.UtcNow : creDt,
                OrgnlTxId = string.IsNullOrEmpty(orgnlTxId) ? "TX1" : orgnlTxId,
                OriginalEndToEnd = string.IsNullOrEmpty(e2e) ? "E2E1" : e2e,
                ReturnId = string.IsNullOrEmpty(retId) ? "RET1" : retId,
                ReturnReason = string.IsNullOrEmpty(Find("Reason")) ? "BE05" : Find("Reason"),
                AdditionalInfo = Find("AdditionalInfo"),
                OriginalAmount = 1m,
                OriginalCurrency = "ZAR",
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
