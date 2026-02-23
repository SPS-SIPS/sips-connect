using SIPS.ISO20022.Schemas.PSResponse;
using SIPS.ISO20022.Schemas.PSRHeader;
using SIPS.ISO20022.Schemas.PSRDocument;
namespace SIPS.ISO20022.Helpers;

public static class PaymentStatusRequestResponseBuilder
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
        public PaymentRequestBuilder.Request Original { get; set; } = new();
        public string? Status { get; set; }
        public string? Reason { get; set; }
        public string? AdditionalInfo { get; set; }
        public DateTime AcceptanceDate { get; set; }
        public string TxId { get; set; } = string.Empty;
    }
    /// <summary>
    /// Creates the AppHdr for a pacs.002 response message (completion notification response).
    /// IMPORTANT: SmartVista IPS Switch Behavior
    /// - The switch REGENERATES AppHdr when forwarding messages (new BizMsgIdr, CreDt, Fr/To)
    /// - The Rltd block MUST reference the IMMEDIATE PARENT message (switch-generated pacs.002 CN)
    /// - NOT the original pacs.008 message
    /// - This is per BPC SmartVista IPS architecture where the switch acts as a message gateway
    /// - For completion notification responses, Rltd points to the CN we received from the switch
    /// </summary>
    private static AppHdr AppHeader(Response model, SupportedMessageTypes type)
    {
        AppHdr hdr = new()
        {
            Fr = new Party44Choice
            {
                FIId = new Schemas.PSRHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.PSRHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.PSRHeader.GenericFinancialIdentification1
                        {
                            Id = model.From
                        }
                    }
                }
            },
            To = new Party44Choice
            {
                FIId = new Schemas.PSRHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.PSRHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.PSRHeader.GenericFinancialIdentification1
                        {
                            Id = model.To
                        }
                    }
                }
            },
            BizMsgIdr = model.BizMsgIdr,
            MsgDefIdr = type.Id,
            CreDt = DateTime.UtcNow,
            // Rltd references the IMMEDIATE PARENT message we received (switch-generated pacs.002 CN)
            // NOT the original pacs.008 - this is critical for SmartVista IPS correlation
            Rltd = [
                new BusinessApplicationHeader7
            {
                Fr = new Party44Choice
                {
                    FIId = new Schemas.PSRHeader.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PSRHeader.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.PSRHeader.GenericFinancialIdentification1
                            {
                                // Use the immediate parent's From (switch or sending bank)
                                Id = model.Original.From
                            }
                        }
                    }
                },
                To = new Party44Choice
                {
                    FIId = new Schemas.PSRHeader.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PSRHeader.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.PSRHeader.GenericFinancialIdentification1
                            {
                                // Use the immediate parent's To (which is us)
                                Id = model.Original.To
                            }
                        }
                    }
                },
                // These fields come from the RECEIVED message's AppHdr (switch-generated)
                BizMsgIdr = model.Original?.BizMsgIdr ?? string.Empty,
                MsgDefIdr = model.Original?.MsgDefIdr ?? string.Empty,
                CreDt = model.Original?.CreDt ?? DateTime.UtcNow
            }
            ]
        };
        return hdr;
    }

    public static string Build(Response request)
    {
        var messageType = SupportedMessageTypes.CreditTransferResponse;

    // Ensure original message parts have safe defaults to satisfy schema minLength constraints
    if (request.Original == null) request.Original = new PaymentRequestBuilder.Request();
    Defaults.EnsureOriginalDefaults(request.Original);
        if (string.IsNullOrWhiteSpace(request.From)) request.From = request.Original?.To ?? "FROM";
        if (string.IsNullOrWhiteSpace(request.To)) request.To = request.Original?.From ?? "TO";
        if (request.CreDt == default) request.CreDt = DateTime.UtcNow;

        var appHdr = AppHeader(request, messageType);
        var isReject = request.Status == "RJCT";

        // Build StsRsnInf only if Reason or AdditionalInfo is not null, not empty, and not whitespace
        StatusReasonInformation12? statusReasonInfo = null;
        bool hasReason = !string.IsNullOrEmpty(request.Reason) && request.Reason.Trim().Length > 0;
        bool hasAddtlInf = !string.IsNullOrEmpty(request.AdditionalInfo) && request.AdditionalInfo.Trim().Length > 0;
        if (hasReason || hasAddtlInf)
        {
            statusReasonInfo = new StatusReasonInformation12();
            if (hasReason)
            {
                statusReasonInfo.Rsn = new StatusReason6Choice { Prtry = request.Reason!.Trim() };
            }
            if (hasAddtlInf)
            {
                statusReasonInfo.AddtlInf = [request.AdditionalInfo!.Trim()];
            }
        }

    var document = new Document
        {
            FIToFIPmtStsRpt = new FIToFIPaymentStatusReportV12
            {
                GrpHdr = new GroupHeader101
                {
                    MsgId = Transformers.GenerateId(request.From),
                    CreDtTm = request.CreDt,
                    InstgAgt = new Schemas.PSRDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PSRDocument.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.PSRDocument.GenericFinancialIdentification1
                            {
                                Id = request.From
                            }
                        }
                    },
                    InstdAgt = new Schemas.PSRDocument.BranchAndFinancialInstitutionIdentification6
                    {
                        FinInstnId = new Schemas.PSRDocument.FinancialInstitutionIdentification18
                        {
                            Othr = new Schemas.PSRDocument.GenericFinancialIdentification1
                            {
                                Id = request.To
                            }
                        }
                    }
                },
                TxInfAndSts = [
                    new PaymentTransaction130 {
                        OrgnlGrpInf = new OriginalGroupInformation29 {
                            OrgnlMsgId = request.Original?.MsgId ?? string.Empty,
                            OrgnlMsgNmId = request.Original?.MsgDefIdr ?? string.Empty,
                            OrgnlCreDtTm = request.Original?.CreDt ?? DateTime.UtcNow
                        },
                        OrgnlEndToEndId = request.Original?.EndToEndId ?? string.Empty,
                        OrgnlTxId = request.Original?.TxId ?? string.Empty,
                        TxSts = request.Status,
                        StsRsnInf = statusReasonInfo != null ? [statusReasonInfo] : null,
                        AccptncDtTm = !isReject ? request.AcceptanceDate : null,
                        OrgnlTxRef =!isReject ? new OriginalTransactionReference35 {
                            IntrBkSttlmAmt = new ActiveOrHistoricCurrencyAndAmount {
                                Ccy = request.Original?.Currency ?? "USD",
                                TypedValue = request.Original?.Amount ?? 0
                            },
                            Amt = new AmountType4Choice {
                                InstdAmt = new ActiveOrHistoricCurrencyAndAmount {
                                    Ccy = request.Original?.Currency ?? "USD",
                                    TypedValue = request.Original?.Amount ?? 0
                                }
                            },
                            Dbtr = new Party40Choice {
                                Pty = new Schemas.PSRDocument.PartyIdentification135 {
                                    Nm = request.Original?.Debtor?.Name ?? string.Empty,
                                    PstlAdr = new Schemas.PSRDocument.PostalAddress24 {
                                        AdrLine = new[] { request.Original?.Debtor?.Address ?? string.Empty }
                                    }
                                }
                            },
                            DbtrAcct = new CashAccount40 {
                                Id = new AccountIdentification4Choice {
                                    Othr = new GenericAccountIdentification1 {
                                        Id = request.Original?.Debtor?.Account ?? string.Empty,
                                        SchmeNm = new AccountSchemeName1Choice {
                                            Prtry = request.Original?.Debtor?.AccountType ?? string.Empty
                                        },
                                        Issr = request.Original?.Debtor?.Issuer ?? string.Empty
                                    }
                                }
                            },
                            Cdtr = new Party40Choice {
                                Pty = new Schemas.PSRDocument.PartyIdentification135 {
                                    Nm = request.Original?.Creditor?.Name ?? string.Empty,
                                    PstlAdr = new Schemas.PSRDocument.PostalAddress24 {
                                        AdrLine = new[] { request.Original?.Creditor?.Address ?? string.Empty }
                                    }
                                }
                            },
                            CdtrAcct = new CashAccount40 {
                                Id = new AccountIdentification4Choice {
                                    Othr = new GenericAccountIdentification1 {
                                        Id = request.Original?.Creditor?.Account ?? string.Empty,
                                        SchmeNm = new AccountSchemeName1Choice {
                                            Prtry = request.Original?.Creditor?.AccountType ?? string.Empty
                                        },
                                        Issr = request.Original?.Creditor?.Issuer ?? string.Empty
                                    }
                                }
                            },
                        }: null,
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
