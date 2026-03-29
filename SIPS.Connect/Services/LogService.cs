using SIPS.Connect.Models;

namespace SIPS.Connect.Services;

public interface ILogService
{
    Task<PagedResult<LogFileResponse>> GetLogFilesAsync(
        int page,
        int pageSize,
        string? search,
        string? sort);

    Task<Stream?> DownloadLogFileAsync(
        string fileName,
        CancellationToken cancellationToken = default);
}

public class LogService(
    ILogger<LogService> logger)
    : ILogService
{
    public async Task<PagedResult<LogFileResponse>> GetLogFilesAsync(
        int page,
        int pageSize,
        string? search,
        string? sort)
    {
        var logsPath = ResolveLogsPath();

        if (!Directory.Exists(logsPath))
        {
            logger.LogWarning("Logs directory not found at {Path}", logsPath);
            return new PagedResult<LogFileResponse>();
        }

        var files = Directory.EnumerateFiles(logsPath, "log*.log")
            .Select(f => new FileInfo(f));
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            files = files.Where(f =>
                f.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        
        sort = sort?.ToLower();

        var orderedList = (sort switch
        {
            "asc" => files.OrderBy(f => f.LastWriteTime),
            _ => files.OrderByDescending(f => f.LastWriteTime)
        }).ToList();
        
        var totalCount = orderedList.Count();
        
        var pagedFiles = orderedList
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        var results = pagedFiles.Select(file => new LogFileResponse(
            file.Name,
            file.LastWriteTime,
            file.Length,
            FormatBytes(file.Length)
        )).ToList();

        return await Task.FromResult(new PagedResult<LogFileResponse>
        {
            Items = results,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<Stream?> DownloadLogFileAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name must be provided.", nameof(fileName));

            if (!IsSafeFileName(fileName))
                throw new ArgumentException("Unsafe file name.", nameof(fileName));

            var logsPath = ResolveLogsPath();
            var fullPath = Path.Combine(logsPath, fileName);

            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Log file not found.", fileName);

            logger.LogInformation("Downloading log file {File}", fullPath);
            
            var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );

            return await Task.FromResult(stream);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download log file: {FileName}", fileName);
            return null;
        }
    }
    
    private static string ResolveLogsPath()
    {
        var dockerPath = "/logs";

        if (Directory.Exists(dockerPath))
            return dockerPath;

        return Path.Combine(Directory.GetCurrentDirectory(), "logs");
    }
    
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
        if (bytes == 0) return "0 B";

        // Handle negative inputs
        var absoluteBytes = Math.Abs(bytes);

        // Calculate the order of magnitude using Logarithms
        var order = (int)Math.Floor(Math.Log(absoluteBytes, 1024));

        // Clamp the order to the max index of our array
        order = Math.Min(order, sizes.Length - 1);

        var adjustedSize = bytes / Math.Pow(1024, order);

        return $"{adjustedSize:0.##} {sizes[order]}";
    }

    private static bool IsSafeFileName(string fileName)
    {
        return !fileName.Contains("..") &&
               !fileName.Contains(Path.DirectorySeparatorChar) &&
               !fileName.Contains(Path.AltDirectorySeparatorChar);
    }
}