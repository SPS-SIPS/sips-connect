using System.Globalization;

namespace SIPS.ISO20022.Models.DTOs;

public sealed record P2GNameDescriptor(
    string MdaCode,
    string ServiceCode,
    string InvoiceId,
    string Currency,
    decimal Amount,
    DateOnly DueDate,
    string PayerReference,
    string Hmac)
{
    public string BillReference => string.IsNullOrWhiteSpace(InvoiceId) ? PayerReference : InvoiceId;
    public string RemittanceInformation => $"BILL:{BillReference}";
}

public static class P2GNameDescriptorParser
{
    public static bool TryParse(string? name, out P2GNameDescriptor descriptor)
    {
        descriptor = default!;

        if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("P2G1|", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = name.Split('|');
        if (parts.Length != 9 || parts[0] != "P2G1")
        {
            return false;
        }

        if (!decimal.TryParse(parts[5], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        if (!DateOnly.TryParseExact(parts[6], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate))
        {
            return false;
        }

        descriptor = new P2GNameDescriptor(
            MdaCode: parts[1],
            ServiceCode: parts[2],
            InvoiceId: parts[3],
            Currency: parts[4],
            Amount: amount,
            DueDate: dueDate,
            PayerReference: parts[7],
            Hmac: parts[8]);

        return true;
    }
}
