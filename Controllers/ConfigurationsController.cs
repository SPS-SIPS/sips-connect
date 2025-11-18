using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIPS.Connect.Config;
using SIPS.Connect.Services;
using SIPS.Core.Options;
using SIPS.ISO20022.Options;
using SIPS.XMLDsig.Xades.Options;
using static SIPS.Connect.KnownRoles;

namespace SIPS.Connect.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class ConfigurationsController(
    IConfiguration configuration,
    IWebHostEnvironment env,
    ISecretManagementService secretService) : ControllerBase
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ISecretManagementService _secretService = secretService;
    private readonly string _jsonFilePath = Path.Combine(env.ContentRootPath, "appsettings.json");
    private static readonly JsonSerializerOptions options = new() { WriteIndented = true };


    [HttpGet("Core")]
    // [Authorize (Roles = Admin)]
    public IActionResult GetCore()
    {
        var configs = new CoreOptions();
        _configuration.GetSection("Core").Bind(configs);

        // Decrypt sensitive fields for display
        if (!string.IsNullOrEmpty(configs.Username) && _secretService.IsEncrypted(configs.Username))
        {
            try
            {
                configs.Username = _secretService.Decrypt(configs.Username);
            }
            catch (Exception)
            {
                // If decryption fails, return as-is (might be corrupted or plain text)
            }
        }
        if (!string.IsNullOrEmpty(configs.Password) && _secretService.IsEncrypted(configs.Password))
        {
            try
            {
                configs.Password = _secretService.Decrypt(configs.Password);
            }
            catch (Exception)
            {
                // If decryption fails, return as-is (might be corrupted or plain text)
            }
        }

        return Ok(configs);
    }

    [HttpPut("Core")]
    // [Authorize (Roles = Admin)]
    public IActionResult ChangeCore([FromBody] CoreOptions request)
    {
        var fileContent = System.IO.File.ReadAllText(_jsonFilePath);
        if (System.Text.Json.Nodes.JsonNode.Parse(fileContent) is not System.Text.Json.Nodes.JsonObject jsonNode || jsonNode["Core"] == null)
        {
            return NotFound("No Core section exist in appsettings.json");
        }

        // Encrypt sensitive fields
        if (!string.IsNullOrEmpty(request.Username))
        {
            request.Username = _secretService.Encrypt(request.Username);
        }
        if (!string.IsNullOrEmpty(request.Password))
        {
            request.Password = _secretService.Encrypt(request.Password);
        }

        jsonNode["Core"] = JsonSerializer.SerializeToNode(request, options);
        var updatedJson = jsonNode.ToJsonString(options);
        System.IO.File.WriteAllText(_jsonFilePath, updatedJson);
        return Ok("Core options updated successfully.");
    }

    [HttpGet("Xades")]
    [Authorize (Roles = Admin)]
    public IActionResult GetXades()
    {
        var configs = new XadesOptions();
        _configuration.GetSection("Xades").Bind(configs);

        // Decrypt sensitive fields for display
        if (!string.IsNullOrEmpty(configs.PrivateKeyPassphrase) && _secretService.IsEncrypted(configs.PrivateKeyPassphrase))
        {
            try
            {
                configs.PrivateKeyPassphrase = _secretService.Decrypt(configs.PrivateKeyPassphrase);
            }
            catch (Exception)
            {
                // If decryption fails, return as-is
            }
        }

        return Ok(configs);
    }

    [HttpPut("Xades")]
    [Authorize (Roles = Admin)]
    public IActionResult ChangeXades([FromBody] XadesOptions request)
    {
        var fileContent = System.IO.File.ReadAllText(_jsonFilePath);
        if (System.Text.Json.Nodes.JsonNode.Parse(fileContent) is not System.Text.Json.Nodes.JsonObject jsonNode || jsonNode["Xades"] == null)
        {
            return NotFound("No Xades section exist in appsettings.json");
        }

        // Encrypt sensitive fields
        if (!string.IsNullOrEmpty(request.PrivateKeyPassphrase))
        {
            request.PrivateKeyPassphrase = _secretService.Encrypt(request.PrivateKeyPassphrase);
        }

        jsonNode["Xades"] = JsonSerializer.SerializeToNode(request, options);
        var updatedJson = jsonNode.ToJsonString(options);
        System.IO.File.WriteAllText(_jsonFilePath, updatedJson);
        return Ok("Xades options updated successfully.");
    }

    [HttpGet("ISO20022")]
    [Authorize (Roles = Admin)]
    public IActionResult GetISO20022()
    {
        var configs = new ISO20022Options();
        _configuration.GetSection("ISO20022").Bind(configs);

        // Decrypt sensitive fields for display
        if (!string.IsNullOrEmpty(configs.Key) && _secretService.IsEncrypted(configs.Key))
        {
            try
            {
                configs.Key = _secretService.Decrypt(configs.Key);
            }
            catch (Exception)
            {
                // If decryption fails, return as-is
            }
        }
        if (!string.IsNullOrEmpty(configs.Secret) && _secretService.IsEncrypted(configs.Secret))
        {
            try
            {
                configs.Secret = _secretService.Decrypt(configs.Secret);
            }
            catch (Exception)
            {
                // If decryption fails, return as-is
            }
        }

        return Ok(configs);
    }

    [HttpPut("ISO20022")]
    [Authorize (Roles = Admin)]
    public IActionResult PostISO20022([FromBody] ISO20022Options request)
    {
        var fileContent = System.IO.File.ReadAllText(_jsonFilePath);
        if (System.Text.Json.Nodes.JsonNode.Parse(fileContent) is not System.Text.Json.Nodes.JsonObject jsonNode || jsonNode["ISO20022"] == null)
        {
            return NotFound("No ISO20022 options exist in appsettings.json");
        }

        // Encrypt sensitive fields
        if (!string.IsNullOrEmpty(request.Key))
        {
            request.Key = _secretService.Encrypt(request.Key);
        }
        if (!string.IsNullOrEmpty(request.Secret))
        {
            request.Secret = _secretService.Encrypt(request.Secret);
        }

        jsonNode["ISO20022"] = JsonSerializer.SerializeToNode(request, options);
        var updatedJson = jsonNode.ToJsonString(options);
        System.IO.File.WriteAllText(_jsonFilePath, updatedJson);
        // Reload the configuration to reflect the changes
        return Ok("ISO20022 options updated successfully.");
    }

    [HttpGet("Emv")]
    [Authorize (Roles = Admin)]
    public IActionResult GetEmv()
    {
        var configs = new EmvOptions();
        _configuration.GetSection("Emv").Bind(configs);

        return Ok(configs);
    }

    [HttpPut("Emv")]
    [Authorize (Roles = Admin)]
    public IActionResult ChangeEmv([FromBody] EmvOptions request)
    {
        var fileContent = System.IO.File.ReadAllText(_jsonFilePath);
        if (System.Text.Json.Nodes.JsonNode.Parse(fileContent) is not System.Text.Json.Nodes.JsonObject jsonNode || jsonNode["Emv"] == null)
        {
            return NotFound("No Emv section exist in appsettings.json");
        }

        jsonNode["Emv"] = JsonSerializer.SerializeToNode(request, options);
        var updatedJson = jsonNode.ToJsonString(options);
        System.IO.File.WriteAllText(_jsonFilePath, updatedJson);
        return Ok("Emv options updated successfully.");
    }

    [HttpGet("Origins")]
    [Authorize (Roles = Admin)]
    public IActionResult GetOrigins()
    {
        var configs = new CorsPolicies();
        _configuration.GetSection("CorsPolicies").Bind(configs);

        return Ok(configs);
    }

    [HttpPut("Origins")]
    [Authorize (Roles = Admin)]
    public IActionResult ChangeEmv([FromBody] CorsPolicies request)
    {
        var fileContent = System.IO.File.ReadAllText(_jsonFilePath);
        if (System.Text.Json.Nodes.JsonNode.Parse(fileContent) is not System.Text.Json.Nodes.JsonObject jsonNode || jsonNode["CorsPolicies"] == null)
        {
            return NotFound("No Cors Policies section exist in appsettings.json");
        }

        jsonNode["CorsPolicies"] = JsonSerializer.SerializeToNode(request, options);
        var updatedJson = jsonNode.ToJsonString(options);
        System.IO.File.WriteAllText(_jsonFilePath, updatedJson);
        return Ok("Cors Policies options updated successfully.");
    }

    [HttpGet("Hosts")]
    [Authorize (Roles = Admin)]
    public IActionResult AllowedHosts()
    {
        var allowedHosts = _configuration["AllowedHosts"];
        return Ok(allowedHosts);
    }

    [HttpPut("Hosts")]
    [Authorize (Roles = Admin)]
    public IActionResult ChangeHosts([FromBody] string[] hosts)
    {
        var fileContent = System.IO.File.ReadAllText(_jsonFilePath);
        if (System.Text.Json.Nodes.JsonNode.Parse(fileContent) is not System.Text.Json.Nodes.JsonObject jsonNode || jsonNode["AllowedHosts"] == null)
        {
            return NotFound("No AllowedHosts section exist in appsettings.json");
        }

        jsonNode["AllowedHosts"] = string.Join(";", hosts);
        var updatedJson = jsonNode.ToJsonString(options);
        System.IO.File.WriteAllText(_jsonFilePath, updatedJson);
        return Ok("AllowedHosts options updated successfully.");
    }

    [HttpGet("APIKeys")]
    // [Authorize(Roles = Admin)]
    public IActionResult APIKeys([FromQuery] string? key)
    {
        var keys = _configuration.GetSection("ApiKeys").Get<List<ApiKey>>() ?? [];

        // Decrypt secrets for display
        foreach (var k in keys)
        {
            if (!string.IsNullOrEmpty(k.Secret) && _secretService.IsEncrypted(k.Secret))
            {
                try
                {
                    k.Secret = _secretService.Decrypt(k.Secret);
                }
                catch (Exception)
                {
                    // If decryption fails, keep original value
                }
            }
        }

        if (string.IsNullOrEmpty(key))
        {
            return Ok(keys);
        }

        var apiKey = keys.FirstOrDefault(k => k.Key == key);
        return apiKey is not null ? Ok(apiKey) : NotFound("API Key not found.");
    }

    [HttpPut("APIKeys")]
    // [Authorize(Roles = Admin)]
    public IActionResult ChangeAPIKeys([FromBody] ApiKey request)
    {
        var fileContent = System.IO.File.ReadAllText(_jsonFilePath);

        if (System.Text.Json.Nodes.JsonNode.Parse(fileContent) is not System.Text.Json.Nodes.JsonObject jsonNode)
        {
            jsonNode = [];
        }

        if (jsonNode["ApiKeys"] == null)
        {
            // create ApiKeys section
            jsonNode["ApiKeys"] = JsonSerializer.SerializeToNode(new List<ApiKey>(), options);
        }

        // Encrypt sensitive fields
        if (!string.IsNullOrEmpty(request.Secret))
        {
            request.Secret = _secretService.Encrypt(request.Secret);
        }

        var keys = jsonNode["ApiKeys"].Deserialize<List<ApiKey>>() ?? [];
        var existing = keys.FirstOrDefault(k => k.Key == request.Key);
        var isUpdate = existing is not null;
        if (isUpdate && existing != null)
        {
            existing.Name = request.Name;
            existing.Secret = request.Secret;
        }
        else
        {
            keys.Add(request);
        }
        jsonNode["ApiKeys"] = JsonSerializer.SerializeToNode(keys, options);
        var updatedJson = jsonNode.ToJsonString(options);
        System.IO.File.WriteAllText(_jsonFilePath, updatedJson);
        return Ok(isUpdate ? "API Key updated successfully." : "API Key added successfully.");
    }

    [HttpDelete("APIKeys")]
    [Authorize(Roles = Admin)]
    public IActionResult DeleteAPIKeys([FromQuery] string key)
    {
        var fileContent = System.IO.File.ReadAllText(_jsonFilePath);
        if (System.Text.Json.Nodes.JsonNode.Parse(fileContent) is not System.Text.Json.Nodes.JsonObject jsonNode || jsonNode["ApiKeys"] == null)
        {
            return NotFound("No ApiKeys section exist in appsettings.json");
        }

        var keys = jsonNode["ApiKeys"].Deserialize<List<ApiKey>>() ?? [];
        var apiKey = keys.FirstOrDefault(k => k.Key == key);
        if (apiKey is null)
        {
            return NotFound("API Key not found.");
        }

        keys.Remove(apiKey);
        jsonNode["ApiKeys"] = JsonSerializer.SerializeToNode(keys, options);
        var updatedJson = jsonNode.ToJsonString(options);
        System.IO.File.WriteAllText(_jsonFilePath, updatedJson);
        return Ok("API Key deleted successfully.");
    }

}