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
        "/api/inventory", "/api/price-lists",
        // ★ PHASE 6A.6: trước đây POST /api/sales/pod KHÔNG được ghi audit
        //   vì record_id lấy từ path (segments[2]) mà path này không có GUID.
        "/api/sales/pod", "/api/sales/trips", "/api/dispatcher"
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
        // ★ 6A.6: giảm ngưỡng 1MB -> 256KB.
        //   POD có ảnh base64 2-5MB; buffer 1MB vào RAM cho mỗi request
        //   là rủi ro DoS. 256KB đủ cho payload audit (record_id, số tiền).
        if (_trackedMethods.Contains(context.Request.Method) &&
            (context.Request.ContentLength ?? 0) < 256 * 1024) // < 256KB
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        // chỉ buffer/capture khi request thực sự cần audit.
        //   Nếu buffer MỌI response thì mọi endpoint (kể cả master data 2.723
        //   khách hàng) đều bị copy vào MemoryStream -> tốn RAM, chậm.
        var needsAudit = _trackedMethods.Contains(context.Request.Method)
                      && _trackedPaths.Any(p => context.Request.Path.StartsWithSegments(p));

        var originalBodyStream = context.Response.Body;
        // ★ KHÔNG dùng `using` ở đây.
        //   Lý do: nếu _next() ném exception, `using` sẽ dispose MemoryStream
        //   TRƯỚC khi ExceptionMiddleware kịp ghi JSON lỗi vào Response.Body
        //   -> "System.ObjectDisposedException: Cannot access a closed Stream"
        //   -> API trả 500 thay vì trả payload lỗi chuẩn.
        //   => Giữ stream sống, restore Response.Body trong finally.
        var responseBody = new MemoryStream();

        try
        {
            if (needsAudit)
                context.Response.Body = responseBody;

            await _next(context);

            if (needsAudit)
            {
                responseBody.Seek(0, SeekOrigin.Begin);
                var responseBodyText = await new StreamReader(responseBody).ReadToEndAsync();
                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);

                stopwatch.Stop();

                if (context.Response.StatusCode < 400)
                {
                    await LogRequestAsync(
                        auditService, context, requestId, requestBody, responseBodyText,
                        stopwatch.ElapsedMilliseconds);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request failed: {Method} {Path}", context.Request.Method, context.Request.Path);
            throw;
        }
        finally
        {
            // Luôn restore Response.Body về stream gốc TRƯỚC khi dispose,
            // để ExceptionMiddleware (đứng ngoài) vẫn ghi được payload lỗi.
            context.Response.Body = originalBodyStream;
            responseBody.Dispose();
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

            // ★ PHASE 6A.6 — lấy record_id từ PATH trước, nếu không có thì
            //   từ BODY (client_pod_id / trip_id / id).
            //   Lý do: `POST /api/sales/pod` có path KHÔNG chứa GUID
            //   (`/api/sales/pod`) ⇒ trước đây hàm `return` sớm ⇒
            //   KHÔNG ghi audit cho mọi POD ⇒ mất dấu vết gian lận.
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

            // Fallback: đọc record_id từ body.
            if (!recordId.HasValue)
            {
                recordId = ExtractRecordIdFromBody(requestBody);
                if (recordId.HasValue && string.IsNullOrEmpty(tableName))
                    tableName = MapEndpointToTable(segments.Length >= 2 ? segments[1] : "");
            }

            if (string.IsNullOrEmpty(tableName))
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
                // recordId đã được kiểm tra HasValue ở trên — nhưng dùng
                // `?? Guid.Empty` để tránh CS8629 (nullable value type may be null).
                recordId: recordId ?? Guid.Empty,
                action: action,
                operation: $"{method.ToUpper()} {path}",
                newValues: TryParseJson(requestBody),
                // ★ 6A.6: lấy user_id từ JWT claim thay vì null cố định.
                userId: ExtractUserId(context),
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

    /// <summary>
    /// ★ 6A.6 — lấy user_id từ JWT claim.
    ///   Ưu tiên claim "sub" (JWT standard), sau đó NameIdentifier.
    /// </summary>
    private static Guid? ExtractUserId(HttpContext context)
    {
        var raw = context.User?.FindFirst("sub")?.Value
               ?? context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// ★ 6A.6 — lấy record_id từ body.
    ///   Thử lần lượt các khoá phổ biến: client_pod_id, trip_id, pod_id,
    ///   reconciliation_id, id, sales_order_id.
    /// </summary>
    private static Guid? ExtractRecordIdFromBody(string? requestBody)
    {
        if (string.IsNullOrWhiteSpace(requestBody)) return null;

        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var key in new[]
            {
                "client_pod_id", "pod_id", "trip_id", "trip_order_id",
                "reconciliation_id", "sales_order_id", "id"
            })
            {
                if (!doc.RootElement.TryGetProperty(key, out var el)) continue;
                if (el.ValueKind == JsonValueKind.String
                    && Guid.TryParse(el.GetString(), out var g))
                    return g;
            }
        }
        catch
        {
            // Body không phải JSON hợp lệ → bỏ qua.
        }

        return null;
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
            // ★ 6A.6: endpoint mới của Phase 6
            "pod" => "delivery_pod",
            "pods" => "delivery_pod",
            "sales" => "delivery_trips",
            "trips" => "delivery_trips",
            "dispatcher" => "delivery_trips",
            "eod" => "eod_reconciliations",
            _ => endpoint
        };
    }
}
