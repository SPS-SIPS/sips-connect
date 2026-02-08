namespace SIPS.Emv.Attributes;

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class EmvSpecificationAttribute(int id) : Attribute
{
    public int Id { get; } = id;

    public int MaxLength { get; set; }

    public bool IsParent { get; set; }
}
