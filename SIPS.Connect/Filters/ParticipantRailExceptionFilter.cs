using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SIPS.Connect.Services;

namespace SIPS.Connect.Filters;

public sealed class ParticipantRailExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ParticipantRailException exception) return;

        context.Result = new BadRequestObjectResult(new
        {
            code = exception.Code,
            message = exception.Message
        });
        context.ExceptionHandled = true;
    }
}
