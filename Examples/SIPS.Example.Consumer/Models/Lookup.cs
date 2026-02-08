namespace IPS.Domain.Models.DTOs;

public sealed class Lookup<T>
{
    public Lookup()
    {
    }
    public Lookup(T id, string name, List<Lookup<T>>? children = null, bool isDefault = false)
    {
        Value = id;
        Label = name;
        Children = children;
        IsDefault = isDefault;
    }

    public T? Value { get; set; }
    public string Label { get; set; } = null!;
    public bool IsDefault { get; set; } = false;
    public List<Lookup<T>>? Children { get; set; }
}
