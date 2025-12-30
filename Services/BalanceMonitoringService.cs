using SIPS.Connect.Models;
using SIPS.Core.Interfaces;

namespace SIPS.Connect.Services;

public class BalanceMonitoringService : IBalanceMonitoringService
{
    private readonly IRepositoryHttpClient _repositoryHttpClient;
    private readonly IAuthService _authService;
    private readonly ILogger<BalanceMonitoringService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _balanceEndpoint;

    public BalanceMonitoringService(
        IRepositoryHttpClient repositoryHttpClient,
        IAuthService authService,
        ILogger<BalanceMonitoringService> logger,
        IConfiguration configuration)
    {
        _repositoryHttpClient = repositoryHttpClient;
        _authService = authService;
        _logger = logger;
        _configuration = configuration;
        _balanceEndpoint = _configuration["Core:BalanceStatusEndpoint"] ?? "/v1/participants/balance-status";
    }

    public async Task<BalanceStatus?> GetBalanceStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching balance status from API");
            
            var (token, error) = await _authService.LoginAsync(cancellationToken);
            if (token == null || !string.IsNullOrEmpty(error))
            {
                _logger.LogError("Authentication failed: {Error}", error);
                return null;
            }

            var response = await _repositoryHttpClient.GetAsync<BalanceStatus>(_balanceEndpoint, cancellationToken);

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
