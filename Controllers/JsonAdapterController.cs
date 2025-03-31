using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using static SIPS.Connect.Helpers.APIAuth;

namespace SIPS.Connect.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdapterController(IConfiguration configuration, IWebHostEnvironment env) : ControllerBase
{
    private readonly IConfiguration _configuration = configuration;
    private readonly string _jsonFilePath = Path.Combine(env.ContentRootPath, "jsonAdapter.json");

    [HttpGet]
    public IActionResult GetEndpoints()
    {
        if (!Request.IsApiAuthorized(_configuration)) { return Unauthorized(); }
        var endpoints = new Dictionary<string, Endpoint>();
        _configuration.GetSection("Endpoints").Bind(endpoints);

        if (endpoints.Count == 0)
        {
            return NotFound("No endpoints found in configuration.");
        }

        return Ok(endpoints);
    }

    [HttpPost("updateEndpoints")]
    public IActionResult UpdateEndpoints([FromBody] Root request)
    {
        if (!Request.IsApiAuthorized(_configuration)) { return Unauthorized(); }

        var fileContent = System.IO.File.ReadAllText(_jsonFilePath);
        var existingData = JsonSerializer.Deserialize<Root>(fileContent);
        if (existingData?.Endpoints == null)
        {
            return NotFound("No endpoints exist in jsonAdapter.json");
        }

        foreach (var (endpointKey, requestEndpoint) in request.Endpoints)
        {
            if (!existingData.Endpoints.TryGetValue(endpointKey, out var existingEndpoint))
            {
                return BadRequest($"Endpoint '{endpointKey}' not found.");
            }

            foreach (var reqMapping in requestEndpoint.FieldMappings)
            {
                var existingMapping = existingEndpoint.FieldMappings
                    .FirstOrDefault(m => m.InternalField == reqMapping.InternalField);

                if (existingMapping == null)
                {
                    return BadRequest($"InternalField '{reqMapping.InternalField}' not found in endpoint '{endpointKey}'.");
                }

                existingMapping.UserField = reqMapping.UserField;
            }
        }

        var updatedJson = JsonSerializer.Serialize(existingData, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(_jsonFilePath, updatedJson);

        return Ok("Endpoints updated successfully.");
    }

    public class Root
    {
        public Dictionary<string, Endpoint> Endpoints { get; set; } = [];
    }

    public class Endpoint
    {
        public List<FieldMapping> FieldMappings { get; set; } = [];
    }

    public class FieldMapping
    {
        public string InternalField { get; set; } = string.Empty;
        public string UserField { get; set; } = string.Empty;
    }
}