namespace SIPS.ISO20022.Interfaces;

public interface IIncomingReturnTransactionHandler
{
    Task<string> HandleAsync(string message, CancellationToken ct);
}