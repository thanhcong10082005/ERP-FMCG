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
/// Monitoring Service cho Go-Live.
/// </summary>
public class MonitoringService : IMonitoringService
{
    private readonly IUnitOfWork _uow;
    private readonly IConfiguration _config;
    private readonly ILogger<MonitoringService> _logger;
    private readonly string _connectionString;
    private static readonly List<MonitoringMetricDto> _metrics = new();
    private static readonly List<SupportTicketDto> _tickets = new();
    private static readonly object _lock = new();

    public MonitoringService(IUnitOfWork uow, IConfiguration config, ILogger<MonitoringService> logger)
    {
        _uow = uow;
        _config = config;
        _logger = logger;
        _connectionString = config.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task<Dictionary<string, object>> GetMetricsAsync(CancellationToken ct = default)
    {
        var process = Process.GetCurrentProcess();
        var metrics = new Dictionary<string, object>
        {
            ["ServerName"] = Environment.MachineName,
            ["CpuUsage"] = Math.Round(GetCpuUsage(), 2),
            ["MemoryMb"] = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2),
            ["CheckedAt"] = DateTime.UtcNow,
            ["ApiVersion"] = "1.0.0"
        };

        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            metrics["DbActiveConnections"] = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.dm_exec_connections WHERE database_id = DB_ID('ERP_FMCG')");
            metrics["DbRecentRequests5Min"] = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE start_time >= DATEADD(MINUTE, -5, GETUTCDATE())");
        }
        catch (Exception ex) { metrics["DbError"] = ex.Message; }

        return metrics;
    }

    public void RecordMetric(string name, double value, string unit = "")
    {
        lock (_lock)
        {
            _metrics.Add(new MonitoringMetricDto { Name = name, Value = value, Unit = unit, Timestamp = DateTime.UtcNow });
            if (_metrics.Count > 1000) _metrics.RemoveRange(0, _metrics.Count - 1000);
        }
    }

    public List<MonitoringMetricDto> GetRecentMetrics(int count = 100)
    {
        lock (_lock) { return _metrics.OrderByDescending(m => m.Timestamp).Take(count).ToList(); }
    }

    public void LogError(string source, string message, Exception? ex = null)
    {
        _logger.LogError(ex, "[{Source}] {Message}", source, message);
        lock (_lock) { _metrics.Add(new MonitoringMetricDto { Name = $"error.{source}", Value = 1, Unit = "count", Timestamp = DateTime.UtcNow, Status = "CRITICAL" }); }
    }

    public Guid CreateTicket(string title, string description, string severity, string? userEmail = null, string? endpoint = null, string? errorTrace = null)
    {
        var ticket = new SupportTicketDto { Id = Guid.NewGuid(), Title = title, Description = description, Severity = severity, Status = "OPEN", UserEmail = userEmail, Endpoint = endpoint, ErrorTrace = errorTrace, CreatedAt = DateTime.UtcNow };
        lock (_lock) { _tickets.Add(ticket); }
        _logger.LogInformation("Ticket {Id} created: {Title}", ticket.Id, title);
        return ticket.Id;
    }

    public List<SupportTicketDto> GetTickets(string? status = null, string? severity = null)
    {
        lock (_lock)
        {
            var query = _tickets.AsEnumerable();
            if (!string.IsNullOrEmpty(status)) query = query.Where(t => t.Status == status);
            if (!string.IsNullOrEmpty(severity)) query = query.Where(t => t.Severity == severity);
            return query.OrderByDescending(t => t.CreatedAt).ToList();
        }
    }

    public bool ResolveTicket(Guid ticketId, string resolutionNotes)
    {
        lock (_lock)
        {
            var ticket = _tickets.FirstOrDefault(t => t.Id == ticketId);
            if (ticket == null) return false;
            ticket.Status = "RESOLVED";
            ticket.ResolvedAt = DateTime.UtcNow;
            ticket.ResolutionNotes = resolutionNotes;
            return true;
        }
    }

    private static double GetCpuUsage()
    {
        try { var p = Process.GetCurrentProcess(); return p.TotalProcessorTime.TotalMilliseconds / Math.Max(1, Environment.TickCount) * 100; }
        catch { return 0; }
    }
}
