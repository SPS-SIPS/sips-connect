namespace SIPS.ISO20022.Helpers;

/// <summary>
/// Maps a core bank's own rejection reason onto the ISO 20022 code PAPSS actually recognizes, per PAPSS's own
/// published internal-to-ISO mapping (PAPSS developer portal, Reference -> Error Codes; see
/// docs/PAPSS_OPERATION_STORE_CONFIG.md "PAPSS error code reference"). A core bank's raw reason string (for example
/// "NotFound", "ERRR", a free-text sentence) is its own internal vocabulary, not PAPSS's - sending it to PAPSS
/// verbatim risks a code PAPSS does not recognize. This only ever returns a code PAPSS's own table confirms;
/// anything not confirmed there falls back to MS03 (itself a PAPSS-confirmed code for "internal processing error" /
/// "generic error"), never an invented or passed-through value.
/// </summary>
public static class PapssReasonCodes
{
    public const string GenericError = "MS03";
    public const string InsufficientFunds = "AM23";

    /// <summary>PAPSS-confirmed ISO codes (the second column of the portal's Error Codes table) that are always
    /// safe to forward unchanged, because PAPSS's own mapping already produces exactly this code.</summary>
    private static readonly HashSet<string> Confirmed = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACCP", "MS02", "MS03", "MS05", "AB05", "AB08", "TM01", "FF01", "DNOR", "CNOR", "RC01",
        "AM05", "AG09", "AG01", "AM02", "AG10", "RR04", "AM23"
    };

    public static string FromCoreBankReason(string? coreBankReason, string fallback = GenericError)
    {
        var trimmed = coreBankReason?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return fallback;

        if (Confirmed.Contains(trimmed)) return trimmed.ToUpperInvariant();

        // The core bank spells out its own reason in free text or a non-PAPSS code; only the one cause that
        // maps unambiguously (PAPSS table row 2000: AM23, "Insufficient funds (clearing)") is recognized here.
        if (trimmed.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "AC02", StringComparison.OrdinalIgnoreCase))
            return InsufficientFunds;

        return fallback;
    }
}
