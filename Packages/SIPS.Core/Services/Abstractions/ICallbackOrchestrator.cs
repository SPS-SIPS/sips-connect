using System.Text.Json.Nodes;

namespace SIPS.Core.Services.Abstractions;

public interface ICallbackOrchestrator
{
    Task<SIPS.ISO20022.Models.DTOs.Response<JsonObject?>> SendJsonAsync(
        string url,
        IDictionary<string, string> headers,
        object dto,
        string transformKey,
        SIPS.Adapter.IJsonAdapter jsonAdapter,
        SIPS.Core.Services.Correlation.ICorrelationService correlation,
        System.Text.Json.JsonSerializerOptions serializerOptions,
        SIPS.Core.Services.Callback.ICallbackClient callback,
        System.Threading.CancellationToken ct,
        string correlationId);
}
