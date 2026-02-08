namespace SIPS.ISO20022.Interfaces;

public interface IIncomingTransferHandler
{
    Task<string> Handle(string message, CancellationToken ct);
}
