using System.Net;
using System.Text.Json;
using ERP.FMC.Application.Services;

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
            // ★ Log inner exception đầy đủ — nếu không, lỗi EF Core
            //   (DbUpdateException) chỉ hiện message chung chung
            //   "An error occurred while saving the entity changes"
            //   mà không có nguyên nhân gốc (constraint nào vi phạm).
            var inner = ex;
            var depth = 0;
            while (inner.InnerException is not null && depth < 5)
            {
                inner = inner.InnerException;
                depth++;
                _logger.LogError("  -> Inner[{Depth}] {Type}: {Message}",
                    depth, inner.GetType().Name, inner.Message);
            }

            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var (status, code) = ex switch
        {
            // ★ PHASE 6A.1: lỗi chống gian lận có mã riêng để client
            //   hiển thị đúng hướng xử lý cho tài xế.
            AntiFraudException af => ((int)HttpStatusCode.BadRequest, af.ErrorCode),
            ArgumentException => ((int)HttpStatusCode.BadRequest, "INVALID_ARGUMENT"),
            InvalidOperationException => ((int)HttpStatusCode.Conflict, "CONFLICT"),
            KeyNotFoundException => ((int)HttpStatusCode.NotFound, "NOT_FOUND"),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "UNAUTHORIZED"),
            _ => ((int)HttpStatusCode.InternalServerError, "INTERNAL_ERROR")
        };

        // nếu response đã bắt đầu ghi thì không ghi lần nữa,
        //   tránh "Cannot access a closed Stream" / "Headers are read-only".
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";

        // ★ PHASE 6A.1: kèm khoảng cách GPS cho lỗi GPS_GATE_FAILED.
        //   Dùng Dictionary<string, object?> để payload có shape thống nhất
        //   (thiếu `distance_m` khi không phải lỗi GPS).
        var payload = new Dictionary<string, object?>
        {
            ["error"] = code,
            ["message"] = ex.Message,
            ["traceId"] = context.TraceIdentifier
        };

        if (ex is AntiFraudException afEx && afEx.DistanceMeters.HasValue)
            payload["distance_m"] = afEx.DistanceMeters;

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
