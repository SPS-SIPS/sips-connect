namespace SIPS.ISO20022.Interfaces;
public interface IIncomingVerificationHandler
{
    Task<string> HandleAsync(string message, CancellationToken ct);
}