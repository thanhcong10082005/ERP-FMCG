using ERP.FMC.Application.Interfaces;
using ERP.FMC.Application.Mappings;
using ERP.FMC.Application.Services;
using ERP.FMC.Domain.Interfaces;
using ERP.FMC.Infrastructure.Data;
using ERP.FMC.Infrastructure.Repositories;
using ERP.FMC.Infrastructure.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
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

// AutoMapper
builder.Services.AddAutoMapper(typeof(MappingProfile).Assembly);

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
builder.Services.AddScoped<ImportStagingService>();
builder.Services.AddScoped<TemporalHistoryService>();
builder.Services.AddSingleton<ValidationEngine>();

// DI - Phase 5 Services
builder.Services.AddScoped<IUatTestService, UatTestService>();
builder.Services.AddScoped<ISmokeTestService, SmokeTestService>();
builder.Services.AddScoped<IGoLiveService, GoLiveService>();
builder.Services.AddScoped<IMonitoringService, MonitoringService>();

// DI - HttpClient for smoke tests
builder.Services.AddHttpClient();

// Validators
builder.Services.AddValidatorsFromAssemblyContaining<MappingProfile>();

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

// CORS
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

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
app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();
