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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ============== Configuration ==============
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? @"Server=.\SQLEXPRESS;Database=ERP_FMCG;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

builder.Configuration["ConnectionStrings:DefaultConnection"] = connectionString;
builder.Configuration["ApiBaseUrl"] = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";

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
    allowedOrigins = new[]
    {
        "http://localhost:5173",
        "http://localhost:5000",
        "http://localhost:5001",
        "http://127.0.0.1:5173",
    };
}

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .SetPreflightMaxAge(TimeSpan.FromSeconds(60));
    }));

// Logging
builder.Services.AddLogging(b => b.AddConsole());

// ============== Build App ==============
var app = builder.Build();

// Root health check
app.MapGet("/", () => Results.Ok(new
{
    name = "ERP FMCG API",
    version = "1.0.0",
    status = "running",
    docs = "/swagger",
    health = "/api/health"
}));

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
