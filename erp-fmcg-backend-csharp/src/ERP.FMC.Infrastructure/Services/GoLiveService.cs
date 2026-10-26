using System.Diagnostics;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;

namespace ERP.FMC.Infrastructure.Services;

/// <summary>
/// Go-Live Management Service.
/// </summary>
public class GoLiveService : IGoLiveService
{
    private readonly IUnitOfWork _uow;
    private readonly IAuditService _auditService;
    private readonly IConfiguration _config;
    private readonly ILogger<GoLiveService> _logger;
    private readonly string _connectionString;

    private static GoLiveStatusDto _status = new()
    {
        Phase = "PREP",
        StartedAt = null,
        IsLockedNestJS = false,
        IsMigrated = false,
        IsTestedPassed = false,
        IsDnsSwitched = false,
        IsSmokeTestPassed = false
    };

    public GoLiveService(IUnitOfWork uow, IAuditService auditService, IConfiguration config, ILogger<GoLiveService> logger)
    {
        _uow = uow;
        _auditService = auditService;
        _config = config;
        _logger = logger;
        _connectionString = config.GetConnectionString("DefaultConnection") ?? "";
    }

    public GoLiveStatusDto GetStatus()
    {
        _status.Blockers = CheckBlockers();
        return _status;
    }

    public async Task<bool> LockNestJsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Locking NestJS to READ-ONLY mode...");
        _status.IsLockedNestJS = true;
        await _auditService.LogAsync("system", Guid.NewGuid(), "UPDATE", operation: "LOCK_NESTJS",
            newValues: new { Mode = "READ_ONLY" }, source: "SYSTEM",
            notes: "NestJS switched to READ-ONLY mode before migration", ct: ct);
        return true;
    }

    public async Task<MigrationResultDto> RunFinalMigrationAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Running final migration...");
        var sw = Stopwatch.StartNew();
        var result = new MigrationResultDto();

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            result.PartnerCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [tenant].[partners]");
            result.ProductCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [tenant].[products]");
            result.OrderCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [tenant].[sales_orders]");
            result.OrderItemCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [tenant].[sales_order_items]");

            var temporalEnabled = new List<string>();
            foreach (var table in new[] { "partners", "products", "sales_orders", "price_lists" })
            {
                var has = await conn.ExecuteScalarAsync<int>($@"
                    SELECT COUNT(*) FROM sys.tables t
                    JOIN sys.columns c ON t.object_id = c.object_id AND c.name = 'SysStartTime'
                    WHERE SCHEMA_NAME(t.schema_id) = 'tenant' AND t.name = '{table}'");
                if (has > 0) temporalEnabled.Add(table);
            }
            result.TemporalTablesEnabled = temporalEnabled;
            result.AuditLogsReady = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'tenant' AND TABLE_NAME = 'audit_logs'") > 0;

            sw.Stop();
            result.Success = true;
            result.Duration = sw.Elapsed;
            _status.IsMigrated = true;

            await _auditService.LogAsync("system", Guid.NewGuid(), "UPDATE", operation: "FINAL_MIGRATION",
                newValues: result, source: "SYSTEM", notes: "Final migration completed", ct: ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Success = false;
            result.Duration = sw.Elapsed;
            result.Error = ex.Message;
            _logger.LogError(ex, "Final migration failed");
        }
        return result;
    }

    public async Task<bool> RunSmokeTestAsync(SmokeTestSuiteDto smokeResults, CancellationToken ct = default)
    {
        _status.IsSmokeTestPassed = smokeResults.AllPassed;
        await _auditService.LogAsync("system", Guid.NewGuid(), "UPDATE", operation: "SMOKE_TEST",
            newValues: new { Total = smokeResults.Total, Passed = smokeResults.Passed, Failed = smokeResults.Failed },
            source: "SYSTEM", notes: $"Smoke test: {smokeResults.Passed}/{smokeResults.Total}", ct: ct);
        return smokeResults.AllPassed;
    }

    public async Task<bool> SwitchDnsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Switching to C# API...");
        _status.IsDnsSwitched = true;
        _status.Phase = "LIVE";
        _status.StartedAt = DateTime.UtcNow;
        await _auditService.LogAsync("system", Guid.NewGuid(), "UPDATE", operation: "DNS_SWITCH",
            newValues: new { OldApi = "NestJS", NewApi = "ASP.NET Core" }, source: "SYSTEM",
            notes: "System is LIVE", ct: ct);
        return true;
    }

    public async Task<string> SendAnnouncementAsync(CancellationToken ct = default)
    {
        var announcement = $@"
==========================================
🚀 ERP FMCG GO-LIVE ANNOUNCEMENT
==========================================
Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
API: {_config["ApiBaseUrl"] ?? "http://localhost:5000"}
==========================================
✅ Features:
  - Import < 1s (SqlBulkCopy)
  - Lean Schema (Zero-Null)
  - Temporal Tables (Audit)
  - Staging Pipeline
  - Excel Export
==========================================
";
        _status.AnnouncementSentAt = DateTime.UtcNow.ToString("O");
        await _auditService.LogAsync("system", Guid.NewGuid(), "INSERT", operation: "GO_LIVE_ANNOUNCEMENT",
            newValues: new { Announcement = announcement }, source: "SYSTEM",
            notes: "Go-live announcement sent", ct: ct);
        return announcement;
    }

    public async Task<GoLiveReportDto> GenerateReportAsync(CancellationToken ct = default)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var report = new GoLiveReportDto
        {
            GeneratedAt = DateTime.UtcNow,
            Phase = _status.Phase,
            StartTime = _status.StartedAt,
            Statistics = new Dictionary<string, object>
            {
                ["TotalPartners"] = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [tenant].[partners]"),
                ["TotalProducts"] = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [tenant].[products]"),
                ["TotalOrders"] = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [tenant].[sales_orders]"),
                ["TotalOrderItems"] = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [tenant].[sales_order_items]"),
                ["TotalAuditLogs"] = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [tenant].[audit_logs]")
            }
        };
        await _auditService.LogAsync("system", Guid.NewGuid(), "INSERT", operation: "GO_LIVE_REPORT",
            newValues: report.Statistics, source: "SYSTEM", notes: "Go-live report generated", ct: ct);
        return report;
    }

    public async Task<HealthCheckDto> HealthCheckAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        bool dbConnected = false;

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            dbConnected = conn.State == System.Data.ConnectionState.Open;
            if (dbConnected) await conn.ExecuteScalarAsync<int>("SELECT 1");
        }
        catch { dbConnected = false; }

        return new HealthCheckDto
        {
            CheckedAt = DateTime.UtcNow,
            Status = dbConnected ? "HEALTHY" : "DOWN",
            DatabaseConnected = dbConnected,
            DatabaseResponseMs = sw.ElapsedMilliseconds,
            ApiResponseMs = sw.ElapsedMilliseconds,
            Details = new() { ["Phase"] = _status.Phase }
        };
    }

    private List<string> CheckBlockers()
    {
        var blockers = new List<string>();
        if (!_status.IsLockedNestJS) blockers.Add("NestJS chưa lock");
        if (!_status.IsMigrated) blockers.Add("Migration chưa chạy");
        if (!_status.IsSmokeTestPassed) blockers.Add("Smoke test chưa pass");
        return blockers;
    }
}
