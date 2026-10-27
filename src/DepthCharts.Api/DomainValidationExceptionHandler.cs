using DepthCharts.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace DepthCharts.Api;

public sealed class DomainValidationExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not DomainValidationException) return false;
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = { Status = StatusCodes.Status400BadRequest, Detail = exception.Message },
        });
    }
}
