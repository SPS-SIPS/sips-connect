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

    public async Task<(CertificateDownloadResponse? Certificates, string? Error)> GetCertificatesAsync(string sn, string issuerDN, CancellationToken cancellationToken = default)
    {
        var serialNumber = sn.Trim();
        var normalizedIssuerDN = NormalizeIssuerDN(issuerDN);
        var cacheKey = CertificateCacheKey(serialNumber, normalizedIssuerDN);

        // Step 1: Validate configuration
        var url = _options.PublicKeysRepUrl;
        if (string.IsNullOrEmpty(url))
        {
            _logger.LogError("Invalid configuration for certificates.");
            return (null, "Invalid configuration for certificates.");
        }

        // Step 2: Check if the certificates are already cached
        // BPC Specification: Cache keying should be (issuer/serial)
        var certificates = await GetCertificatesFromCacheAsync(serialNumber, normalizedIssuerDN, cacheKey, cancellationToken);
        if (certificates != null)
            return (certificates, null);

        // Step 3: Login to the API
        var loginResult = await _authService.LoginAsync(cancellationToken);
        if (loginResult.Error != null)
            return (null, loginResult.Error);

        // [FIX]: Inject the retrieved access token into the repository client
        if (loginResult.Token != null && !string.IsNullOrEmpty(loginResult.Token.AccessToken))
        {
            _logger.LogInformation("Authentication successful. Attaching Bearer token (Length: {Length}) to certificate request.", loginResult.Token.AccessToken.Length);
            _httpService.AddAuthHeaders(loginResult.Token.AccessToken);
        }
        else
        {
            _logger.LogWarning("Authentication failed or returned empty token. Proceeding without Auth header (Likely 401). Error: {Error}", loginResult.Error);
        }

        // Step 4: Build request and call API (with 401 retry logic)
        _logger.LogInformation("Sending certificate download request to {Url}. SN: {SN}, Issuer: {Issuer}", url, sn, issuerDN);
        var request = new CertificateRequest(serialNumber, issuerDN.Trim());
        var response = await _httpService.PostAsync<CertificateRequest, CertificateDownloadResponse>(url, request, cancellationToken);

        // [HARDENING]: Handle 401 Unauthorized via token refresh
        if (response.StatusCode == 401)
        {
            _logger.LogWarning("Certificate download returned 401 Unauthorized. Forcing token refresh and retrying...");
            
            // Clear cache and re-login
            await _authService.ClearCacheAsync(cancellationToken);
            var retryLogin = await _authService.LoginAsync(cancellationToken);
            
            if (retryLogin.Error == null && retryLogin.Token != null)
            {
                _logger.LogInformation("Re-authentication successful. Retrying certificate download...");
                _httpService.AddAuthHeaders(retryLogin.Token.AccessToken);
                response = await _httpService.PostAsync<CertificateRequest, CertificateDownloadResponse>(url, request, cancellationToken);
            }
        }

        if (!response.IsSuccess)
        {
            _logger.LogError("Failed to get certificates: {Error} (Status: {Status})", response.Message, response.StatusCode);
            return (null, response.Message);
        }

        // Step 5: Cache the certificates for future use
        // BPC Specification: Mandatory cache/refresh only when missing or older than 1 hour.
        var cacheDurationMins = Math.Max(response.CacheInMins, 60);

        await _cacheService.SetAsync(cacheKey, response.Data, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(cacheDurationMins)
        }, cancellationToken);

        // Step 6: Return the certificates
        return (response.Data, null);
    }

    private async Task<CertificateDownloadResponse?> GetCertificatesFromCacheAsync(string sn, string issuerDN, string cacheKey, CancellationToken cancellationToken = default)
    {
        var response = await _cacheService.GetAsync<CertificateDownloadResponse>(cacheKey, cancellationToken);
        if (response == null)
        {
            _logger.LogInformation("Certificates not found in cache SN: {SN}, Issuer: {Issuer}.", sn, issuerDN);
            return null;
        }

        _logger.LogInformation("Certificates found in cache SN: {SN}, Issuer: {Issuer}.", sn, issuerDN);
        return response;
    }

    private static string CertificateCacheKey(string sn, string issuerDN)
    {
        return $"certificates:{sn}:{issuerDN}";
    }

    internal static string NormalizeIssuerDN(string issuerDN)
    {
        return string.Join(",",
            issuerDN
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(CollapseWhitespace));
    }

    private static string CollapseWhitespace(string value)
    {
        var normalized = value.Trim();
        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized;
    }
}
