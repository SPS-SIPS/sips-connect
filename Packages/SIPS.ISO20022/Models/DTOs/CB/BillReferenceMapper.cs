namespace SIPS.ISO20022.Models.DTOs.CB;

public static class BillReferenceMapper
{
    public static void Apply(CBPaymentRequestDto dto)
    {
        var reference = Extract(dto.RemittanceInformation);
        if (string.IsNullOrWhiteSpace(reference))
        {
            return;
        }

        if (dto.RemittanceInformation == null
            || !dto.RemittanceInformation.TrimStart().StartsWith("BILL:", StringComparison.OrdinalIgnoreCase))
        {
            dto.RemittanceInformation = $"BILL:{reference}";
        }

        if (reference.StartsWith("UPR", StringComparison.OrdinalIgnoreCase))
        {
            dto.Upr = reference;
        }
        else
        {
            dto.InvoiceId = reference;
        }
    }

    public static string? Extract(string? remittanceInformation)
    {
        if (string.IsNullOrWhiteSpace(remittanceInformation))
        {
            return null;
        }

        var trimmed = remittanceInformation.Trim();
        if (trimmed.StartsWith("BILL:", StringComparison.OrdinalIgnoreCase))
        {
            var value = trimmed["BILL:".Length..].Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        if (trimmed.StartsWith("INV", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("UPR", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return null;
    }
}
