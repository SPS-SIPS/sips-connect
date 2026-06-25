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
        public DateTime? AcceptanceDate { get; set; }
        public string TxId { get; set; } = string.Empty;
        public SIPS.ISO20022.Enums.Pacs002Role Role { get; set; } = SIPS.ISO20022.Enums.Pacs002Role.StatusUpdate;
    }
    /// <summary>
    /// Creates the AppHdr for a pacs.002 response message.
    /// IMPORTANT: SmartVista IPS Switch Behavior
    /// - The switch REGENERATES AppHdr when forwarding pacs.008 (new BizMsgIdr, CreDt, Fr/To)
    /// - The Rltd block MUST reference the IMMEDIATE PARENT message (switch-forwarded pacs.008)
    /// - NOT the original bank's pacs.008 message
    /// - This is per BPC SmartVista IPS architecture where the switch acts as a message gateway
    /// </summary>
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
            BizMsgIdr = IsoText.Max35Identifier(model.BizMsgIdr, model.From),
            MsgDefIdr = type.Id,
            CreDt = DateTime.UtcNow,
            // Rltd references the IMMEDIATE PARENT message we received (switch-forwarded pacs.008)
            // NOT the original bank's pacs.008 - this is critical for SmartVista IPS correlation
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
                                // Use the immediate parent's From (which is the switch or sending bank)
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
                                // Use the immediate parent's To (which is us)
                                Id = model.Original.To
                            }
                        }
                    }
                },
                // These fields come from the RECEIVED message's AppHdr (switch-generated)
                BizMsgIdr = IsoText.Max35Identifier(model.Original.BizMsgIdr, model.Original.From),
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

        if (request.Original == null) request.Original = new PaymentRequestBuilder.Request();
        Defaults.EnsureOriginalDefaults(request.Original);
        if (string.IsNullOrWhiteSpace(request.From)) request.From = request.Original?.To ?? "FROM";
        if (string.IsNullOrWhiteSpace(request.To)) request.To = request.Original?.From ?? "TO";
        if (request.CreDt == default) request.CreDt = DateTime.UtcNow;

        var appHdr = AppHeader(request, messageType);

        var orig = request.Original ?? new PaymentRequestBuilder.Request();

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
                            OrgnlMsgId = IsoText.Max35Identifier(orig.MsgId, orig.From),
                            OrgnlMsgNmId = orig.MsgDefIdr,
                            OrgnlCreDtTm = orig.CreDt
                        },
                        OrgnlEndToEndId = orig.EndToEndId,
                        OrgnlTxId = orig.TxId,
                        TxSts = request.Status,
                        StsRsnInf = GetStatusReasonInformation(request),
                        AccptncDtTm = request.Status == "RJCT" ? null : request.AcceptanceDate,
                        OrgnlTxRef = new OriginalTransactionReference35 {
                            IntrBkSttlmAmt = new ActiveOrHistoricCurrencyAndAmount {
                                Ccy = orig.Currency,
                                TypedValue = orig.Amount
                            },
                            Amt = new AmountType4Choice {
                                InstdAmt = new ActiveOrHistoricCurrencyAndAmount {
                                    Ccy = orig.Currency,
                                    TypedValue = orig.Amount
                                }
                            },
                            Dbtr = new Party40Choice {
                                Pty = new Schemas.PRRDocument.PartyIdentification135 {
                                    Nm = orig.Debtor.Name,
                                    PstlAdr = new Schemas.PRRDocument.PostalAddress24 {
                                        AdrLine = [orig.Debtor.Address]
                                    }
                                }
                            },
                            DbtrAcct = new CashAccount40 {
                                Id = new AccountIdentification4Choice {
                                    Othr = new GenericAccountIdentification1 {
                                        Id = orig.Debtor.Account,
                                        SchmeNm = new AccountSchemeName1Choice {
                                            Prtry = orig.Debtor.AccountType
                                        },
                                        Issr = orig.Debtor.Issuer
                                    }
                                }
                            },
                            CdtrAcct = new CashAccount40 {
                                Id = new AccountIdentification4Choice {
                                    Othr = new GenericAccountIdentification1 {
                                        Id = orig.Creditor.Account,
                                        SchmeNm = new AccountSchemeName1Choice {
                                            Prtry = orig.Creditor.AccountType
                                        },
                                        Issr = orig.Creditor.Issuer
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
        rsp.AcceptanceDate = document.FIToFIPmtStsRpt.TxInfAndSts[0].AccptncDtTm;
        rsp.TxId = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxId;
        rsp.Status = document.FIToFIPmtStsRpt.TxInfAndSts[0].TxSts ?? "RJCT";
        rsp.Reason = document.FIToFIPmtStsRpt.TxInfAndSts[0].StsRsnInf?.Select(x => x.Rsn?.Prtry)?.FirstOrDefault();
        rsp.AdditionalInfo = document.FIToFIPmtStsRpt.TxInfAndSts[0].StsRsnInf?
            .SelectMany(x => x.AddtlInf)
            .FirstOrDefault();

        // Infer Pacs002Role based on Status (Delta 5)
        if (rsp.Status == "ACSC")
        {
            rsp.Role = SIPS.ISO20022.Enums.Pacs002Role.CompletionNotify;
        }
        else if (rsp.Status == "ACSP")
        {
            rsp.Role = SIPS.ISO20022.Enums.Pacs002Role.StatusUpdate;
        }
        else
        {
            // For RJCT or others, default to StatusUpdate or specialized handling
            rsp.Role = SIPS.ISO20022.Enums.Pacs002Role.StatusUpdate;
        }
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
                Address = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.Dbtr?.Pty?.PstlAdr?.AdrLine.FirstOrDefault() ?? "",
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
                Address = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.Cdtr?.Pty?.PstlAdr?.AdrLine.FirstOrDefault() ?? "",
                Account = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.CdtrAcct?.Id?.Othr?.Id ?? "",
                AccountType = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.CdtrAcct?.Id?.Othr?.SchmeNm?.Prtry ?? "",
                Issuer = document.FIToFIPmtStsRpt.TxInfAndSts[0].OrgnlTxRef?.CdtrAcct?.Id?.Othr?.Issr ?? ""
            };
            rsp.Original.Creditor = creditor;
        }
        return rsp;
    }

    private static List<StatusReasonInformation12>? GetStatusReasonInformation(Response request)
    {
        if (request.Status == "ACSC") return null;

        var rawReason = request.Reason?.Trim();
        var reasonCode = IsoText.StatusReasonCode(rawReason);
        var additionalInfo = IsoText.StatusAdditionalInfo(
            request.AdditionalInfo,
            IsoText.IsSafeMax35Text(rawReason) ? null : rawReason);

        var reasonChoice = new StatusReason6Choice();
        try
        {
            reasonChoice.Prtry = reasonCode;
        }
        catch (Xml.Schema.Linq.LinqToXsdException) when (reasonCode != "NARR")
        {
            reasonChoice.Prtry = "NARR";
            additionalInfo = IsoText.StatusAdditionalInfo(additionalInfo, rawReason);
        }

        var statusReason = new StatusReasonInformation12
        {
            Rsn = reasonChoice
        };

        if (!string.IsNullOrEmpty(additionalInfo))
        {
            statusReason.AddtlInf.Add(additionalInfo);
        }

        return [statusReason];
    }
}
