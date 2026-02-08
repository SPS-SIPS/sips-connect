namespace SIPS.ISO20022.Interfaces;

public interface IIncomingTransactionStatusHandler
{
    Task<string> HandleAsync(string message, CancellationToken ct);
}