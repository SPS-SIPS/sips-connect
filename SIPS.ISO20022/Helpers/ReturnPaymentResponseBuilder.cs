using SIPS.ISO20022.Schemas.RPResponse;
using SIPS.ISO20022.Schemas.RPRHeader;
using SIPS.ISO20022.Schemas.RPRDocument;
namespace SIPS.ISO20022.Helpers;

public static class ReturnPaymentResponseBuilder
{
    public sealed class Response : IMessage
    {
        public string From { get; set; } = default!;
        public string To { get; set; } = default!;
        public string MsgDefIdr { get; set; } = default!;
        public string BizMsgIdr { get; set; } = default!;
        public DateTime CreDt { get; set; }
        public string MsgId { get; set; } = string.Empty;

        // body
        public ReturnPaymentRequestBuilder.Request Original { get; set; } = new();
        public string? Status { get; set; }
        public string? Reason { get; set; }
        public string? AdditionalInfo { get; set; }
        public string TxId { get; set; } = string.Empty;
    }
    private static AppHdr AppHeader(Response model, SupportedMessageTypes type)
    {
        AppHdr hdr = new()
        {
            Fr = new Party44Choice
            {
                FIId = new Schemas.RPRHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.RPRHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.RPRHeader.GenericFinancialIdentification1
                        {
                            Id = model.From
                        }
                    }
                }
            },
            To = new Party44Choice
            {
                FIId = new Schemas.RPRHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.RPRHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.RPRHeader.GenericFinancialIdentification1
                        {
                            Id = model.To
                        }
                    }
                }
            },
            BizMsgIdr = Transformers.GenerateId(model.From),
            MsgDefIdr = type.Id,
            CreDt = DateTime.UtcNow,
            Rltd = [
                new BusinessApplicationHeader7
            {
                Fr = new Party44Choice
                {
                    FIId = new Schemas.RPRHeader.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.RPRHeader.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.RPRHeader.GenericFinancialIdentification1
                            {
                                Id = model.Original.From
                            }
                        }
                    }
                },
                To = new Party44Choice
                {
                    FIId = new Schemas.RPRHeader.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.RPRHeader.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.RPRHeader.GenericFinancialIdentification1
                            {
                                Id = model.Original.To
                            }
                        }
                    }
                },
                BizMsgIdr = model.Original.BizMsgIdr,
                MsgDefIdr = model.Original.MsgDefIdr,
                CreDt = model.Original.CreDt
            }
            ]
        };
        return hdr;
    }

    public static string Build(Response request)
    {
        var messageType = SupportedMessageTypes.CreditTransferResponse;

        // ensure minimal defaults via shared helper so XML builders do not hit minLength validation
        if (request.Original == null) request.Original = new ReturnPaymentRequestBuilder.Request();
        Defaults.EnsureReturnDefaults(request.Original);
        if (string.IsNullOrWhiteSpace(request.From)) request.From = request.Original.From;
        if (string.IsNullOrWhiteSpace(request.To)) request.To = request.Original.To;
        if (request.CreDt == default) request.CreDt = DateTime.UtcNow;

        var appHdr = AppHeader(request, messageType);

        var document = new Document
        {
            FIToFIPmtStsRpt = new FIToFIPaymentStatusReportV12
            {
                GrpHdr = new GroupHeader101
                {
                    MsgId = Transformers.GenerateId(request.From),
                    CreDtTm = request.CreDt,
                    InstgAgt = new Schemas.RPRDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.RPRDocument.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.RPRDocument.GenericFinancialIdentification1
                            {
                                Id = request.From
                            }
                        }
                    },
                    InstdAgt = new Schemas.RPRDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.RPRDocument.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.RPRDocument.GenericFinancialIdentification1
                            {
                                Id = request.To
                            }
                        }
                    }
                },
                TxInfAndSts = [
                    new PaymentTransaction130 {
                        OrgnlGrpInf = new OriginalGroupInformation29 {
                            OrgnlMsgId = request.Original.BizMsgIdr,
                            OrgnlMsgNmId = request.Original.MsgDefIdr,
                            OrgnlCreDtTm = request.Original.CreDt
                        },
                        OrgnlEndToEndId =request.Original.OriginalEndToEnd,
                        OrgnlTxId = request.Original.OrgnlTxId,
                        TxSts = request.Status,
                        OrgnlTxRef =new OriginalTransactionReference35 {
                            IntrBkSttlmAmt = new ActiveOrHistoricCurrencyAndAmount {
                                Ccy = request.Original.OriginalCurrency,
                                TypedValue = request.Original.OriginalAmount
                            },
                            Amt = new AmountType4Choice {
                                InstdAmt = new ActiveOrHistoricCurrencyAndAmount {
                                    Ccy = request.Original.OriginalCurrency,
                                    TypedValue = request.Original.OriginalAmount
                                }
                            }
                        },
                    }
                ]
            }
        };

        if (request.Status == "RJCT")
        {
            document.FIToFIPmtStsRpt.TxInfAndSts[0].StsRsnInf = [
                new StatusReasonInformation12
                {
                    Rsn = new StatusReason6Choice
                    {
                        Prtry = request.Reason ?? request.Status
                    },
                    AddtlInf = [request.AdditionalInfo ?? request.Status]
                }
            ];
        }

        var envelope = new FPEnvelope
        {
            AppHdr = appHdr,
            Document = document
        };
        return Transformers.GeneratePrefixedXml(envelope.Untyped, docNS: messageType.GroupId);
    }
    public static Response? Parse(string content)
    {
        var envelope = FPEnvelope.Parse(content);
        var document = envelope.Document;
        var rsp = new Response
        {
            BizMsgIdr = envelope.AppHdr?.BizMsgIdr ?? "",
            MsgDefIdr = envelope.AppHdr?.MsgDefIdr ?? "",
            CreDt = document.FIToFIPmtStsRpt.GrpHdr?.CreDtTm ?? DateTime.UtcNow,
            // GrdHeader Information
            MsgId = document.FIToFIPmtStsRpt.GrpHdr?.MsgId ?? "",
            From = document.FIToFIPmtStsRpt.GrpHdr?.InstgAgt?.FinInstnId?.Othr?.Id ?? "",
            To = document.FIToFIPmtStsRpt.GrpHdr?.InstdAgt?.FinInstnId?.Othr?.Id ?? "",
            Status = document.FIToFIPmtStsRpt.TxInfAndSts[0].TxSts ?? "RJCT",
            Reason = document.FIToFIPmtStsRpt.TxInfAndSts[0].StsRsnInf?.Select(x => x.Rsn?.Prtry)?.FirstOrDefault(),
            AdditionalInfo = document.FIToFIPmtStsRpt.TxInfAndSts[0].StsRsnInf?
                .SelectMany(x => x.AddtlInf)
                .FirstOrDefault(),
            TxId = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxId,

            // Original Message Information
            Original = new()
            {
                MsgId = document.FIToFIPmtStsRpt?.OrgnlGrpInfAndSts[0]?.OrgnlMsgId ?? "",
                BizMsgIdr = document.FIToFIPmtStsRpt?.OrgnlGrpInfAndSts[0]?.OrgnlMsgNmId ?? "",
                CreDt = document.FIToFIPmtStsRpt?.OrgnlGrpInfAndSts[0]?.OrgnlCreDtTm ?? DateTime.UtcNow,
                OriginalEndToEnd = document.FIToFIPmtStsRpt?.TxInfAndSts[0].OrgnlEndToEndId ?? "",
                OrgnlTxId = document.FIToFIPmtStsRpt?.TxInfAndSts[0].OrgnlTxId ?? "",
                OriginalAmount = document.FIToFIPmtStsRpt?.TxInfAndSts[0].OrgnlTxRef?.Amt?.InstdAmt?.TypedValue ?? 0,
                OriginalCurrency = document.FIToFIPmtStsRpt?.TxInfAndSts[0].OrgnlTxRef?.Amt?.InstdAmt?.Ccy ?? "",
            }
        };

        return rsp;
    }
}
