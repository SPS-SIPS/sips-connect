using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIPS.Connect.Config;
using SIPS.Core.Options;
using SIPS.ISO20022.Options;
using SIPS.XMLDsig.Xades.Options;
using static SIPS.Connect.KnownRoles;

namespace SIPS.Connect.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class ConfigurationsController(IConfiguration configuration, IWebHostEnvironment env) : ControllerBase
{
    private readonly IConfiguration _configuration = configuration;
    private readonly string _jsonFilePath = Path.Combine(env.ContentRootPath, "appsettings.json");
    private static readonly JsonSerializerOptions options = new() { WriteIndented = true };


    [HttpGet("Core")]
    [Authorize (Roles = Admin)]
    public IActionResult GetCore()
    {
        var configs = new CoreOptions();
        _configuration.GetSection("Core").Bind(configs);

        return Ok(configs);
    }

    [HttpPut("Core")]
    [Authorize (Roles = Admin)]
    public IActionResult ChangeCore([FromBody] CoreOptions request)
    {
        var fileContent = System.IO.File.ReadAllText(_jsonFilePath);
        if (System.Text.Json.Nodes.JsonNode.Parse(fileContent) is not System.Text.Json.Nodes.JsonObject jsonNode || jsonNode["Core"] == null)
        {
            return NotFound("No Core section exist in appsettings.json");
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
        var configs = new EmvOptions();
        _configuration.GetSection("Xades").Bind(configs);

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
}