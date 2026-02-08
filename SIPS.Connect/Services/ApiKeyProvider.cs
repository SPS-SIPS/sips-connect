using SIPS.Connect.Config;

namespace SIPS.Connect.Services;

/// <summary>
/// Provides API keys from configuration with automatic decryption and reload support
/// </summary>
public interface IApiKeyProvider
{
    List<ApiKey> GetApiKeys();
}

public class ApiKeyProvider : IApiKeyProvider
{
    private readonly IConfiguration _configuration;
    private readonly ISecretManagementService _secretService;

    public ApiKeyProvider(IConfiguration configuration, ISecretManagementService secretService)
    {
        _configuration = configuration;
        _secretService = secretService;
    }

    public List<ApiKey> GetApiKeys()
    {
        var apiKeys = _configuration.GetSection("ApiKeys").Get<List<ApiKey>>() ?? new List<ApiKey>();

        // Decrypt secrets
        foreach (var key in apiKeys)
        {
            if (!string.IsNullOrEmpty(key.Secret) && _secretService.IsEncrypted(key.Secret))
            {
                try
                {
                    key.Secret = _secretService.Decrypt(key.Secret);
                }
                catch (Exception)
                {
                    // If decryption fails, keep original value
                }
            }
        }

        return apiKeys;
    }
}
