namespace SIPS.Adapter.Models;
public class JsonAdapterOptions
{
    public Dictionary<string, EndpointMapping> Endpoints { get; set; } = [];
    public string[] DateFormats { get; set; } = [];
}
