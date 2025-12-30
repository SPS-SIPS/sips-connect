using SIPS.Connect.Models;
using SIPS.Core.Interfaces;

namespace SIPS.Connect.Services;

public class BalanceMonitoringService : IBalanceMonitoringService
{
    private readonly IRepositoryHttpClient _repositoryHttpClient;
    private readonly ILogger<BalanceMonitoringService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _balanceEndpoint;

    public BalanceMonitoringService(
        IRepositoryHttpClient repositoryHttpClient,
        ILogger<BalanceMonitoringService> logger,
        IConfiguration configuration)
    {
        _repositoryHttpClient = repositoryHttpClient;
        _logger = logger;
        _configuration = configuration;
        _balanceEndpoint = _configuration["Core:BalanceStatusEndpoint"] ?? "/v1/participants/balance-status";
    }

    public async Task<BalanceStatus?> GetBalanceStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching balance status from API");
            
            var response = await _repositoryHttpClient.PostEmptyAsync<BalanceStatus>(_balanceEndpoint, cancellationToken);

            if (response?.Data != null)
            {
                _logger.LogInformation(
                    "Successfully fetched balance status - BIC: {Bic}, Zone: {Zone}, Balance: {Balance}", 
                    response.Data.Bic, 
                    response.Data.CurrentZone, 
                    response.Data.LastKnownBalance);
                
                return response.Data;
            }

            _logger.LogWarning("API returned unsuccessful response or no data");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching balance status from API");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching balance status from API");
            throw;
        }
    }
}
