using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WilkenAutomation.Application.Services;

namespace WilkenAutomation.Api.Auth;

public class AuthExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is AuthException auth)
        {
            context.Result = new ObjectResult(new { message = auth.Message, errorCode = auth.ErrorCode })
            {
                StatusCode = auth.ErrorCode switch
                {
                    "EMAIL_TAKEN" => StatusCodes.Status409Conflict,
                    "REGISTRATION_DISABLED" => StatusCodes.Status403Forbidden,
                    "INVALID_CREDENTIALS" or "REFRESH_INVALID" or "REFRESH_REUSE" => StatusCodes.Status401Unauthorized,
                    _ => StatusCodes.Status400BadRequest
                }
            };
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is UnauthorizedAccessException)
        {
            context.Result = new ObjectResult(new { message = "Unauthorized.", errorCode = "UNAUTHORIZED" })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            context.ExceptionHandled = true;
        }
    }
}
