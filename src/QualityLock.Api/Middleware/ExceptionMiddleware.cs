using System.Net;
using System.Text.Json;
using QualityLock.Application.Exceptions;
using QualityLock.Shared.Constants;
using QualityLock.Shared.DTOs;

namespace QualityLock.Api.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Request.Headers[AppConstants.CorrelationIdHeader].FirstOrDefault()
                                ?? context.TraceIdentifier;

            var (statusCode, code) = ex switch
            {
                NotFoundException => (HttpStatusCode.NotFound, "NOT_FOUND"),
                ValidationException => (HttpStatusCode.BadRequest, "VALIDATION_ERROR"),
                ConflictException => (HttpStatusCode.Conflict, "CONFLICT"),
                UnauthorizedException => (HttpStatusCode.Forbidden, "FORBIDDEN"),
                _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR")
            };

            // Las excepciones de negocio (404/400/409/403) son esperadas: se registran
            // como advertencia SIN stack trace. Solo las inesperadas (500) van como error
            // con la traza completa.
            if (statusCode == HttpStatusCode.InternalServerError)
                logger.LogError(ex, "Unhandled exception. CorrelationId: {CorrelationId}", correlationId);
            else
                logger.LogWarning("{Code}: {Message} (CorrelationId: {CorrelationId})",
                    code, ex.Message, correlationId);

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            var error = new ApiErrorDto(code, ex.Message, correlationId);
            await context.Response.WriteAsync(JsonSerializer.Serialize(error));
        }
    }
}
