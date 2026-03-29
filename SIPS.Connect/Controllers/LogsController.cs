using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIPS.Connect.Services;

namespace SIPS.Connect.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = KnownRoles.Logs)]
public class LogsController(ILogService logService, ILogger<LogsController> logger) : ControllerBase
{
    [HttpGet("files")]
    public async Task<IActionResult> GetLogFiles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "desc",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 100) pageSize = 10;

            var result = await logService.GetLogFilesAsync(page, pageSize, search, sort);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve log files");
            return StatusCode(500, "Internal server error");
        }
    }
    
    [HttpGet("download/{fileName}")]
    public async Task<IActionResult> DownloadLogFile(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await logService.DownloadLogFileAsync(fileName, cancellationToken);

            if (stream == null)
            {
                logger.LogWarning("Downloaded file is null: {FileName}", fileName);
                return NotFound(new { Message = "File not found." });
            }

            return File(stream, "application/octet-stream", fileName);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid file name: {FileName}", fileName);
            return BadRequest(new { ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            logger.LogWarning(ex, "Log file not found: {FileName}", fileName);
            return NotFound(new { ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download log file: {FileName}", fileName);
            return StatusCode(500, "Internal server error");
        }
    }
}