using QualityLock.Shared.Constants;

namespace QualityLock.Api.Middleware;

public class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey(AppConstants.CorrelationIdHeader))
            context.Request.Headers[AppConstants.CorrelationIdHeader] = Guid.NewGuid().ToString();

        context.Response.Headers[AppConstants.CorrelationIdHeader] =
            context.Request.Headers[AppConstants.CorrelationIdHeader];

        await next(context);
    }
}
