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
            t => Task.FromResult(new SmokeTestDto { Name = "Health Check", Endpoint = "/api/health", Method = "GET" }),
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
