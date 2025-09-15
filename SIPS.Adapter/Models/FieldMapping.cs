namespace SIPS.Adapter.Models;

public class FieldMapping
{
    public string InternalField { get; set; } = string.Empty;
    public string UserField { get; set; } = string.Empty;
    // Backward-compatible string type (e.g., "string", "int", "datetime").
    // Prefer using EnumType for compile-time safety.
    public string Type { get; set; } = string.Empty;

    // New optional enum-based type to avoid magic strings.
    public MappingType? EnumType { get; set; }
}
