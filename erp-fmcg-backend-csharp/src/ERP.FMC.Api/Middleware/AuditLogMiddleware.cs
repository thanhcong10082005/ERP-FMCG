using System.Diagnostics;
using System.Text.Json;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Interfaces;

namespace ERP.FMC.Api.Middleware;

/// <summary>
/// Middleware tự động ghi audit log cho các write operations.
/// </summary>
public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLogMiddleware> _logger;

    private static readonly HashSet<string> _trackedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    private static readonly HashSet<string> _trackedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/partners", "/api/products", "/api/sales-orders",
        "/api/categories", "/api/routes", "/api/warehouses",
        "/api/inventory", "/api/price-lists"
    };

    public AuditLogMiddleware(RequestDelegate next, ILogger<AuditLogMiddleware> logger)
    {
        _next = next; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUnitOfWork uow, IAuditService auditService)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        context.Response.Headers["X-Request-Id"] = requestId;

        // Capture request body for POST/PUT/PATCH
        string? requestBody = null;
        if (_trackedMethods.Contains(context.Request.Method) &&
            (context.Request.ContentLength ?? 0) < 1024 * 1024) // < 1MB
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);

            responseBody.Seek(0, SeekOrigin.Begin);
            var responseBodyText = await new StreamReader(responseBody).ReadToEndAsync();
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);

            stopwatch.Stop();

            // Log audit for successful writes
            if (_trackedMethods.Contains(context.Request.Method) &&
                _trackedPaths.Any(p => context.Request.Path.StartsWithSegments(p)) &&
                context.Response.StatusCode < 400)
            {
                await LogRequestAsync(
                    auditService, context, requestId, requestBody, responseBodyText,
                    stopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request failed: {Method} {Path}", context.Request.Method, context.Request.Path);
            throw;
        }
    }

    private async Task LogRequestAsync(
        IAuditService auditService, HttpContext context,
        string requestId, string? requestBody, string? responseBody,
        long durationMs)
    {
        try
        {
            var path = context.Request.Path.Value ?? "";
            var method = context.Request.Method;
            var ip = context.Connection.RemoteIpAddress?.ToString();

            // Extract record ID from path if present
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            Guid? recordId = null;
            string tableName = "";

            if (segments.Length >= 3)
            {
                var endpoint = segments[1]; // e.g., "partners"
                tableName = MapEndpointToTable(endpoint);

                if (Guid.TryParse(segments[2], out var id))
                    recordId = id;
            }

            if (string.IsNullOrEmpty(tableName) || !recordId.HasValue)
                return;

            // Determine action
            var action = method.ToUpper() switch
            {
                "POST" => "CREATE",
                "PUT" or "PATCH" => "UPDATE",
                "DELETE" => "DELETE",
                _ => method.ToUpper()
            };

            await auditService.LogAsync(
                tableName: tableName,
                recordId: recordId.Value,
                action: action,
                operation: $"{method.ToUpper()} {path}",
                newValues: TryParseJson(requestBody),
                userId: null, // Could extract from JWT
                source: "API",
                requestId: requestId,
                ipAddress: ip,
                notes: $"Duration: {durationMs}ms, Status: {context.Response.StatusCode}"
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write audit log for request");
        }
    }

    private static object? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<object>(json);
        }
        catch
        {
            return json;
        }
    }

    private static string MapEndpointToTable(string endpoint)
    {
        return endpoint.ToLower() switch
        {
            "partners" => "partners",
            "products" => "products",
            "sales-orders" => "sales_orders",
            "categories" => "categories",
            "routes" => "routes",
            "warehouses" => "warehouses",
            "price-lists" => "price_lists",
            "inventory" => "inventory_balances",
            _ => endpoint
        };
    }
}
