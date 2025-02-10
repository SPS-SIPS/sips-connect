using SIPS.Adapter;
using SIPS.ISO20022.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
namespace SIPS.Connect.Controllers;

public static class APIResponseRenderer
{
    public static ActionResult GenerateAdminMessage<T>(Response<T> response, IJsonAdapter jsonAdapter, string endpointName = "AdminMessage")
    {
        if (response.Data != null)
        {
            var md = jsonAdapter.Transform(response.Data, endpointName);
            return new OkObjectResult(md);
        }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.BadRequest => new BadRequestObjectResult(new { message = response.Message }),
            System.Net.HttpStatusCode.NotFound => new NotFoundObjectResult(new { message = response.Message }),
            System.Net.HttpStatusCode.Unauthorized => new UnauthorizedObjectResult(new { message = response.Message }),
            _ => new ObjectResult(new { message = response.Message, statusCode = (int)response.StatusCode }) { StatusCode = (int)response.StatusCode },
        };
    }
}