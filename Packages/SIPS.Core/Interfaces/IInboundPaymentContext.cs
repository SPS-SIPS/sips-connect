namespace SIPS.Core.Interfaces;

/// <summary>
/// A host-authenticated rail can require a corebank acceptance decision independently of
/// the domestic IncludeCoreBankOnListing setting. Raw message headers do not set this policy.
/// </summary>
public interface IInboundPaymentContext
{
    bool RequiresCoreBankAcceptance { get; }
}

public sealed class DomesticInboundPaymentContext : IInboundPaymentContext
{
    public bool RequiresCoreBankAcceptance => false;
}
