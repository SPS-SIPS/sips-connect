namespace SIPS.Core.Interfaces;

/// <summary>
/// Handles an asynchronous payee-verification report (acmt.024.001.03) received on the
/// incoming endpoint, e.g. the result of a cross-border PAPSS name enquiry, and delivers it
/// to the participant bank's callback.
/// </summary>
public interface IIncomingVerificationResponseHandler
{
    /// <returns>
    /// An empty string once the result has been delivered to the bank, or a signed admi.002
    /// when the report is rejected. Throws <see cref="SIPS.Core.Services.Callback.CallbackDeliveryException"/>
    /// when the bank callback could not be delivered so the sender can retry.
    /// </returns>
    Task<string> HandleAsync(string message, CancellationToken ct);
}

/// <summary>
/// Tells core handlers whether the current inbound message has already been authenticated by
/// the hosting application (for example by the PAPSS callback guard, which verifies the
/// WP-SIPS XAdES profile and signer provenance). When false, handlers verify the signature themselves.
/// </summary>
public interface IInboundAuthenticationContext
{
    bool IsPreAuthenticated { get; }
}

public sealed class NoInboundPreAuthentication : IInboundAuthenticationContext
{
    public bool IsPreAuthenticated => false;
}
