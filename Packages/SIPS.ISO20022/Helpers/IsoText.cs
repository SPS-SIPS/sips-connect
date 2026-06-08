namespace SIPS.ISO20022.Helpers;

public static class IsoText
{
    public const int Max35TextLength = 35;
    public const int Max105TextLength = 105;

    public static bool IsSafeMax35Text(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= Max35TextLength;
    }

    public static string StatusReasonCode(string? reason, string fallback = "NARR")
    {
        var value = reason?.Trim();
        return IsSafeMax35Text(value) ? value! : fallback;
    }

    public static string? StatusAdditionalInfo(params string?[] values)
    {
        var parts = values
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (parts.Length == 0)
            return null;

        var combined = string.Join("; ", parts);
        return combined.Length <= Max105TextLength
            ? combined
            : combined[..Max105TextLength];
    }

    public static string Max35Text(string? value, string fallback = "NARR")
    {
        var trimmed = value?.Trim();
        return IsSafeMax35Text(trimmed) ? trimmed! : fallback;
    }

    public static string Max35Identifier(string? value, string fallbackPrefix = "MSG")
    {
        var trimmed = value?.Trim();
        if (IsSafeMax35Text(trimmed))
            return trimmed!;

        var prefix = string.IsNullOrWhiteSpace(fallbackPrefix) ? "MSG" : fallbackPrefix.Trim();
        if (prefix.Length > 8)
            prefix = prefix[..8];

        var generated = Transformers.GenerateId(prefix);
        return generated.Length <= Max35TextLength ? generated : generated[..Max35TextLength];
    }
}
