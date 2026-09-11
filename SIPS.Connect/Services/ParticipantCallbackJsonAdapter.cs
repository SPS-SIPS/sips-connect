using System.Text.Json.Nodes;
using SIPS.Adapter;
using SIPS.Adapter.Models;
using SIPS.Core.Services.Callback;
using SIPS.ISO20022.Models.DTOs;

namespace SIPS.Connect.Services;

public interface IParticipantCallbackContext
{
    PapssParticipantBinding? Binding { get; }
    IDisposable Push(PapssParticipantBinding binding);
}

public sealed class ParticipantCallbackContext : IParticipantCallbackContext
{
    private readonly AsyncLocal<PapssParticipantBinding?> _binding = new();
    public PapssParticipantBinding? Binding => _binding.Value;
    public IDisposable Push(PapssParticipantBinding binding)
    {
        var previous = _binding.Value; _binding.Value = binding;
        return new Scope(() => _binding.Value = previous);
    }
    private sealed class Scope(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}

public sealed class ParticipantCallbackJsonAdapter(JsonAdapter inner, JsonAdapterOptions options, IParticipantCallbackContext context) : IJsonAdapter
{
    private string Route(string endpoint)
    {
        if (context.Binding?.CallbackMappingProfile is not { Length: > 0 } profile) return endpoint;
        var routed = $"{profile}.{endpoint}";
        return options.Endpoints.ContainsKey(routed) ? routed : throw new InvalidOperationException($"Callback mapping '{routed}' is not configured.");
    }
    public JsonObject Transform(JsonObject userJson, string endpointName) => inner.Transform(userJson, Route(endpointName));
    public JsonObject Transform<T>(T localObject, string endpointName) => inner.Transform(localObject, Route(endpointName));
    public T ToObject<T>(JsonObject json) => inner.ToObject<T>(json);
}

public sealed class ParticipantCallbackClient(CallbackClient inner, IParticipantCallbackContext context) : ICallbackClient
{
    public Task<Response<JsonObject?>> SendAsync(string url, Dictionary<string, string> headers, StringContent content, CancellationToken ct, string? correlationId = null)
    {
        var destination = context.Binding?.CallbackUrl ?? url;
        if (context.Binding is not null && string.IsNullOrWhiteSpace(context.Binding.CallbackUrl))
            throw new InvalidOperationException("The PAPSS participant callback URL is not configured.");
        return inner.SendAsync(destination, headers, content, ct, correlationId);
    }
}
