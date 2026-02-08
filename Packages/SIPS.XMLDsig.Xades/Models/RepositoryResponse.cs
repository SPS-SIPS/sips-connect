namespace SIPS.XMLDsig.Xades.Models;
public sealed class RepositoryResponse<T>(T Data)
{
    public bool IsSuccess { get; init; } = true;
    public int StatusCode { get; set; } = 200;
    public string Message { get; init; } = string.Empty;
    public int CacheInMins { get; set; } = 0;
    public T Data { get; set; } = Data;

    public static RepositoryResponse<T> BadRequest(string message, T? result = default)
    {
        return new RepositoryResponse<T>(result ?? default!)
        {
            IsSuccess = false,
            Message = message,
            StatusCode = 400
        };
    }

    public static RepositoryResponse<T> NotFound(string message, T? result = default)
    {
        return new RepositoryResponse<T>(result ?? default!)
        {
            IsSuccess = false,
            Message = message,
            StatusCode = 404
        };
    }

    public static RepositoryResponse<T> InternalServerError(string message, T? result = default)
    {
        return new RepositoryResponse<T>(result ?? default!)
        {
            IsSuccess = false,
            Message = message,
            StatusCode = 500
        };
    }

    // Conflict status code 409
    public static RepositoryResponse<T> Conflict(string message, T? result = default)
    {
        return new RepositoryResponse<T>(result ?? default!)
        {
            IsSuccess = false,
            Message = message,
            StatusCode = 409
        };
    }

    public static RepositoryResponse<T> Success(T result, int cacheInMins)
    {
        return new RepositoryResponse<T>(result)
        {
            IsSuccess = true,
            StatusCode = 200,
            CacheInMins = cacheInMins
        };
    }

    public static RepositoryResponse<T> Fail(string message, int statusCode, T? result = default)
    {
        return new RepositoryResponse<T>(result ?? default!)
        {
            IsSuccess = false,
            Message = message,
            StatusCode = statusCode
        };
    }
}

