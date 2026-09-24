using System.Net;

namespace SIPS.Core.Services.Callback;

/// <summary>
/// Raised when a participant callback could not be delivered (transport failure or non-2xx),
/// so the caller can signal a retryable failure to the message originator.
/// </summary>
public sealed class CallbackDeliveryException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
