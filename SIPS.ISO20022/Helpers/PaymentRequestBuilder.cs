using SIPS.ISO20022.Schemas.PRequest;
using SIPS.ISO20022.Schemas.PRHeader;
using SIPS.ISO20022.Schemas.PRDocument;
namespace SIPS.ISO20022.Helpers;

public static class PaymentRequestBuilder
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
        public SettlementMethod1Code SettlementMethod { get; set; } = SettlementMethod1Code.CLRG;
        public string ClearingSystem { get; set; } = "FP";
        public string LocalInstrument { get; set; } = string.Empty;
        public string CategoryPurpose { get; set; } = string.Empty;
        public string TxId { get; set; } = string.Empty;
        public string EndToEndId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public ChargeBearerType1Code ChargeBearer { get; set; } = ChargeBearerType1Code.SLEV;
        public Person Debtor { get; set; } = new Person();
        public Person Creditor { get; set; } = new Person();
        public string? Ustrd { get; set; }
    }

    private static AppHdr AppHeader(string from, string to, SupportedMessageTypes type)
    {
        AppHdr hdr = new()
        {
            Fr = new Party44Choice
            {
                FIId = new Schemas.PRHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.PRHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.PRHeader.GenericFinancialIdentification1
                        {
                            Id = from
                        }
                    }
                }
            },
            To = new Party44Choice
            {
                FIId = new Schemas.PRHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.PRHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.PRHeader.GenericFinancialIdentification1
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
        var messageType = SupportedMessageTypes.CreditTransferRequest;

        var appHdr = AppHeader(request.From, request.To, messageType);

        var document = new Document
        {
            FIToFICstmrCdtTrf = new FIToFICustomerCreditTransferV10
            {
                GrpHdr = new GroupHeader96
                {
                    MsgId = Transformers.GenerateId(request.From),
                    CreDtTm = DateTime.UtcNow,
                    NbOfTxs = "1",
                    SttlmInf = new SettlementInstruction11
                    {
                        SttlmMtd = request.SettlementMethod,
                        ClrSys = new ClearingSystemIdentification3Choice
                        {
                            Prtry = request.ClearingSystem
                        }
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
                    InstgAgt = new Schemas.PRDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PRDocument.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.PRDocument.GenericFinancialIdentification1
                            {
                                Id = request.From
                            }
                        }
                    },
                    InstdAgt = new Schemas.PRDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PRDocument.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.PRDocument.GenericFinancialIdentification1
                            {
                                Id = request.To
                            }
                        }
                    }
                },
                CdtTrfTxInf = [
                    new CreditTransferTransaction50 {
                        PmtId = new PaymentIdentification13
                        {
                            EndToEndId = request.EndToEndId,
                            TxId = request.TxId
                        },
                        IntrBkSttlmAmt = new ActiveCurrencyAndAmount
                        {
                            Ccy = request.Currency,
                            TypedValue = request.Amount
                        },
                        AccptncDtTm = DateTime.UtcNow,
                        InstdAmt = new ActiveOrHistoricCurrencyAndAmount
                        {
                            Ccy = request.Currency,
                            TypedValue = request.Amount
                        },
                        ChrgBr = request.ChargeBearer,
                        Dbtr = new Schemas.PRDocument.PartyIdentification135
                        {
                            Nm = request.Debtor.Name,
                            PstlAdr = !string.IsNullOrEmpty(request.Debtor.Address)? new Schemas.PRDocument.PostalAddress24
                            {
                                AdrLine = [request.Debtor.Address]
                            }: null
                        },
                        DbtrAcct = new CashAccount40
                        {
                            Id = new AccountIdentification4Choice
                            {
                                Othr = new GenericAccountIdentification1
                                {
                                    Id = request.Debtor.Account,
                                    SchmeNm = new AccountSchemeName1Choice
                                    {
                                        Prtry = request.Debtor.AccountType
                                    },
                                    Issr = request.Debtor.Issuer
                                }
                            },
                        },
                        DbtrAgt = new Schemas.PRDocument.BranchAndFinancialInstitutionIdentification6
                        {
                            FinInstnId = new Schemas.PRDocument.FinancialInstitutionIdentification18
                            {
                                Othr = new Schemas.PRDocument.GenericFinancialIdentification1
                                {
                                    Id = request.Debtor.AgentBIC
                                }
                            }
                        },
                        CdtrAgt = new Schemas.PRDocument.BranchAndFinancialInstitutionIdentification6
                        {
                            FinInstnId = new Schemas.PRDocument.FinancialInstitutionIdentification18
                            {
                                Othr = new Schemas.PRDocument.GenericFinancialIdentification1
                                {
                                    Id = request.Creditor.AgentBIC
                                }
                            }
                        },
                        Cdtr = new Schemas.PRDocument.PartyIdentification135
                        {
                            Nm = request.Creditor.Name,
                            PstlAdr = !string.IsNullOrEmpty(request.Creditor.Address)? new Schemas.PRDocument.PostalAddress24
                            {
                                AdrLine = [request.Creditor.Address]
                            } : null
                        },
                        CdtrAcct = new CashAccount40
                        {
                            Id = new AccountIdentification4Choice
                            {
                                Othr = new GenericAccountIdentification1
                                {
                                    Id = request.Creditor.Account,
                                    SchmeNm = new AccountSchemeName1Choice
                                    {
                                        Prtry = request.Creditor.AccountType
                                    },
                                    Issr = request.Creditor.Issuer
                                }
                            },
                        },
                        RmtInf = new RemittanceInformation21
                        {
                            Ustrd = [request.Ustrd]
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
    public static Request Parse(string content)
    {
        var envelope = FPEnvelope.Parse(content);
        var document = envelope.Document;
        var request = new Request();
        request.From = document.FIToFICstmrCdtTrf.GrpHdr.InstgAgt.FinInstnId.Othr.Id;
        request.To = document.FIToFICstmrCdtTrf.GrpHdr.InstdAgt.FinInstnId.Othr.Id;
        request.BizMsgIdr = envelope.AppHdr.BizMsgIdr;
        request.MsgDefIdr = envelope.AppHdr.MsgDefIdr;
        request.CreDt = envelope.AppHdr.CreDt;
        request.SettlementMethod = document.FIToFICstmrCdtTrf.GrpHdr.SttlmInf.SttlmMtd;
        request.ClearingSystem = document.FIToFICstmrCdtTrf.GrpHdr.SttlmInf.ClrSys.Prtry;
        request.LocalInstrument = document.FIToFICstmrCdtTrf.GrpHdr.PmtTpInf.LclInstrm.Prtry;
        request.CategoryPurpose = document.FIToFICstmrCdtTrf.GrpHdr.PmtTpInf.CtgyPurp.Prtry;
        request.TxId = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].PmtId.TxId;
        request.EndToEndId = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].PmtId.EndToEndId;
        request.Amount = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].InstdAmt.TypedValue;
        request.Currency = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].InstdAmt.Ccy;
        request.ChargeBearer = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].ChrgBr;
        request.Debtor = new Person
        {
            Name = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].Dbtr.Nm,
            Address = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].Dbtr?.PstlAdr?.AdrLine[0] ?? "",
            Account = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].DbtrAcct.Id.Othr.Id,
            AccountType = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].DbtrAcct.Id.Othr.SchmeNm.Prtry,
            Issuer = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].DbtrAcct.Id.Othr.Issr,
            AgentBIC = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].DbtrAgt.FinInstnId.Othr.Id
        };
        request.Creditor = new Person
        {
            Name = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].Cdtr.Nm,
            Address = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].Cdtr?.PstlAdr?.AdrLine[0] ?? "",
            Account = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].CdtrAcct.Id.Othr.Id,
            AccountType = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].CdtrAcct.Id.Othr.SchmeNm.Prtry,
            Issuer = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].CdtrAcct.Id.Othr.Issr,
            AgentBIC = document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].CdtrAgt.FinInstnId.Othr.Id
        };
        request.Ustrd = string.Join(" ", document.FIToFICstmrCdtTrf.CdtTrfTxInf[0].RmtInf.Ustrd);

        return request;
    }
}
