using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using SIPS.ISO20022.Schemas.VResponse;
using SIPS.ISO20022.Schemas.VRHeader;
using SIPS.ISO20022.Schemas.VRDocument;

namespace SIPS.ISO20022.Helpers;

public static class PayeeVerificationResponseBuilder
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
        public PayeeVerificationBuilder.Request Original { get; set; } = null!;
        public bool Verified { get; set; }
        public string VerificationId { get; set; } = string.Empty;
        public string Reason { get; set; } = "Miss";
        public string? AdditionalInfo { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string? InvoiceId { get; set; }
        public string? Upr { get; set; }
        public string? BillReference { get; set; }
        public string? Mda { get; set; }
        public string? MdaId { get; set; }
        public string? MdaCode { get; set; }
        public decimal? AmountPayable { get; set; }
    }
    private static AppHdr AppHeader(Request request, SupportedMessageTypes type)
    {
        AppHdr hdr = new()
        {
            Fr = new Party44Choice
            {
                FIId = new Schemas.VRHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.VRHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.VRHeader.GenericFinancialIdentification1
                        {
                            Id = request.From
                        }
                    }
                }
            },
            To = new Party44Choice
            {
                FIId = new Schemas.VRHeader.BranchAndFinancialInstitutionIdentification6
                {
                    FinInstnId = new Schemas.VRHeader.FinancialInstitutionIdentification18
                    {
                        Othr = new Schemas.VRHeader.GenericFinancialIdentification1
                        {
                            Id = request.To
                        }
                    }
                }
            },
            BizMsgIdr = Transformers.GenerateId(request.To),
            MsgDefIdr = type.Id,
            CreDt = DateTime.UtcNow,
            Rltd = [
                new BusinessApplicationHeader7 {
                     Fr = new Party44Choice
                        {
                            FIId = new Schemas.VRHeader.BranchAndFinancialInstitutionIdentification6
                            {
                                FinInstnId = new Schemas.VRHeader.FinancialInstitutionIdentification18
                                {
                                    Othr = new Schemas.VRHeader.GenericFinancialIdentification1
                                    {
                                        Id = request.Original.From
                                    }
                                }
                            }
                        },
                    To = new Party44Choice
                        {
                            FIId = new Schemas.VRHeader.BranchAndFinancialInstitutionIdentification6
                            {
                                FinInstnId = new Schemas.VRHeader.FinancialInstitutionIdentification18
                                {
                                    Othr = new Schemas.VRHeader.GenericFinancialIdentification1
                                    {
                                        Id = request.Original.To
                                    }
                                }
                            }
                        },
                        BizMsgIdr = Transformers.GenerateId(request.Original.To),
                        MsgDefIdr = request.Original.MsgDefIdr,
                        CreDt = DateTime.UtcNow
                    }
            ]
        };
        return hdr;
    }

    public static string Build(Request request)
    {
        var messageType = SupportedMessageTypes.VerificationResponse;

    // ensure minimal defaults via shared helper so XML builders do not hit minLength validation
    if (request.Original == null) request.Original = new PayeeVerificationBuilder.Request();
    Defaults.EnsureVerificationDefaults(request.Original);
    if (string.IsNullOrWhiteSpace(request.From)) request.From = request.Original.From;
    if (string.IsNullOrWhiteSpace(request.To)) request.To = request.Original.To;
    if (request.CreDt == default) request.CreDt = DateTime.UtcNow;

        var appHdr = AppHeader(request, messageType);

        var document = new Document
        {
            IdVrfctnRpt = new IdentificationVerificationReportV03
            {
                Assgnmt = new IdentificationAssignment3
                {
                    MsgId = Transformers.GenerateId(request.From),
                    CreDtTm = DateTime.UtcNow,
                    Assgnr = new Party40Choice
                    {
                        Agt = new Schemas.VRDocument.BranchAndFinancialInstitutionIdentification6
                        {
                            FinInstnId = new Schemas.VRDocument.FinancialInstitutionIdentification18
                            {
                                Othr = new Schemas.VRDocument.GenericFinancialIdentification1
                                {
                                    Id = request.From
                                }
                            }
                        }
                    },
                    Assgne = new Party40Choice
                    {
                        Agt = new Schemas.VRDocument.BranchAndFinancialInstitutionIdentification6
                        {
                            FinInstnId = new Schemas.VRDocument.FinancialInstitutionIdentification18
                            {
                                Othr = new Schemas.VRDocument.GenericFinancialIdentification1
                                {
                                    Id = request.To
                                }
                            }
                        }
                    },
                },
                OrgnlAssgnmt = new MessageIdentification7
                {
                    MsgId = request.Original.MsgId,
                    CreDtTm = request.Original.CreDt
                },
                Rpt = [
                    new VerificationReport4 {
                        OrgnlId = request.Original.SIPSRequestId,
                        Vrfctn = request.Verified,
                        OrgnlPtyAndAcctId = new IdentificationInformation4
                        {
                            Acct = new CashAccount40
                            {
                                Id = new AccountIdentification4Choice {
                                    Othr = new GenericAccountIdentification1
                                    {
                                        Id = request.Original.Alias,
                                        SchmeNm = new AccountSchemeName1Choice
                                        {
                                            Prtry = request.Original.Type
                                        }
                                    }
                                }
                            },
                        }
                    }
                ]
            }
        };

        if (request.Verified)
        {
            // Only set Reason if it's not empty (MinLength=1 constraint)
            if (!string.IsNullOrEmpty(request.Reason))
            {
                document.IdVrfctnRpt.Rpt[0].Rsn = new VerificationReason1Choice
                {
                    Prtry = IsoText.Max35Text(request.Reason, "MISS")
                };
            }

            document.IdVrfctnRpt.Rpt[0].UpdtdPtyAndAcctId = new IdentificationInformation4
            {
                Pty = new Schemas.VRDocument.PartyIdentification135
                {
                    Nm = request.Name
                },
                Acct = new CashAccount40
                {
                    Id = new AccountIdentification4Choice
                    {
                        Othr = new GenericAccountIdentification1
                        {
                            Id = string.IsNullOrEmpty(request.Id) ? request.Original.Alias : request.Id,
                            SchmeNm = new AccountSchemeName1Choice
                            {
                                Prtry = request.Type,
                            }
                        }
                    },
                    Ccy = string.IsNullOrEmpty(request.Currency) ? null : request.Currency
                },
            };

            if (!string.IsNullOrEmpty(request.Address))
            {
                document.IdVrfctnRpt.Rpt[0].UpdtdPtyAndAcctId.Pty.PstlAdr = new Schemas.VRDocument.PostalAddress24
                {
                    AdrLine = [request.Address]
                };
            }

            var billDetails = BuildBillDetailsElement(request);
            if (billDetails != null)
            {
                var supplementaryData = new SupplementaryData1
                {
                    PlcAndNm = "SipsConnectBillDetails",
                    Envlp = new SupplementaryDataEnvelope1()
                };
                supplementaryData.Envlp.Untyped.Add(billDetails);
                document.IdVrfctnRpt.SplmtryData.Add(supplementaryData);
            }
        }
        else
        {
            document.IdVrfctnRpt.Rpt[0].Rsn = new VerificationReason1Choice
            {
                Prtry = IsoText.Max35Text(request.Reason, "MISS"),
            };
        }

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

        Request response = new()
        {
            From = document.IdVrfctnRpt?.Assgnmt?.Assgnr?.Agt?.FinInstnId?.Othr?.Id ?? "",
            To = document.IdVrfctnRpt?.Assgnmt?.Assgne?.Agt?.FinInstnId?.Othr?.Id ?? "",
            MsgDefIdr = envelope.AppHdr.MsgDefIdr,
            MsgId = document.IdVrfctnRpt?.OrgnlAssgnmt?.MsgId ?? "",
            CreDt = document.IdVrfctnRpt?.Assgnmt?.CreDtTm ?? DateTime.UtcNow,
            Verified = document.IdVrfctnRpt?.Rpt?[0]?.Vrfctn ?? false,
            Reason = document.IdVrfctnRpt?.Rpt?[0]?.Rsn?.Prtry ?? "",
            VerificationId = document.IdVrfctnRpt?.Rpt?[0]?.OrgnlId ?? ""
        };
        if (response.Verified)
        {
            response.Name = document.IdVrfctnRpt?.Rpt?[0]?.UpdtdPtyAndAcctId?.Pty?.Nm ?? "";
            response.Address = document.IdVrfctnRpt?.Rpt?[0]?.UpdtdPtyAndAcctId?.Pty?.PstlAdr?.AdrLine?.FirstOrDefault();
            response.Id = document.IdVrfctnRpt?.Rpt?[0]?.UpdtdPtyAndAcctId?.Acct?.Id?.Othr?.Id ?? "";
            response.Type = document.IdVrfctnRpt?.Rpt?[0]?.UpdtdPtyAndAcctId?.Acct?.Id?.Othr?.SchmeNm?.Prtry ?? "";
            response.Currency = document.IdVrfctnRpt?.Rpt?[0]?.UpdtdPtyAndAcctId?.Acct?.Ccy ?? "";
            ApplyBillDetails(document, response);
        }
        return response;
    }

    private static XElement? BuildBillDetailsElement(Request request)
    {
        var reference = FirstNonEmpty(request.BillReference, request.Upr, request.InvoiceId, request.Original?.Alias);
        var payload = new Dictionary<string, object?>();

        AddIfPresent(payload, "invoiceId", request.InvoiceId);
        AddIfPresent(payload, "upr", request.Upr);
        AddIfPresent(payload, "billReference", reference);
        AddIfPresent(payload, "mda", request.Mda);
        AddIfPresent(payload, "mdaId", request.MdaId);
        AddIfPresent(payload, "mdaCode", request.MdaCode);

        if (request.AmountPayable.HasValue)
        {
            payload["amountPayable"] = request.AmountPayable.Value;
        }

        AddIfPresent(payload, "currency", request.Currency);
        AddIfPresent(payload, "creditorAccount", request.Id);
        AddIfPresent(payload, "creditorName", request.Name);
        AddIfPresent(payload, "creditorAccountType", request.Type);

        if (payload.Count == 0)
        {
            return null;
        }

        return new XElement(
            XName.Get("BillDetails", "urn:sips-connect:bill-details:v1"),
            new XCData(JsonSerializer.Serialize(payload)));
    }

    private static void ApplyBillDetails(Document document, Request response)
    {
        var detailText = document.IdVrfctnRpt?.SplmtryData?
            .SelectMany(data => data.Envlp?.Any ?? [])
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "BillDetails", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (string.IsNullOrWhiteSpace(detailText))
        {
            return;
        }

        try
        {
            using var json = JsonDocument.Parse(detailText);
            var root = json.RootElement;
            response.InvoiceId = GetString(root, "invoiceId");
            response.Upr = GetString(root, "upr");
            response.BillReference = FirstNonEmpty(GetString(root, "billReference"), response.Upr, response.InvoiceId);
            response.Mda = GetString(root, "mda");
            response.MdaId = GetString(root, "mdaId");
            response.MdaCode = GetString(root, "mdaCode");
            response.AmountPayable = GetDecimal(root, "amountPayable");
        }
        catch
        {
            // Supplementary data is optional; malformed details should not break ISO parsing.
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static void AddIfPresent(Dictionary<string, object?> payload, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            payload[key] = value;
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static decimal? GetDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var textNumber))
        {
            return textNumber;
        }

        return null;
    }
}
