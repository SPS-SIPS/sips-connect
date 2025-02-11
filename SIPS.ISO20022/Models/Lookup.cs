namespace SIPS.ISO20022.Models;
public sealed class Lookup<T>
{
    public Lookup()
    {
    }
    public Lookup(T id, string name, string groupId, decimal? ratioOrBalance = null, List<Lookup<T>>? children = null, bool isDefault = false)
    {
        Value = id;
        Label = name;
        GroupId = groupId;
        RatioOrBalance = ratioOrBalance;
        Children = children;
        IsDefault = isDefault;
    }

    public T? Value { get; set; }
    public string Label { get; set; } = null!;
    public string GroupId { get; set; } = string.Empty;
    public decimal? RatioOrBalance { get; set; }
    public bool IsDefault { get; set; } = false;
    public List<Lookup<T>>? Children { get; set; }
}
