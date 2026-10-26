using System.Net;
using System.Text.Json;

namespace ERP.FMC.Api.Middleware;

/// <summary>
/// Global error handler middleware.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";
        var (status, code) = ex switch
        {
            ArgumentException => ((int)HttpStatusCode.BadRequest, "INVALID_ARGUMENT"),
            InvalidOperationException => ((int)HttpStatusCode.Conflict, "CONFLICT"),
            KeyNotFoundException => ((int)HttpStatusCode.NotFound, "NOT_FOUND"),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "UNAUTHORIZED"),
            _ => ((int)HttpStatusCode.InternalServerError, "INTERNAL_ERROR")
        };
        context.Response.StatusCode = status;

        var payload = new
        {
            error = code,
            message = ex.Message,
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
