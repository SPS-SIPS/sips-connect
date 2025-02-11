using SIPS.ISO20022.Schemas.PSRequest;
using SIPS.ISO20022.Schemas.PSHeader;
using SIPS.ISO20022.Schemas.PSDocument;

namespace SIPS.ISO20022.Helpers;

public static class PaymentStatusRequestBuilder
{
    public class Request : IMessage
    {
        public string From { get; set; } = default!;
        public string To { get; set; } = default!;
        public string MsgDefIdr { get; set; } = default!;
        public string BizMsgIdr { get; set; } = default!;
        public DateTime CreDt { get; set; }
        public string MsgId { get; set; } = string.Empty;
        public string OriginalEndToEnd { get; set; } = string.Empty;
        public string OrgnlTxId { get; set; } = string.Empty;
    }
    private static AppHdr AppHeader(string from, string to, SupportedMessageTypes type)
    {
        AppHdr hdr = new()
        {
            Fr = new Party44Choice
            {
                FIId = new Schemas.PSHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.PSHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.PSHeader.GenericFinancialIdentification1
                        {
                            Id = from
                        }
                    }
                }
            },
            To = new Party44Choice
            {
                FIId = new Schemas.PSHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.PSHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.PSHeader.GenericFinancialIdentification1
                        {
                            Id = to
                        }
                    }
                }
            },
            BizMsgIdr = Transformers.GenerateId(from),
            MsgDefIdr = type.Id,
            CreDt = DateTime.UtcNow,
        };
        return hdr;
    }

    public static string Build(Request request)
    {
        var messageType = SupportedMessageTypes.CreditTransferStatusRequest;

        var appHdr = AppHeader(request.From, request.To, messageType);

        var document = new Document
        {
            FIToFIPmtStsReq = new FIToFIPaymentStatusRequestV05
            {
                GrpHdr = new GroupHeader91
                {
                    MsgId = request.MsgId,
                    CreDtTm = request.CreDt,
                    InstgAgt = new Schemas.PSDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PSDocument.FinancialInstitutionIdentification18
                        {
                            BICFI = request.From,

                        }
                    },
                    InstdAgt = new Schemas.PSDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PSDocument.FinancialInstitutionIdentification18
                        {
                            BICFI = request.To
                        }
                    },
                },
                TxInf =
                [
                    new PaymentTransaction131
                    {
                        OrgnlEndToEndId = request.OriginalEndToEnd,
                        OrgnlTxId = request.OrgnlTxId,
                    }
                ],
            }
        };
        var envelope = new FPEnvelope
        {
            AppHdr = appHdr,
            Document = document
        };
        return Transformers.GeneratePrefixedXml(envelope.Untyped, docNS: messageType.GroupId);
    }
    public static Request Parse(string content)
    {
        var envelope = FPEnvelope.Parse(content);
        var document = envelope.Document;

        return new Request
        {
            From = document.FIToFIPmtStsReq.GrpHdr?.InstdAgt?.FinInstnId?.BICFI ?? "",
            To = document.FIToFIPmtStsReq.GrpHdr?.InstgAgt?.FinInstnId?.BICFI ?? "",
            MsgId = document.FIToFIPmtStsReq?.GrpHdr?.MsgId ?? "",
            CreDt = document.FIToFIPmtStsReq?.GrpHdr?.CreDtTm ?? DateTime.UtcNow,
            MsgDefIdr = envelope.AppHdr?.MsgDefIdr ?? "",
            BizMsgIdr = envelope.AppHdr?.BizMsgIdr ?? "",
            OriginalEndToEnd = document?.FIToFIPmtStsReq?.TxInf.FirstOrDefault()?.OrgnlEndToEndId ?? "",
            OrgnlTxId = document?.FIToFIPmtStsReq?.TxInf.FirstOrDefault()?.OrgnlTxId ?? "",
        };
    }
}
