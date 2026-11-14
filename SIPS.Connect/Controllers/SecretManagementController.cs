using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIPS.Connect.Services;

namespace SIPS.Connect.Controllers;

/// <summary>
/// Controller for managing encrypted secrets
/// WARNING: This should be protected and only accessible by administrators
/// </summary>
[ApiController]
[Route("api/admin/secrets")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Roles = KnownRoles.Admin)]
public class SecretManagementController(
    ISecretManagementService secretService,
    SecretManagementTool tool,
    ILogger<SecretManagementController> logger,
    IWebHostEnvironment environment) : ControllerBase
{
    private readonly ISecretManagementService _secretService = secretService;
    private readonly SecretManagementTool _tool = tool;
    private readonly ILogger<SecretManagementController> _logger = logger;

    /// <summary>
    /// Encrypt a single value
    /// </summary>
    [HttpPost("encrypt")]
    public IActionResult EncryptValue([FromBody] SecretRequest request)
    {
        try
        {
            var encrypted = _secretService.Encrypt(request.Value);
            _logger.LogInformation("Encrypted value for key: {Key}", request.Key);

            return Ok(new
            {
                key = request.Key,
                encrypted = encrypted,
                message = "Copy this encrypted value to your appsettings.json"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt value");
            return BadRequest(new { error = "Secret operation failed" });
        }
    }

    /// <summary>
    /// Decrypt a single value (for verification)
    /// </summary>
    [HttpPost("decrypt")]
    public IActionResult DecryptValue([FromBody] SecretRequest request)
    {
        if (!environment.IsDevelopment()) return NotFound();
        try
        {
            var decrypted = _secretService.Decrypt(request.Value);
            _logger.LogWarning("Decrypted value for key: {Key}", request.Key);

            return Ok(new
            {
                key = request.Key,
                decrypted = decrypted,
                warning = "This is sensitive information!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt value");
            return BadRequest(new { error = "Secret operation failed" });
        }
    }

    /// <summary>
    /// Encrypt all plain text secrets in appsettings.json
    /// </summary>
    [HttpPost("encrypt-all")]
    public async Task<IActionResult> EncryptAllSecrets()
    {
        try
        {
            var result = await _tool.EncryptSecretsAsync();

            if (result.Success)
            {
                _logger.LogInformation("Encrypted all secrets: {Message}", result.Message);
                return Ok(new
                {
                    success = true,
                    message = result.Message,
                    encryptedKeys = result.EncryptedKeys,
                    skippedKeys = result.SkippedKeys,
                    backupPath = result.BackupPath,
                    warning = "Application restart required for changes to take effect"
                });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt all secrets");
            return StatusCode(500, new { error = "Secret operation failed" });
        }
    }

    /// <summary>
    /// Decrypt all encrypted secrets in appsettings.json (for debugging)
    /// WARNING: Use with caution!
    /// </summary>
    [HttpPost("decrypt-all")]
    public async Task<IActionResult> DecryptAllSecrets()
    {
        if (!environment.IsDevelopment()) return NotFound();
        try
        {
            var result = await _tool.DecryptSecretsAsync();

            if (result.Success)
            {
                _logger.LogWarning("Decrypted all secrets: {Message}", result.Message);
                return Ok(new
                {
                    success = true,
                    message = result.Message,
                    decryptedKeys = result.EncryptedKeys,
                    backupPath = result.BackupPath,
                    warning = "Secrets are now in plain text! Re-encrypt before committing."
                });
            }

            return BadRequest(new { success = false, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt all secrets");
            return StatusCode(500, new { error = "Secret operation failed" });
        }
    }

    /// <summary>
    /// Get a secret value (decrypted)
    /// </summary>
    [HttpGet("{key}")]
    public IActionResult GetSecret(string key)
    {
        if (!environment.IsDevelopment()) return NotFound();
        try
        {
            // Replace : with / for URL compatibility
            key = key.Replace("/", ":");

            var value = _secretService.GetSecret(key);
            _logger.LogWarning("Retrieved secret: {Key}", key);

            return Ok(new
            {
                key = key,
                value = value,
                isEncrypted = _secretService.IsEncrypted(value),
                warning = "This is sensitive information!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secret");
            return BadRequest(new { error = "Secret operation failed" });
        }
    }
}

public class SecretRequest
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
