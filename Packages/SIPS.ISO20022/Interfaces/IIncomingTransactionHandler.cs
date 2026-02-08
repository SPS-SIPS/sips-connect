namespace SIPS.ISO20022.Interfaces;

public interface IIncomingTransactionHandler
{
    Task<string> HandleAsync(string message, CancellationToken ct);
}
