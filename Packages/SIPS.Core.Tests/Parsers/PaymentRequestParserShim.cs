using System;
using System.Linq;
using System.Xml.Linq;
using SIPS.Core.Services.ISOParsers;
using SIPS.ISO20022.Helpers;

namespace SIPS.Core.Tests.Parsers;

public sealed class PaymentRequestParserShim : IPaymentRequestParser
{
    public bool TryParse(string message, out PaymentRequestBuilder.Request request)
    {
        try
        {
            var doc = XDocument.Parse(message);
            string Find(string local) => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == local)?.Value ?? string.Empty;

            var from = Find("From");
            var to = Find("To");
            var msgId = Find("MsgId");
            var biz = Find("BizMsgIdr");
            var def = Find("MsgDefIdr");
            var e2e = Find("EndToEndId");
            var tx = Find("TxId");
            var cre = Find("CreDtTm");
            if (string.IsNullOrEmpty(cre)) cre = Find("CreDt");
            DateTime.TryParse(cre, out var creDt);

            var amtStr = Find("InstdAmt");
            decimal.TryParse(amtStr, out var amt);
            var ccy = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "InstdAmt")?.Attribute("Ccy")?.Value ?? Find("Currency");

            request = new PaymentRequestBuilder.Request
            {
                From = string.IsNullOrEmpty(from) ? "BICA" : from,
                To = string.IsNullOrEmpty(to) ? "BICB" : to,
                MsgDefIdr = string.IsNullOrEmpty(def) ? "CT" : def,
                BizMsgIdr = string.IsNullOrEmpty(biz) ? "BIZ" : biz,
                MsgId = string.IsNullOrEmpty(msgId) ? "MSG" : msgId,
                CreDt = creDt == default ? DateTime.UtcNow : creDt,
                EndToEndId = string.IsNullOrEmpty(e2e) ? "E2E" : e2e,
                TxId = string.IsNullOrEmpty(tx) ? "TX" : tx,
                Amount = amt == default ? 1 : amt,
                Currency = string.IsNullOrEmpty(ccy) ? "USD" : ccy,
                Debtor = new SIPS.ISO20022.Models.Person {
                    Name = string.IsNullOrEmpty(Find("DbtrNm")) ? "Alice" : Find("DbtrNm"),
                    Account = string.IsNullOrEmpty(Find("DbtrAcctId")) ? "A1" : Find("DbtrAcctId"),
                    AccountType = string.IsNullOrEmpty(Find("DbtrAcctTp")) ? "CHK" : Find("DbtrAcctTp")
                },
                Creditor = new SIPS.ISO20022.Models.Person {
                    Name = string.IsNullOrEmpty(Find("CdtrNm")) ? "Bob" : Find("CdtrNm"),
                    Account = string.IsNullOrEmpty(Find("CdtrAcctId")) ? "B1" : Find("CdtrAcctId"),
                    AccountType = string.IsNullOrEmpty(Find("CdtrAcctTp")) ? "SAV" : Find("CdtrAcctTp")
                },
                LocalInstrument = Find("LclInstrm"),
                CategoryPurpose = Find("CtgyPurp"),
                Ustrd = Find("Ustrd")
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
