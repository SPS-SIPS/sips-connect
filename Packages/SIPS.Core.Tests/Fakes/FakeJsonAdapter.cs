using System.Collections.Generic;
using System.Text.Json.Nodes;
using SIPS.Adapter;

namespace SIPS.Core.Tests.Fakes;

/// <summary>
/// Fake implementation of IJsonAdapter for testing purposes.
/// Allows setting up responses for specific types without complex Moq generic mocking.
/// </summary>
public class FakeJsonAdapter : IJsonAdapter
{
    private readonly Dictionary<System.Type, object> _toObjectResponses = new();

    /// <summary>
    /// Set the response that ToObject should return for a specific type.
    /// </summary>
    public void SetToObjectResponse<T>(T response) where T : class
    {
        _toObjectResponses[typeof(T)] = response;
    }

    /// <summary>
    /// Transform simply returns the input JsonObject (pass-through for tests).
    /// </summary>
    public JsonObject Transform(JsonObject userJson, string endpointName)
    {
        return userJson;
    }

    /// <summary>
    /// Transform from object to JsonObject (not used in current tests).
    /// </summary>
    public JsonObject Transform<T>(T localObject, string endpointName)
    {
        return new JsonObject();
    }

    /// <summary>
    /// ToObject returns the pre-configured response for the requested type.
    /// </summary>
    public T ToObject<T>(JsonObject json)
    {
        if (_toObjectResponses.TryGetValue(typeof(T), out var response))
        {
            return (T)response;
        }
        return default!;
    }
}
