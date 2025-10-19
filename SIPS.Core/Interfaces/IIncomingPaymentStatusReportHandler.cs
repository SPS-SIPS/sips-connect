namespace SIPS.Core.Interfaces;

public interface IIncomingPaymentStatusReportHandler
{
    Task<string> HandleAsync(string message, CancellationToken ct);
}
