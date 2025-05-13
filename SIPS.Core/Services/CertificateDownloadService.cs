using SIPS.Core.Interfaces;
using SIPS.Core.Models;
using SIPS.Core.Options;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Models;
using Microsoft.Extensions.Logging;

namespace SIPS.Core.Services;
public class CertificateDownloadService(CoreOptions options, ILogger<CertificateDownloadService> logger, IRepositoryHttpClient httpService, IAuthService authService, ICacheService cacheService) : ICertificateDownloadService
{
    private readonly ILogger<CertificateDownloadService> _logger = logger;
    private readonly IRepositoryHttpClient _httpService = httpService;
    private readonly CoreOptions _options = options;
    private readonly IAuthService _authService = authService;
    private readonly ICacheService _cacheService = cacheService;

    public async Task<(CertificateDownloadResponse? Certificates, string? Error)> GetCertificatesAsync(string sn, CancellationToken cancellationToken = default)
    {
        var url = _options.PublicKeysRepUrl;
        if (string.IsNullOrEmpty(url))
        {
            _logger.LogError("Invalid configuration for certificates.");
            return (null, "Invalid configuration for certificates.");
        }
        // Check if the certificates are already cached
        var certificates = await GetCertificatesFromCacheAsync(sn, cancellationToken);

        if (certificates != null)
        {
            return (certificates, null);
        }

        // Login to the API
        var (_, Error) = _ = await _authService.LoginAsync(cancellationToken);
        if (Error != null)
        {
            return (null, Error);
        }

        CertificateRequest request = new(sn, "");

        var response = await _httpService.PostAsync<CertificateRequest, CertificateDownloadResponse>(_options.PublicKeysRepUrl!, request, cancellationToken);

        if (!response.IsSuccess)
        {
            _logger.LogError("Failed to get certificates: {Error}", response.Message);
            return (null, response.Message);
        }

        // Cache the certificates for future use
        await _cacheService.SetAsync($"certificates:{sn}", response.Data, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(response.CacheInMins)
        }, cancellationToken);

        return (response.Data, null);
    }

    private async Task<CertificateDownloadResponse?> GetCertificatesFromCacheAsync(string sn, CancellationToken cancellationToken = default)
    {
        var response = await _cacheService.GetAsync<CertificateDownloadResponse>($"certificates:{sn}", cancellationToken);
        if (response == null)
        {
            _logger.LogWarning("Certificates not found in cache SN: {SN}.", sn);
            return null;
        }

        _logger.LogInformation("Certificates found in cache SN: {SN}.", sn);
        return response;
    }
}