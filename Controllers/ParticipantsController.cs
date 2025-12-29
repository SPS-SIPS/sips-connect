using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIPS.Connect.Models;
using SIPS.Connect.Services;

namespace SIPS.Connect.Controllers;

[ApiController]
[Produces("application/json")]
[Route("api/v1/[controller]")]
[Authorize]
public class ParticipantsController : ControllerBase
{
    private readonly ILiveParticipantsService _liveParticipantsService;
    private readonly ILogger<ParticipantsController> _logger;

    public ParticipantsController(
        ILiveParticipantsService liveParticipantsService,
        ILogger<ParticipantsController> logger)
    {
        _liveParticipantsService = liveParticipantsService;
        _logger = logger;
    }

    [HttpGet("live")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetLiveParticipants(
        [FromQuery] bool? IsLive = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching live participants with filter: IsLive={IsLive}", IsLive);
            
            var participants = await _liveParticipantsService.GetLiveParticipantsAsync(IsLive, cancellationToken);
            
            _logger.LogInformation("Successfully retrieved {Count} participants", participants.Count);
            
            return Ok(new { data = participants, succeeded = true, message = (string?)null, errors = (string[]?)null });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving live participants");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { data = (object?)null, succeeded = false, message = "Internal server error", errors = new[] { "An unexpected error occurred" } }
            );
        }
    }

    [HttpGet("live/{bic}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> IsParticipantLive(
        string bic,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(bic))
            {
                return BadRequest(new { data = (object?)null, succeeded = false, message = "Invalid request", errors = new[] { "BIC cannot be empty" } });
            }

            _logger.LogInformation("Checking if participant {BIC} is live", bic);
            
            var isLive = await _liveParticipantsService.IsParticipantLiveAsync(bic, cancellationToken);
            
            var result = new
            {
                institutionBic = bic,
                isLive = isLive
            };
            
            return Ok(new { data = result, succeeded = true, message = (string?)null, errors = (string[]?)null });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if participant {BIC} is live", bic);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { data = (object?)null, succeeded = false, message = "Internal server error", errors = new[] { "An unexpected error occurred" } }
            );
        }
    }

    [HttpGet("live/available/bics")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAvailableParticipantBics(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching available participant BICs");
            
            var bics = await _liveParticipantsService.GetAvailableParticipantBicsAsync(cancellationToken);
            
            _logger.LogInformation("Successfully retrieved {Count} available BICs", bics.Count);
            
            return Ok(new { data = bics, succeeded = true, message = (string?)null, errors = (string[]?)null });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving available participant BICs");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { data = (object?)null, succeeded = false, message = "Internal server error", errors = new[] { "An unexpected error occurred" } }
            );
        }
    }
}
