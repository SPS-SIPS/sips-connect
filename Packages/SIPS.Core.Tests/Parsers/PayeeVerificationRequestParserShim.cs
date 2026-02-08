using System;
using System.Linq;
using System.Xml.Linq;
using SIPS.Core.Services.ISOParsers;
using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Tests.Parsers;

public sealed class PayeeVerificationRequestParserShim : IPayeeVerificationRequestParser
{
    public bool TryParse(string xml, out PayeeVerificationBuilder.Request request)
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

            request = new PayeeVerificationBuilder.Request
            {
                From = string.IsNullOrEmpty(from) ? "BICA" : from,
                To = string.IsNullOrEmpty(to) ? "BICB" : to,
                MsgDefIdr = string.IsNullOrEmpty(def) ? "ACMT" : def,
                BizMsgIdr = string.IsNullOrEmpty(biz) ? "BIZ" : biz,
                MsgId = string.IsNullOrEmpty(msgId) ? "MSG" : msgId,
                CreDt = creDt == default ? DateTime.UtcNow : creDt,
                Alias = string.IsNullOrEmpty(Find("Alias")) ? "ALIAS1" : Find("Alias"),
                Type = string.IsNullOrEmpty(Find("Type")) ? "MSISDN" : Find("Type"),
                SIPSRequestId = string.IsNullOrEmpty(Find("VerificationId")) ? "VRID1" : Find("VerificationId"),
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
