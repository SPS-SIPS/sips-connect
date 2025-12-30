using SIPS.Connect.Models;

namespace SIPS.Connect.Services;

public interface IBalanceMonitoringService
{
    Task<BalanceStatus?> GetBalanceStatusAsync(CancellationToken cancellationToken = default);
}
