using SIPS.ISO20022.Schemas.VRequest;
using SIPS.ISO20022.Schemas.VHeader;
using SIPS.ISO20022.Schemas.VDocument;

namespace SIPS.ISO20022.Helpers;

public static class PayeeVerificationBuilder
{
    public class Request : IMessage
    {
        public string From { get; set; } = default!;
        public string To { get; set; } = default!;
        public string TargetBic { get; set; } = string.Empty;
        public string MsgDefIdr { get; set; } = default!;
        public string BizMsgIdr { get; set; } = default!;
        public DateTime CreDt { get; set; }
        public string MsgId { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? SIPSRequestId { get; set; }
    }
    private static AppHdr AppHeader(string from, string to, SupportedMessageTypes type, string bizMsgIdr)
    {
        AppHdr hdr = new()
        {
            Fr = new Party44Choice
            {
                FIId = new Schemas.VHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.VHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.VHeader.GenericFinancialIdentification1
                        {
                            Id = from
                        }
                    }
                }
            },
            To = new Party44Choice
            {
                FIId = new Schemas.VHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.VHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.VHeader.GenericFinancialIdentification1
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

    public static (string document, string bizMsgIdr, string type) Build(Request request)
    {
        var messageType = SupportedMessageTypes.VerificationRequest;
        var bizMsgIdr = Transformers.GenerateId(request.From);
        var appHdr = AppHeader(request.From, request.To, messageType, bizMsgIdr);

        var document = new Document
        {
            IdVrfctnReq = new IdentificationVerificationRequestV03
            {
                Assgnmt = new IdentificationAssignment3
                {
                    MsgId = !string.IsNullOrWhiteSpace(request.MsgId) ? request.MsgId : Transformers.GenerateId(request.From),
                    CreDtTm = DateTime.UtcNow,
                    Assgnr = new Party40Choice
                    {
                        Agt = new Schemas.VDocument.BranchAndFinancialInstitutionIdentification6
                        {
                            FinInstnId = new Schemas.VDocument.FinancialInstitutionIdentification18
                            {
                                Othr = new Schemas.VDocument.GenericFinancialIdentification1
                                {
                                    Id = request.From
                                }
                            }
                        }
                    },
                    Assgne = new Party40Choice
                    {
                        Agt = new Schemas.VDocument.BranchAndFinancialInstitutionIdentification6
                        {
                            FinInstnId = new Schemas.VDocument.FinancialInstitutionIdentification18
                            {
                                Othr = new Schemas.VDocument.GenericFinancialIdentification1
                                {
                                    Id = request.To
                                }
                            }
                        }
                    },
                },
                Vrfctn = [
                    new IdentificationVerification4 {
                        Id = "FP",
                        PtyAndAcctId = new IdentificationInformation4 {
                            Acct = new CashAccount40 {
                                Id = new AccountIdentification4Choice {
                                    Othr = new GenericAccountIdentification1 {
                                        Id = request.Alias,
                                        SchmeNm = new AccountSchemeName1Choice {
                                            Prtry = request.Type
                                        }
                                    }
                                },
                            }
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
        return (Transformers.GeneratePrefixedXml(envelope.Untyped, docNS: messageType.GroupId), bizMsgIdr, messageType.Id);
    }
    public static Request Parse(string content)
    {
        var envelope = FPEnvelope.Parse(content);
        var document = envelope.Document;
        if (!string.Equals(envelope.AppHdr?.MsgDefIdr, SupportedMessageTypes.VerificationRequest.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("The AppHdr message definition does not match an acmt.023 request.");

        return new Request
        {
            From = envelope.AppHdr?.Fr?.FIId?.FinInstnId?.Othr?.Id ?? "",
            To = envelope.AppHdr?.To?.FIId?.FinInstnId?.Othr?.Id ?? "",
            TargetBic = document.IdVrfctnReq?.Assgnmt?.Assgne?.Agt?.FinInstnId?.Othr?.Id ?? "",
            SIPSRequestId = document.IdVrfctnReq?.Vrfctn[0]?.Id ?? "",
            Alias = document.IdVrfctnReq?.Vrfctn[0]?.PtyAndAcctId?.Acct?.Id?.Othr?.Id ?? "",
            Type = document.IdVrfctnReq?.Vrfctn[0]?.PtyAndAcctId?.Acct?.Id?.Othr?.SchmeNm?.Prtry ?? "",
            MsgId = document.IdVrfctnReq?.Assgnmt?.MsgId ?? "",
            CreDt = envelope.AppHdr?.CreDt ?? DateTime.UtcNow,
            MsgDefIdr = envelope.AppHdr?.MsgDefIdr ?? "",
            BizMsgIdr = envelope.AppHdr?.BizMsgIdr ?? "",
        };
    }
}
