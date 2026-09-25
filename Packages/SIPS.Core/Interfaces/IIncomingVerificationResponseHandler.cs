namespace SIPS.Core.Interfaces;

/// <summary>
/// Handles an asynchronous payee-verification report (acmt.024.001.03) received on the
/// incoming endpoint, e.g. the result of a cross-border PAPSS name enquiry, and delivers it
/// to the participant bank's callback.
/// </summary>
public interface IIncomingVerificationResponseHandler
{
    /// <returns>
    /// An empty string once the result has been durably stored by an <see cref="IVerificationResultInbox"/>
    /// (pre-authenticated PAPSS path) or delivered to the bank inline, or a signed admi.002 when the report
    /// is rejected. On the inline path throws <see cref="SIPS.Core.Services.Callback.CallbackDeliveryException"/>
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

public enum VerificationResultInboxOutcome
{
    /// <summary>The inbox does not own this report; the handler delivers it inline (legacy behaviour).</summary>
    NotHandled,
    /// <summary>The report was durably stored and queued for asynchronous bank delivery.</summary>
    Stored,
    /// <summary>The same report (same source BizMsgIdr) was already stored; nothing new is queued.</summary>
    Duplicate
}

/// <summary>
/// Durable inbox for pre-authenticated asynchronous verification results (PAPSS). When it accepts
/// a report, the handler acknowledges the sender without delivering to the bank inline; delivery is
/// done later from an outbox with retries.
/// </summary>
public interface IVerificationResultInbox
{
    Task<VerificationResultInboxOutcome> AcceptAsync(
        string rawXml,
        SIPS.ISO20022.Helpers.PayeeVerificationResponseBuilder.Request report,
        SIPS.ISO20022.Models.DTOs.CB.CBVerificationResultDto result,
        CancellationToken ct);
}

public sealed class NoVerificationResultInbox : IVerificationResultInbox
{
    public Task<VerificationResultInboxOutcome> AcceptAsync(
        string rawXml,
        SIPS.ISO20022.Helpers.PayeeVerificationResponseBuilder.Request report,
        SIPS.ISO20022.Models.DTOs.CB.CBVerificationResultDto result,
        CancellationToken ct) => Task.FromResult(VerificationResultInboxOutcome.NotHandled);
}

/// <summary>Delivers a verification result to the participant bank callback (CB_VerificationResult).</summary>
public interface IVerificationResultDelivery
{
    /// <summary>Throws <see cref="SIPS.Core.Services.Callback.CallbackDeliveryException"/> when the bank did not acknowledge (2xx).</summary>
    Task DeliverAsync(SIPS.ISO20022.Models.DTOs.CB.CBVerificationResultDto result, CancellationToken ct);
}
