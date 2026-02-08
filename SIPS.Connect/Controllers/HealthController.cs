using Microsoft.AspNetCore.Mvc;
using SIPS.Connect.Services;
using SIPS.Connect.Models;

namespace SIPS.Connect.Controllers;

[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    private readonly IHealthCheckService _healthCheckService;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IHealthCheckService healthCheckService,
        ILogger<HealthController> logger)
    {
        _healthCheckService = healthCheckService;
        _logger = logger;
    }

    /// <summary>
    /// Health check endpoint that returns the status of all registered components
    /// </summary>
    /// <returns>Health status of the API and its dependencies</returns>
    /// <response code="200">Returns health status (can be 'ok' or 'degraded')</response>
    /// <response code="503">Service unavailable if critical components are down</response>
    [HttpGet]
    [ProducesResponseType(typeof(HealthCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthCheckResponse>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var healthStatus = await _healthCheckService.CheckHealthAsync(cancellationToken);

            // Return 503 if any critical component is down
            if (healthStatus.Status == "degraded")
            {
                _logger.LogWarning("Health check returned degraded status");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, healthStatus);
            }

            return Ok(healthStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check endpoint failed");
            
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new HealthCheckResponse
            {
                Status = "error",
                Components = new List<ComponentHealth>
                {
                    new ComponentHealth
                    {
                        Name = "health-check-service",
                        Status = "error",
                        EndpointStatus = "error",
                        HttpResult = "Error",
                        LastChecked = DateTime.UtcNow,
                        ErrorMessage = ex.Message
                    }
                }
            });
        }
    }
}
