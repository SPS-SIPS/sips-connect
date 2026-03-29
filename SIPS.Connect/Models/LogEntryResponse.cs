namespace SIPS.Connect.Models;

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public record LogFileResponse(
    string FileName,
    DateTime LastWriteTime,
    long SizeBytes,
    string Size);