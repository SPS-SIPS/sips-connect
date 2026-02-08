namespace SIPS.Connect.Config;
public sealed class ApiKeys(List<ApiKey> keys)
{
    public List<ApiKey> Keys { get; set; } = keys;
}
