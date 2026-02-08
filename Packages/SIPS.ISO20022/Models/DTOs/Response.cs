

using System.Net;

namespace SIPS.ISO20022.Models.DTOs;
public sealed record Response<T>(T? Data)
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty; // Add the 'required' modifier or declare the property as nullable.
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public static Response<T> Unauthorized(T? result, string message)
    {
        return new Response<T>(result)
        {
            IsSuccess = false,
            Message = message,
            StatusCode = HttpStatusCode.Unauthorized
        };
    }

    public static Response<T> NotFound(T? result, string message)
    {
        return new Response<T>(result)
        {
            IsSuccess = false,
            Message = message,
            StatusCode = HttpStatusCode.NotFound
        };
    }

    public static Response<T> BadRequest(T? result, string message)
    {
        return new Response<T>(result)
        {
            IsSuccess = false,
            Message = message,
            StatusCode = HttpStatusCode.BadRequest
        };
    }

    public static Response<T> Success(T result)
    {
        return new Response<T>(result)
        {
            IsSuccess = true,
            Message = "Success",
            StatusCode = HttpStatusCode.OK
        };
    }
    public static Response<T> Fail(string message, HttpStatusCode statusCode = HttpStatusCode.InternalServerError, T? result = default)
    {
        return new Response<T>(result ?? default!)
        {
            IsSuccess = false,
            Message = message,
            StatusCode = statusCode
        };
    }

    public void Deconstruct(out T? data, out bool isSuccess, out string message, out HttpStatusCode statusCode)
    {
        data = Data;
        isSuccess = IsSuccess;
        message = Message;
        statusCode = StatusCode;
    }
}

