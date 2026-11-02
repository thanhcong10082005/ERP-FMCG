using ERP.FMC.Application.Interfaces;
using ERP.FMC.Application.Mappings;
using ERP.FMC.Application.Services;
using ERP.FMC.Api.Middleware;
using ERP.FMC.Domain.Interfaces;
using ERP.FMC.Infrastructure.Data;
using ERP.FMC.Infrastructure.Repositories;
using ERP.FMC.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ============== Configuration ==============
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? @"Server=.\SQLEXPRESS;Database=ERP_FMCG;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

builder.Configuration["ConnectionStrings:DefaultConnection"] = connectionString;

// --- ĐỒNG BỘ CỔNG KHỞI ĐỘNG -------------------------------------------------
// Ở đây ta tự dò cổng trống, ghi đè cấu hình và đồng bộ ApiBaseUrl + CORS
// để toàn hệ thống luôn nhất quán (API ↔ SmokeTest ↔ Driver App).
var explicitApiBaseUrl = builder.Configuration["ApiBaseUrl"];
var actualHttpEndpoint = SyncKestrelEndpoints(builder.Configuration);

builder.Configuration["ApiBaseUrl"] = !string.IsNullOrWhiteSpace(explicitApiBaseUrl)
    ? explicitApiBaseUrl.TrimEnd('/')
    : actualHttpEndpoint;

// ============== Services ==============

// EF Core
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, b =>
        b.EnableRetryOnFailure(maxRetryCount: 3)));

// đã gỡ AutoMapper — mapping thủ công trong Mappings/EntityMapper.cs

// DI - Repositories + UoW
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// DI - Application Services
builder.Services.AddScoped<IPartnerService, PartnerService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IRouteService, RouteService>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();
builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IPriceListService, PriceListService>();
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<IAuditService, AuditService>();
// ExportService is registered later as concrete type

// DI - Infrastructure Services
builder.Services.AddScoped<BulkOperationService>();
builder.Services.AddScoped<ERP.FMC.Infrastructure.Services.ExportService>();
// xuất Excel cho 6 báo cáo vận hành + POD + EOD
builder.Services.AddScoped<ERP.FMC.Infrastructure.Services.OperationsExportService>();
builder.Services.AddScoped<ImportStagingService>();
builder.Services.AddScoped<TemporalHistoryService>();
builder.Services.AddSingleton<ValidationEngine>();

// DI - Services
builder.Services.AddScoped<IUatTestService, UatTestService>();
builder.Services.AddScoped<ISmokeTestService, SmokeTestService>();
builder.Services.AddScoped<IGoLiveService, GoLiveService>();
builder.Services.AddScoped<IMonitoringService, MonitoringService>();

// DI - JWT + Auth
var jwtOptions = new JwtOptions();
builder.Configuration.GetSection(JwtOptions.SectionName).Bind(jwtOptions);
// Ưu tiên biến môi trường (không commit secret vào repo)
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ERP_FMCG_JWT_KEY")))
    jwtOptions.SigningKey = Environment.GetEnvironmentVariable("ERP_FMCG_JWT_KEY")!;
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// DI - nghiệp vụ giao hàng
builder.Services.AddScoped<IDeliveryTripService, DeliveryTripService>();
builder.Services.AddScoped<IDeliveryPodService, DeliveryPodService>();

// chống gian lận server-side (3 chốt chặn)
builder.Services.AddScoped<IAntiFraudService, AntiFraudService>();

// dispatcher, báo cáo, đối soát EOD
builder.Services.AddScoped<IDispatcherService, DispatcherService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IEodReconciliationService, EodReconciliationService>();

// DI - HttpClient for smoke tests
builder.Services.AddHttpClient();

// ============== JWT Authentication ==============
// Driver App gọi API cần Bearer token.
//   Token ký HS256, hạn 12h (mặc định) cho 1 ca giao hàng.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(ResolveJwtKey(jwtOptions))),
            ClockSkew = TimeSpan.FromMinutes(1),
            // Token phát ra dùng claim tùy biến "role" (không phải ClaimTypes.Role).
            // Khai báo RoleClaimType để `User.IsInRole(...)` và handler role hoạt động.
            RoleClaimType = "role",
            NameClaimType = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Jwt");
                logger.LogWarning("JWT xac thuc that bai: {Error}",
                    context.Exception.Message);
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

// Role KHÔNG phân biệt hoa/thường.
// DB lưu role dạng UPPERCASE (ADMIN/MANAGER/ACCOUNTANT/SALES/WAREHOUSE) trong khi
// `Roles = "Admin,Manager"` (RolesAuthorizationRequirement) so sánh phân biệt
// hoa/thường ⇒ "ADMIN" != "Admin" ⇒ 403 cho MỌI endpoint quản trị dù tài khoản
// có role hợp lệ. Các policy dưới đây dùng RoleInAnyRequirement (case-insensitive).
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    ERP.FMC.Api.Policies.RoleInAnyHandler>();

builder.Services.AddAuthorization(options =>
{
    // ★ FIX — FallbackPolicy: mặc định YÊU CẦU token hợp lệ cho MỌI endpoint.
    //
    //   SYMPTOM (triệu chứng):
    //     Trước đây chỉ một số controller khai báo [Authorize] (Dispatcher, Reports,
    //     DriverSales). Các controller còn lại — Audit, Exports, Imports, Monitoring,
    //     GoLive, Uat, Smoke, Partners, Products, SalesOrders, Inventory, Catalog —
    //     KHÔNG có attribute ⇒ KHÔNG cần token vẫn gọi được.
    //     ⇒ `GET /api/audit/logs` (không token) trả 200, lộ toàn bộ nhật ký kiểm toán.
    //     ⇒ `POST /api/golive/switch-dns` (không token) chạy được.
    //     ⇒ Đây là lỗ hổng "thiếu xác thực mặc định" (CWE-306).
    //
    //   FIX (cách sửa):
    //     Đặt FallbackPolicy = RequireAuthenticatedUser ⇒ mặc định CHẶN,
    //     và chỉ mở những endpoint thực sự công khai bằng [AllowAnonymous]:
    //       - POST /api/auth/login, POST /api/auth/dev-login
    //       - GET  /api/Health, GET /api/Health/deep  (health check cho monitor/LB)
    //
    //   PREVENTION (ngăn tái diễn):
    //     Controller MỚI mặc định đã được bảo vệ; muốn mở phải khai báo
    //     [AllowAnonymous] tường minh ⇒ review dễ thấy.
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Khu vực điều phối / vận hành
    options.AddPolicy("DispatcherRole", policy =>
        policy.AddRequirements(new ERP.FMC.Api.Policies.RoleInAnyRequirement(
            "Admin", "Dispatcher", "Manager")));

    // Báo cáo + xuất Excel
    options.AddPolicy("ReportingRole", policy =>
        policy.AddRequirements(new ERP.FMC.Api.Policies.RoleInAnyRequirement(
            "Admin", "Dispatcher", "Manager", "Accountant")));
});

// Validators
builder.Services.AddValidatorsFromAssemblyContaining<ERP.FMC.Application.DTOs.PartnerDto>();

// MVC + JSON
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ERP FMCG API",
        Version = "v1",
        Description = "API cho hệ thống ERP FMCG"
    });
});

// RATE LIMITING
//   Chống tài xế spam POD (gửi hàng loạt để bơm nợ / gian lận).
//   - POST /api/sales/pod: 30 lần / phút / tài xế (IP fallback)
//   - POST /api/sales/trips/*: 10 lần / phút
//   - Global: 300 lần / phút / IP cho tất cả API
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<
        HttpContext, string>(ctx =>
        {
            var key = GetRateLimitKey(ctx);
            return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                key,
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });

    options.AddPolicy("pod-submit", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitKey(httpContext),
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    options.AddPolicy("trip-mutate", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitKey(httpContext),
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "{\"error\":\"RATE_LIMIT_EXCEEDED\",\"message\":\"Quá nhiều yêu cầu. Vui lòng thử lại sau.\"}",
            token);
    };
});

// CORS
// siết CORS.
//   Trước: AllowAnyOrigin() → mọi website đều gọi được API.
//   Sau: chỉ cho phép origin trong cấu hình `Cors:AllowedOrigins`.
//   Nếu danh sách rỗng → chỉ cho phép localhost (an toàn mặc định).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();

if (allowedOrigins.Length == 0)
{
    // Danh sách origin mặc định.
    //   5173 = Vite dev server (port chuẩn của CRA/React template)
    //   3002 = Vite dev server của Driver App (xem erp-fmcg-driver/vite.config.ts)
    //   4173 = Vite preview server của Driver App (npm run preview)
    //   5000/5001 = chính API + HTTPS của API
    // Thiếu 3002/4173 sẽ khiến Driver App gặp lỗi CORS khi gọi API.
    allowedOrigins = new[]
    {
        "http://localhost:5173",
        "http://127.0.0.1:5173",
        "http://localhost:3002",
        "http://127.0.0.1:3002",
        "http://localhost:4173",
        "http://127.0.0.1:4173",
        "http://localhost:5000",
        "http://127.0.0.1:5000",
        "http://localhost:5001",
        "http://127.0.0.1:5001",
    };
}

// Thêm origin của chính API theo cổng thực tế (có thể đã dịch khỏi 5000
// vì 5000 bị ứng dụng khác chiếm). Nếu không, PWA gọi thẳng API ở cổng
// mới sẽ bị CORS chặn.
if (!string.IsNullOrWhiteSpace(actualHttpEndpoint) && actualHttpEndpoint != "http://localhost:0")
{
    var origin = actualHttpEndpoint.TrimEnd('/');
    var extraOrigins = new List<string> { origin };
    if (origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase))
        extraOrigins.Add(origin.Replace("http://localhost:", "http://127.0.0.1:"));
    allowedOrigins = allowedOrigins.Concat(extraOrigins).Distinct().ToArray();
}

// Biểu thức kiểm tra origin: chấp nhận danh sách cấu hình VÀ mọi origin
// loopback (localhost/127.0.0.1/[::1]) ở bất kỳ cổng nào.
//   Lý do: cổng nghe của API/Vite có thể thay đổi động (Kestrel tự chọn
//   cổng trống khi 5000 bị chiếm), nếu khoá cứng theo cổng sẽ hỏng CORS.
//   An toàn: website bên ngoài KHÔNG thể giả mạo Origin thành loopback.
bool IsAllowedOrigin(string origin) =>
    !string.IsNullOrWhiteSpace(origin)
    && (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)
        || IsLoopbackOrigin(origin));

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(IsAllowedOrigin)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .SetPreflightMaxAge(TimeSpan.FromSeconds(60));
    }));

/// <summary>
/// Kiểm tra origin có trỏ về loopback (localhost / 127.0.0.1 / ::1) hay không.
/// Chấp nhận mọi cổng vì cổng dev server có thể thay đổi theo phiên chạy.
/// </summary>
static bool IsLoopbackOrigin(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
    if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        return false;

    var host = uri.Host.Trim('[', ']');
    return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
}

// Logging
builder.Services.AddLogging(b => b.AddConsole());

// ============== Build App ==============
var app = builder.Build();

// Root health check.
//   `.AllowAnonymous()` là BẮT BUỘC: FallbackPolicy yêu cầu token cho
//   mọi endpoint, kể cả minimal API. Thiếu khai báo này => HTTP 401.
app.MapGet("/", () => Results.Ok(new
{
    name = "ERP FMCG API",
    version = "1.0.0",
    status = "running",
    docs = "/swagger",
    health = "/api/health"
})).AllowAnonymous();

// Cổng nghe thực tế của API.
//   Driver App (vite proxy) cần biết cổng này khi API tự dò cổng trống
//   (VD: cổng 5000 bị ứng dụng khác chiếm). Endpoint này CÔNG KHAI và
//   chỉ trả về thông tin không nhạy cảm.
//
//   `.AllowAnonymous()` là BẮT BUỘC: FallbackPolicy yêu cầu token cho
//   mọi endpoint, kể cả minimal API. Thiếu khai báo này => HTTP 401.
app.MapGet("/api/health/endpoint", () => Results.Ok(new
{
    baseUrl = builder.Configuration["ApiBaseUrl"],
    port = ResolveBoundPort(builder.Configuration["ApiBaseUrl"]),
    timestamp = DateTime.UtcNow
})).AllowAnonymous();

// API health check nằm trong HealthController (route api/health).
// KHÔNG khai báo trùng ở đây, nếu không request sẽ lỗi
// AmbiguousMatchException (HTTP 500) do khớp 2 endpoint.

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ERP FMCG v1");
        c.RoutePrefix = "swagger";
    });
}

// Chỉ ép HTTPS ở môi trường thật. Khi chạy local (Development) mà bật
// HttpsRedirection thì client gọi http://localhost:5000 sẽ bị đẩy sang
// https://localhost:5001 với chứng chỉ self-signed -> trình duyệt/app báo
// "Could not establish trust relationship" và không gọi được API.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

//  Middleware thứ tự RẤT QUAN TRỌNG
//   1. ExceptionMiddleware  — bắt lỗi toàn cục, trả JSON chuẩn
//   2. AuditLogMiddleware    — ghi audit cho write operations
//   3. CORS
//   4. Authentication       — xác thực JWT
//   5. Authorization
//
// Lưu ý: AuditLogMiddleware phải đặt SAU ExceptionMiddleware để lỗi
// không làm hỏng luồng audit, và TRƯỚC Authentication để có context.
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<AuditLogMiddleware>();
app.UseCors();
// rate limiting phải SAU CORS, TRƯỚC auth để key theo user.
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Chốt endpoint thực tế sau khi Kestrel đã bind.
//   Với port 0, OS chọn cổng ngẫu nhiên -> phải đọc lại từ
//   IServerAddressesFeature để ApiBaseUrl / CORS / SmokeTest biết cổng đúng.
//   Với cổng cố định, giữ nguyên giá trị đã dò sẵn.
if (actualHttpEndpoint == "http://localhost:0")
{
    var addresses = app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>();
    var bound = addresses?.Addresses?.FirstOrDefault(a =>
        !a.Contains("https://", StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrWhiteSpace(bound))
    {
        builder.Configuration["ApiBaseUrl"] = bound.TrimEnd('/');
        Console.WriteLine($"[BOOT] Kestrel da bind, endpoint thuc te: {bound}");
    }
}

// Xuất endpoint ra file để Driver App (vite proxy) dò được cổng API khi
// API tự chuyển cổng (cổng ưu tiên bị ứng dụng khác chiếm).
//   File: erp-fmcg-driver/.api-endpoint.json (nằm cạnh vite.config.ts)
//   Nếu ghi thất bại (read-only fs) -> chỉ log cảnh báo, KHÔNG làm API chết.
try
{
    var driverDir = ResolveDriverDirectory();
    if (!string.IsNullOrWhiteSpace(driverDir))
    {
        var endpointFile = Path.Combine(driverDir, ".api-endpoint.json");
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            baseUrl = builder.Configuration["ApiBaseUrl"],
            port = ResolveBoundPort(builder.Configuration["ApiBaseUrl"]),
            pid = Environment.ProcessId,
            updatedAt = DateTime.UtcNow
        });
        File.WriteAllText(endpointFile, payload);
        Console.WriteLine($"[BOOT] Da ghi endpoint file: {endpointFile}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[BOOT] WARN khong ghi duoc endpoint file: {ex.Message}");
}

app.Run();

// ===================================================================
// Helpers
// ===================================================================

/// <summary>
/// Lấy khoá phân vùng rate limit.
/// Ưu tiên theo user (tài xế) để 1 tài xế không bị chặn nhầm
///   khi nhiều tài xế dùng chung NAT/IP trong một khu vực.
///   Fallback theo IP cho request chưa xác thực.
/// </summary>
static string GetRateLimitKey(HttpContext ctx)
{
    var userId = ctx.User?.FindFirst("sub")?.Value
                 ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

    if (!string.IsNullOrWhiteSpace(userId))
        return $"user:{userId}";

    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return $"ip:{ip}";
}

/// <summary>
/// Lấy khoá ký JWT. Nếu chưa cấu hình -> dùng khoá Development tạm
/// (đủ để API chạy được local) và in cảnh báo rõ ràng.
/// </summary>
static string ResolveJwtKey(JwtOptions options)
{
    var key = options.SigningKey;
    if (string.IsNullOrWhiteSpace(key))
        key = Environment.GetEnvironmentVariable("ERP_FMCG_JWT_KEY");

    if (string.IsNullOrWhiteSpace(key))
    {
        key = "DEV-ONLY-INSECURE-KEY-CHANG-DOI-KEY-NAY-TRUOC-KHI-LEN-PROD-0123456789";
        Console.WriteLine(
            "[WARN] JWT:SigningKey chua duoc cau hinh — dang dung khoa DEV tam. " +
            "Production BAT BUOC dat appsettings.Local.json hoac bien moi truong ERP_FMCG_JWT_KEY.");
    }

    // HS256 yeu cau >= 32 byte
    if (Encoding.UTF8.GetByteCount(key) < 32)
        key = key.PadRight(32, '0');

    return key;
}

/// <summary>
/// Đồng bộ cổng nghe của Kestrel và trả về URL HTTP thực tế.
///
/// VÌ SAO CẦN:
///   Cổng mặc định (5000) có thể đã bị ứng dụng khác trên máy chiếm
///   . Khi đó Kestrel ném
///   <c>AddressInUseException</c> ngay lúc bind và API CHẾT — không
///   phản hồi HTTP nào, Driver App / smoke test đều trả ECONNREFUSED.
///
/// CÁCH SỬA:
///   1. Nếu người dùng chỉ định rõ endpoint (biến môi trường
///      <c>Kestrel__Endpoints__Http__Url</c> / <c>ASPNETCORE_URLS</c>)
///      -> giữ nguyên, KHÔNG tự ý đổi (đây là ý muốn rõ ràng).
///   2. Ngược lại: dò từ cổng ưu tiên (5000, 5002..5099, 5218) xuống
///      cổng trống đầu tiên. Nếu tất cả đều bận -> dùng cổng 0 (OS tự
///      chọn cổng trống) để API VẪN KHỞI ĐỘNG ĐƯỢC thay vì crash.
///   3. Ghi cổng đã chọn lại vào <c>Kestrel:Endpoints:Http:Url</c> để
///      <c>SmokeTestService.ResolveBaseUrl()</c> và mọi nơi khác đọc
///      đúng cổng thực tế.
///   4. In ra console để người vận hành biết API đang ở đâu.
///
/// LƯU Ý: HTTPS endpoint giữ nguyên mặc định; nếu HTTPS bị chiếm thì
/// Kestrel sẽ bỏ qua (vì chỉ nghe HTTP trong local dev).
/// </summary>
static string SyncKestrelEndpoints(IConfigurationManager config)
{
    const string httpUrlKey = "Kestrel:Endpoints:Http:Url";

    // Ưu tiên 1: endpoint do môi trường chỉ định -> tôn trọng ý muốn người dùng.
    //   LƯU Ý: `dotnet run` tự đặt ASPNETCORE_URLS từ launchSettings.json, nên
    //   biến này KHÔNG nhất thiết là ý muốn thật của người dùng. Vì vậy ta vẫn
    //   kiểm tra cổng có trống không; nếu bận thì tự dò cổng khác thay vì crash.
    var envHttpUrl = Environment.GetEnvironmentVariable("Kestrel__Endpoints__Http__Url");
    var envUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    var explicitUrl = !string.IsNullOrWhiteSpace(envHttpUrl)
        ? envHttpUrl
        : (!string.IsNullOrWhiteSpace(envUrls) && envUrls.Contains("http://", StringComparison.OrdinalIgnoreCase)
            ? envUrls
            : null);

    if (!string.IsNullOrWhiteSpace(explicitUrl))
    {
        // Nhiều endpoint cách nhau bằng dấu chấm phẩy -> chỉ xét endpoint HTTP đầu tiên.
        var firstHttp = explicitUrl
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(u => u.Contains("http://", StringComparison.OrdinalIgnoreCase));

        if (TryGetPort(firstHttp, out var explicitPort) && !IsPortFree(explicitPort))
        {
            Console.WriteLine(
                $"[BOOT] Canh {explicitUrl} da BI CHIEM (port {explicitPort}) — " +
                "se tu dong chon cong trong khac thay vi crash.");
        }
        else
        {
            config[httpUrlKey] = firstHttp ?? explicitUrl;
            Console.WriteLine($"[BOOT] Kestrel HTTP endpoint (tu moi truong): {firstHttp ?? explicitUrl}");
            return (firstHttp ?? explicitUrl).TrimEnd('/');
        }
    }

    // Ưu tiên 2: dò cổng trống.
    var preferred = new[] { 5000, 5002, 5003, 5004, 5005, 5006, 5007, 5008, 5009,
                            5010, 5011, 5012, 5013, 5014, 5015, 5016, 5017, 5018, 5019,
                            5020, 5021, 5022, 5023, 5024, 5025, 5026, 5027, 5028, 5029,
                            5030, 5031, 5032, 5033, 5034, 5035, 5036, 5037, 5038, 5039,
                            5090, 5091, 5092, 5093, 5094, 5095, 5096, 5097, 5098, 5099 };
    foreach (var port in preferred)
    {
        if (IsPortFree(port))
        {
            var url = $"http://localhost:{port}";
            config[httpUrlKey] = url;
            Console.WriteLine($"[BOOT] Kestrel HTTP endpoint: {url}");
            return url;
        }
    }

    // Ưu tiên 3: để OS tự chọn cổng trống -> API luôn khởi động được.
    // Cổng thực tế sẽ được chốt lại ở ApplicationStarted (xem Program.cs).
    var fallbackUrl = "http://localhost:0";
    config[httpUrlKey] = fallbackUrl;
    Console.WriteLine("[BOOT] Tat ca cong uu tien deu dang ban — Kestrel se tu chon cong trong (port 0).");
    return fallbackUrl;
}

/// <summary>
/// Trích xuất số cổng từ một URL (VD: "http://localhost:5000" -> 5000).
/// Trả về false nếu URL không chứa cổng (VD: "http://localhost" -> cổng 80 mặc định).
/// </summary>
static bool TryGetPort(string? url, out int port)
{
    port = 0;
    if (string.IsNullOrWhiteSpace(url)) return false;
    if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
    return uri.Port > 0 && int.TryParse(uri.Port.ToString(), out port);
}

/// <summary>
/// Lấy số cổng thực tế đang lắng nghe từ URL base của API.
/// Trả về 0 nếu không xác định được.
/// </summary>
static int ResolveBoundPort(string? baseUrl)
{
    TryGetPort(baseUrl, out var port);
    return port;
}

/// <summary>
/// Tìm thư mục Driver App (chứa vite.config.ts) để ghi file endpoint.
/// Dò ngược từ thư mục hiện tại lên tối đa 6 cấp.
/// Nếu không tìm thấy -> trả null (API vẫn chạy bình thường).
/// </summary>
static string? ResolveDriverDirectory()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var i = 0; i < 8 && dir is not null; i++)
    {
        if (File.Exists(Path.Combine(dir.FullName, "vite.config.ts"))
            && Directory.Exists(Path.Combine(dir.FullName, "src")))
            return dir.FullName;

        var sibling = Path.Combine(dir.FullName, "erp-fmcg-driver");
        if (File.Exists(Path.Combine(sibling, "vite.config.ts")))
            return sibling;

        dir = dir.Parent;
    }
    return null;
}

/// <summary>
/// Kiểm tra một cổng TCP có đang trống (không bị process khác chiếm) hay không.
/// Dùng TcpListener thử bind rồi release ngay.
/// </summary>
static bool IsPortFree(int port)
{
    try
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
        return true;
    }
    catch (System.Net.Sockets.SocketException)
    {
        return false;
    }
    catch (Exception)
    {
        // Không xác định được -> coi như trống, Kestrel sẽ báo lỗi nếu thật sự bận.
        return true;
    }
}
