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

public sealed class ParticipantCallbackContext : IParticipantCallbackContext, SIPS.Core.Interfaces.IInboundAuthenticationContext
{
    private readonly AsyncLocal<PapssParticipantBinding?> _binding = new();
    public PapssParticipantBinding? Binding => _binding.Value;
    // A binding is only pushed after PapssCallbackGuard verified the WP-SIPS XAdES signature,
    // signer provenance and local destination of the inbound PAPSS callback.
    public bool IsPreAuthenticated => _binding.Value is not null;
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
    public Task<Response<JsonObject?>> SendAsync(string url, Dictionary<string, string> headers, StringContent content, CancellationToken ct, string? correlationId = null, bool bypassParticipantBinding = false)
    {
        // bypassParticipantBinding=true is a question asked of the bank's own corebank (e.g. CB_Verify, answering
        // an inbound PAPSS enquiry): it must reach the exact url its caller built from the configured corebank
        // endpoint, never the participant's bank-notification CallbackUrl. Previously this override applied
        // unconditionally to every call made while a PAPSS participant binding was active - which is correct for
        // delivering an outcome/notification to the bank (e.g. IncomingVerificationResponseHandler.DeliverAsync,
        // which deliberately reuses options.Verification as its own url and relies on this override to redirect
        // it to the bank's CallbackUrl) but silently redirected CoreBankVerificationClient's /CB/Verify corebank
        // lookup to that same CallbackUrl too, so every inbound PAPSS acmt.023 enquiry was answered from whatever
        // the CallbackUrl happened to return instead of a real corebank verification.
        if (bypassParticipantBinding) return inner.SendAsync(url, headers, content, ct, correlationId);
        var destination = context.Binding?.CallbackUrl ?? url;
        if (context.Binding is not null && string.IsNullOrWhiteSpace(context.Binding.CallbackUrl))
            throw new InvalidOperationException("The PAPSS participant callback URL is not configured.");
        return inner.SendAsync(destination, headers, content, ct, correlationId);
    }
}
