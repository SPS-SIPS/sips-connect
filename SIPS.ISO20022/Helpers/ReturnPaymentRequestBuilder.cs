using SIPS.ISO20022.Schemas.RPRequest;
using SIPS.ISO20022.Schemas.RPHeader;
using SIPS.ISO20022.Schemas.RPDocument;

namespace SIPS.ISO20022.Helpers;

public static class ReturnPaymentRequestBuilder
{
    public class Request : IMessage
    {
        public string From { get; set; } = default!;
        public string To { get; set; } = default!;
        public string MsgDefIdr { get; set; } = default!;
        public string BizMsgIdr { get; set; } = default!;
        public DateTime CreDt { get; set; }
        public string MsgId { get; set; } = string.Empty;
        // body
        public int NumberOfTransactions { get; set; }
        public SettlementMethod1Code SettlementMethod { get; set; } = SettlementMethod1Code.CLRG;
        public string ClearingSystem { get; set; } = "FP";
        public string LocalInstrument { get; set; } = string.Empty;
        public string CategoryPurpose { get; set; } = string.Empty;
        public string ReturnId { get; set; } = string.Empty;
        public string OriginalEndToEnd { get; set; } = string.Empty;
        public string OrgnlTxId { get; set; } = string.Empty;
        public string OriginalCurrency { get; set; } = "USD";
        public decimal OriginalAmount { get; set; }
        public string ReturnReason { get; set; } = string.Empty;
        public string AdditionalInfo { get; set; } = string.Empty;

    }
    private static AppHdr AppHeader(string from, string to, SupportedMessageTypes type, string bizMsgIdr)
    {
        AppHdr hdr = new()
        {
            Fr = new Party44Choice
            {
                FIId = new Schemas.RPHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.RPHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.RPHeader.GenericFinancialIdentification1
                        {
                            Id = from
                        }
                    }
                }
            },
            To = new Party44Choice
            {
                FIId = new Schemas.RPHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.RPHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.RPHeader.GenericFinancialIdentification1
                        {
                            Id = to
                        }
                    }
                }
            },
            BizMsgIdr = bizMsgIdr,
            MsgDefIdr = type.Id,
            CreDt = DateTime.UtcNow,
        };
        return hdr;
    }

    public static (string document, string bizMsgIdr, string type, string msgId) Build(Request request)
    {
        var messageType = SupportedMessageTypes.CreditTransferStatusRequest;
        var bizMsgIdr = Transformers.GenerateId(request.From);
        var msgId = Transformers.GenerateId(request.From);
        var appHdr = AppHeader(request.From, request.To, messageType, bizMsgIdr);

        var document = new Document
        {
            PmtRtr = new PaymentReturnV11
            {
                GrpHdr = new GroupHeader99
                {
                    MsgId = msgId,
                    CreDtTm = request.CreDt,
                    NbOfTxs = request.NumberOfTransactions.ToString(),
                    SttlmInf = new SettlementInstruction11
                    {
                        SttlmMtd = request.SettlementMethod,
                        ClrSys = new ClearingSystemIdentification3Choice
                        {
                            Prtry = request.ClearingSystem
                        },
                    },
                    PmtTpInf = new PaymentTypeInformation28
                    {
                        LclInstrm = new LocalInstrument2Choice
                        {
                            Prtry = request.LocalInstrument
                        },
                        CtgyPurp = new CategoryPurpose1Choice
                        {
                            Prtry = request.CategoryPurpose
                        }
                    },
                    InstgAgt = new Schemas.RPDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.RPDocument.FinancialInstitutionIdentification18
                        {
                            BICFI = request.From
                        }
                    },
                    InstdAgt = new Schemas.RPDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.RPDocument.FinancialInstitutionIdentification18
                        {
                            BICFI = request.To
                        }
                    },
                },
                TxInf =
                [
                    new (){
                        RtrId = request.ReturnId,
                        OrgnlEndToEndId = request.OriginalEndToEnd,
                        OrgnlTxId = request.OrgnlTxId,
                        RtrdIntrBkSttlmAmt = new ActiveCurrencyAndAmount
                        {
                            Ccy = request.OriginalCurrency,
                            TypedValue = request.OriginalAmount
                        },
                        RtrRsnInf = [
                            new (){
                                Rsn = new ReturnReason5Choice(){
                                    Prtry = request.ReturnReason
                                },
                                AddtlInf = [request.AdditionalInfo]
                            }
                        ]
                    }
                ],
            }
        };
        var envelope = new FPEnvelope
        {
            AppHdr = appHdr,
            Document = document
        };
        return (Transformers.GeneratePrefixedXml(envelope.Untyped, docNS: messageType.GroupId), bizMsgIdr, messageType.Id, msgId);
    }
    public static Request Parse(string content)
    {
        var envelope = FPEnvelope.Parse(content);
        var document = envelope.Document;

        return new Request
        {
            MsgDefIdr = envelope.AppHdr?.MsgDefIdr ?? "",
            BizMsgIdr = envelope.AppHdr?.BizMsgIdr ?? "",

            MsgId = document.PmtRtr?.GrpHdr?.MsgId ?? "",
            CreDt = document.PmtRtr?.GrpHdr?.CreDtTm ?? DateTime.UtcNow,
            NumberOfTransactions = int.Parse(document?.PmtRtr?.GrpHdr?.NbOfTxs ?? "0"),
            SettlementMethod = document?.PmtRtr?.GrpHdr?.SttlmInf?.SttlmMtd ?? SettlementMethod1Code.CLRG,
            ClearingSystem = document?.PmtRtr?.GrpHdr?.SttlmInf?.ClrSys?.Prtry ?? "FP",
            LocalInstrument = document?.PmtRtr?.GrpHdr?.PmtTpInf?.LclInstrm?.Prtry ?? "",
            CategoryPurpose = document?.PmtRtr?.GrpHdr?.PmtTpInf?.CtgyPurp?.Prtry ?? "",
            From = document.PmtRtr?.GrpHdr?.InstgAgt?.FinInstnId?.Othr.Id ?? "",
            To = document.PmtRtr?.GrpHdr?.InstgAgt?.FinInstnId?.Othr.Id ?? "",

            ReturnId = document?.PmtRtr?.TxInf.FirstOrDefault()?.RtrId ?? "",
            OriginalEndToEnd = document?.PmtRtr?.TxInf.FirstOrDefault()?.OrgnlEndToEndId ?? "",
            OrgnlTxId = document?.PmtRtr?.TxInf.FirstOrDefault()?.OrgnlTxId ?? "",
            OriginalCurrency = document?.PmtRtr?.TxInf.FirstOrDefault()?.RtrdIntrBkSttlmAmt?.Ccy ?? "",
            OriginalAmount = document?.PmtRtr?.TxInf.FirstOrDefault()?.RtrdIntrBkSttlmAmt?.TypedValue ?? 0,
            ReturnReason = document?.PmtRtr?.TxInf.FirstOrDefault()?.RtrRsnInf.FirstOrDefault()?.Rsn?.Prtry ?? "",
            AdditionalInfo = document?.PmtRtr?.TxInf.FirstOrDefault()?.RtrRsnInf.FirstOrDefault()?.AddtlInf.FirstOrDefault() ?? "",
        };
    }
}
