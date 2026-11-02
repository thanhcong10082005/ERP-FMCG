using System.Diagnostics;
using Dapper;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace ERP.FMC.Infrastructure.Services;

/// <summary>
/// Smoke Test Service - 10 chức năng chính trước Go-Live.
/// </summary>
public class SmokeTestService : ISmokeTestService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmokeTestService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _connectionString;

    public SmokeTestService(IConfiguration config, ILogger<SmokeTestService> logger, HttpClient? httpClient = null)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _connectionString = config.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task<SmokeTestSuiteDto> RunAsync(CancellationToken ct = default)
    {
        var suite = new SmokeTestSuiteDto();
        var swTotal = Stopwatch.StartNew();

        var tests = new List<Func<CancellationToken, Task<SmokeTestDto>>>
        {
            t => RunHttpTest("Health Check", "/api/health", t),
            t => RunDbTest("Partners List", "partners", "SELECT COUNT(*) FROM [tenant].[partners]", t),
            t => RunDbTest("Products List", "products", "SELECT COUNT(*) FROM [tenant].[products]", t),
            t => RunDbTest("Sales Orders", "sales_orders", "SELECT COUNT(*) FROM [tenant].[sales_orders]", t),
            t => RunDbTest("Categories", "categories", "SELECT COUNT(*) FROM [tenant].[categories]", t),
            t => RunDbTest("Routes", "routes", "SELECT COUNT(*) FROM [tenant].[routes]", t),
            t => RunDbTest("Warehouses", "warehouses", "SELECT COUNT(*) FROM [tenant].[warehouses]", t),
            t => RunDbTest("Inventory", "inventory_balances", "SELECT COUNT(*) FROM [tenant].[inventory_balances]", t),
            t => RunDbTest("Price Lists", "price_lists", "SELECT COUNT(*) FROM [tenant].[price_lists]", t),
            t => RunDbTest("Audit Logs", "audit_logs", "SELECT COUNT(*) FROM [tenant].[audit_logs]", t)
        };

        foreach (var test in tests)
        {
            try { suite.Tests.Add(await test(ct)); }
            catch (Exception ex) { suite.Tests.Add(new SmokeTestDto { Name = "Unknown", Pass = false, ErrorMessage = ex.Message }); }
        }

        swTotal.Stop();
        suite.Total = suite.Tests.Count;
        suite.Passed = suite.Tests.Count(t => t.Pass);
        suite.Failed = suite.Tests.Count(t => !t.Pass);
        suite.Duration = swTotal.Elapsed;
        suite.ExecutedAt = DateTime.UtcNow;

        _logger.LogInformation("Smoke Test: {Passed}/{Total} passed in {Ms}ms", suite.Passed, suite.Total, swTotal.ElapsedMilliseconds);
        return suite;
    }

    private async Task<SmokeTestDto> RunHttpTest(string name, string path, CancellationToken ct)
    {
        var result = new SmokeTestDto { Name = name, Endpoint = path, Method = "GET" };
        var sw = Stopwatch.StartNew();
        try
        {
            var baseUrl = ResolveBaseUrl();
            using var resp = await _httpClient.GetAsync(baseUrl + path, ct);
            sw.Stop();

            result.StatusCode = (int)resp.StatusCode;
            result.ResponseTimeMs = sw.ElapsedMilliseconds;
            result.Pass = resp.IsSuccessStatusCode;
            if (!result.Pass)
                result.ErrorMessage = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Pass = false;
            result.ErrorMessage = ex.Message;
            result.ResponseTimeMs = sw.ElapsedMilliseconds;
        }
        return result;
    }

    /// <summary>
    /// Xác định base URL mà API thực sự đang lắng nghe.
    /// Thứ tự ưu tiên:
    ///   1. Biến môi trường <c>ERP_FMCG_API_BASE_URL</c> (ghi đè rõ ràng nhất).
    ///   2. Cấu hình <c>ApiBaseUrl</c>.
    ///   3. Cổng lắng nghe thực tế của Kestrel lấy từ cấu hình Kestrel
    ///      (đồng nghĩa với endpoint mà API thật sự phục vụ).
    ///   4. Mặc định <c>http://localhost:5000</c>.
    /// </summary>
    private string ResolveBaseUrl()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ERP_FMCG_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.TrimEnd('/');

        // Kestrel:Endpoints:Http:Url  (ví dụ "http://127.0.0.1:5199")
        //   -> phản ánh đúng cổng API thực sự đang phục vụ.
        var kestrelUrl = _config["Kestrel:Endpoints:Http:Url"];
        if (!string.IsNullOrWhiteSpace(kestrelUrl))
            return kestrelUrl.TrimEnd('/');

        var fromConfig = _config["ApiBaseUrl"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
            return fromConfig.TrimEnd('/');

        return "http://localhost:5000";
    }

    private async Task<SmokeTestDto> RunDbTest(string name, string endpoint, string sql, CancellationToken ct)
    {
        var result = new SmokeTestDto { Name = name, Endpoint = $"DB:{endpoint}", Method = "QUERY" };
        var sw = Stopwatch.StartNew();
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            var count = await conn.ExecuteScalarAsync<int>(sql);
            sw.Stop();
            result.Pass = true; result.ResponseTimeMs = sw.ElapsedMilliseconds; result.StatusCode = 200;
        }
        catch (Exception ex) { sw.Stop(); result.Pass = false; result.ErrorMessage = ex.Message; result.ResponseTimeMs = sw.ElapsedMilliseconds; }
        return result;
    }
}
