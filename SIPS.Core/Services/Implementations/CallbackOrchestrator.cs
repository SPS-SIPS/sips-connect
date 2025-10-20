using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIPS.Adapter;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Correlation;

namespace SIPS.Core.Services.Implementations;

public sealed class CallbackOrchestrator : ICallbackOrchestrator
{
    public async Task<SIPS.ISO20022.Models.DTOs.Response<JsonObject?>> SendJsonAsync(
        string url,
        IDictionary<string, string> headers,
        object dto,
        string transformKey,
        IJsonAdapter jsonAdapter,
        ICorrelationService correlation,
        JsonSerializerOptions serializerOptions,
        ICallbackClient callback,
        CancellationToken ct,
        string correlationId)
    {
        JsonObject md = jsonAdapter.Transform(dto, transformKey);
        var body = JsonSerializer.Serialize(md, serializerOptions);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var concreteHeaders = headers is Dictionary<string, string> d ? d : new Dictionary<string, string>(headers);
        var rsp = await callback.SendAsync(url, concreteHeaders, content, ct, correlationId);
        return rsp;
    }
}
