using System.Text.Json.Nodes;

namespace SIPS.Adapter;
public interface IJsonAdapter
{
    JsonObject Transform(JsonObject userJson, string endpointName);
    JsonObject Transform<T>(T localObject, string endpointName);
    T ToObject<T>(JsonObject json);
}
