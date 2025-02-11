using SIPS.ISO20022.Schemas.PRResponse;
using SIPS.ISO20022.Schemas.PRRHeader;
using SIPS.ISO20022.Schemas.PRRDocument;
namespace SIPS.ISO20022.Helpers;

public static class PaymentRequestResponseBuilder
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
        public PaymentRequestBuilder.Request Original { get; set; } = default!;
        public string? Status { get; set; }
        public string? Reason { get; set; }
        public string? AdditionalInfo { get; set; }
        public DateTime AcceptanceDate { get; set; }
        public string TxId { get; set; } = string.Empty;
    }
    private static AppHdr AppHeader(Response model, SupportedMessageTypes type)
    {
        AppHdr hdr = new()
        {
            Fr = new Party44Choice
            {
                FIId = new Schemas.PRRHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.PRRHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.PRRHeader.GenericFinancialIdentification1
                        {
                            Id = model.From
                        }
                    }
                }
            },
            To = new Party44Choice
            {
                FIId = new Schemas.PRRHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.PRRHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.PRRHeader.GenericFinancialIdentification1
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
                    FIId = new Schemas.PRRHeader.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PRRHeader.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.PRRHeader.GenericFinancialIdentification1
                            {
                                Id = model.Original.From
                            }
                        }
                    }
                },
                To = new Party44Choice
                {
                    FIId = new Schemas.PRRHeader.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PRRHeader.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.PRRHeader.GenericFinancialIdentification1
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

        var appHdr = AppHeader(request, messageType);

        var document = new Document
        {
            FIToFIPmtStsRpt = new FIToFIPaymentStatusReportV12
            {
                GrpHdr = new GroupHeader101
                {
                    MsgId = Transformers.GenerateId(request.From),
                    CreDtTm = DateTime.UtcNow,
                    InstgAgt = new Schemas.PRRDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PRRDocument.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.PRRDocument.GenericFinancialIdentification1
                            {
                                Id = request.From
                            }
                        }
                    },
                    InstdAgt = new Schemas.PRRDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PRRDocument.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.PRRDocument.GenericFinancialIdentification1
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
                        OrgnlEndToEndId = request.Original.TxId,
                        OrgnlTxId = request.Original.TxId,
                        TxSts = request.Status,
                        StsRsnInf = request.Status != "ACSC"? [
                            new StatusReasonInformation12 {
                                Rsn = new StatusReason6Choice {
                                    Prtry = request.Reason
                                },
                                AddtlInf = [request.AdditionalInfo]
                            }
                        ]: null,
                        AccptncDtTm = DateTime.UtcNow,
                        OrgnlTxRef = new OriginalTransactionReference35 {
                            IntrBkSttlmAmt = new ActiveOrHistoricCurrencyAndAmount {
                                Ccy = request.Original.Currency,
                                TypedValue = request.Original.Amount
                            },
                            Amt = new AmountType4Choice {
                                InstdAmt = new ActiveOrHistoricCurrencyAndAmount {
                                    Ccy = request.Original.Currency,
                                    TypedValue = request.Original.Amount
                                }
                            },
                            Dbtr = new Party40Choice {
                                Pty = new Schemas.PRRDocument.PartyIdentification135 {
                                    Nm = request.Original.Debtor.Name,
                                    PstlAdr = new Schemas.PRRDocument.PostalAddress24 {
                                        AdrLine = [request.Original.Debtor.Address]
                                    }
                                }
                            },
                            DbtrAcct = new CashAccount40 {
                                Id = new AccountIdentification4Choice {
                                    Othr = new GenericAccountIdentification1 {
                                        Id = request.Original.Debtor.Account,
                                        SchmeNm = new AccountSchemeName1Choice {
                                            Prtry = request.Original.Debtor.AccountType
                                        },
                                        Issr = request.Original.Debtor.Issuer
                                    }
                                }
                            },
                            CdtrAcct = new CashAccount40 {
                                Id = new AccountIdentification4Choice {
                                    Othr = new GenericAccountIdentification1 {
                                        Id = request.Original.Creditor.Account,
                                        SchmeNm = new AccountSchemeName1Choice {
                                            Prtry = request.Original.Creditor.AccountType
                                        },
                                        Issr = request.Original.Creditor.Issuer
                                    }
                                }
                            },
                        }
                    }
                ]
            }
        };

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
        var rsp = new Response();
        // Current Message Information
        rsp.MsgId = document.FIToFIPmtStsRpt.GrpHdr?.MsgId ?? "";
        rsp.CreDt = document.FIToFIPmtStsRpt.GrpHdr?.CreDtTm ?? DateTime.UtcNow;
        rsp.From = document.FIToFIPmtStsRpt.GrpHdr?.InstgAgt?.FinInstnId?.Othr?.Id ?? "";
        rsp.To = document.FIToFIPmtStsRpt.GrpHdr?.InstdAgt?.FinInstnId?.Othr?.Id ?? "";
        rsp.BizMsgIdr = envelope.AppHdr?.BizMsgIdr ?? "";
        rsp.MsgDefIdr = envelope.AppHdr?.MsgDefIdr ?? "";
        rsp.AcceptanceDate = document.FIToFIPmtStsRpt.TxInfAndSts[0].AccptncDtTm ?? DateTime.UtcNow;
        rsp.TxId = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxId;
        rsp.Status = document.FIToFIPmtStsRpt.TxInfAndSts[0].TxSts ?? "RJCT";
        rsp.Reason = document.FIToFIPmtStsRpt.TxInfAndSts[0].StsRsnInf?.Select(x => x.Rsn?.Prtry)?.FirstOrDefault();
        rsp.AdditionalInfo = document.FIToFIPmtStsRpt.TxInfAndSts[0].StsRsnInf?
            .SelectMany(x => x.AddtlInf)
            .FirstOrDefault();
        // Original Message Information
        rsp.Original = new()
        {
            MsgId = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlGrpInf?.OrgnlMsgId ?? "",
            MsgDefIdr = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlGrpInf?.OrgnlMsgNmId ?? "",
            CreDt = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlGrpInf?.OrgnlCreDtTm ?? DateTime.UtcNow,
            EndToEndId = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlEndToEndId ?? "",
            TxId = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxId ?? "",
            Amount = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.Amt?.InstdAmt?.TypedValue ?? 0,
            Currency = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.Amt?.InstdAmt?.Ccy ?? "USD"
        };


        if (rsp.Original?.Debtor != null)
        {
            Person debtor = new()
            {
                Name = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.Dbtr?.Pty?.Nm ?? "",
                Address = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.Dbtr?.Pty?.PstlAdr?.AdrLine[0] ?? "",
                Account = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.DbtrAcct?.Id?.Othr?.Id ?? "",
                AccountType = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.DbtrAcct?.Id?.Othr?.SchmeNm?.Prtry ?? "",
                Issuer = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.DbtrAcct?.Id?.Othr?.Issr ?? ""
            };
            rsp.Original.Debtor = debtor;
        }

        if (rsp.Original?.Creditor != null)
        {
            Person creditor = new()
            {
                Name = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.Cdtr?.Pty?.Nm ?? "",
                Address = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.Cdtr?.Pty?.PstlAdr?.AdrLine[0] ?? "",
                Account = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.CdtrAcct?.Id?.Othr?.Id ?? "",
                AccountType = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.CdtrAcct?.Id?.Othr?.SchmeNm?.Prtry ?? "",
                Issuer = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.CdtrAcct?.Id?.Othr?.Issr ?? ""
            };
            rsp.Original.Creditor = creditor;
        }
        return rsp;
    }
}
