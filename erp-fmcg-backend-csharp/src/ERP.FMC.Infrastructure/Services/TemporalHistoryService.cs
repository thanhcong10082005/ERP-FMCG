using System.Text.Json;
using Dapper;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Infrastructure.Services;

/// <summary>
/// Temporal Table history queries (sử dụng Dapper cho FOR SYSTEM_TIME queries).
/// </summary>
public class TemporalHistoryService
{
    private readonly string _connectionString;
    private readonly IAuditService _auditService;
    private readonly ILogger<TemporalHistoryService> _logger;

    private static readonly HashSet<string> _temporalTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "partners", "products", "sales_orders", "price_lists"
    };

    public TemporalHistoryService(
        IConfiguration config,
        IAuditService auditService,
        ILogger<TemporalHistoryService> logger)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentNullException("DefaultConnection");
        _auditService = auditService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy lịch sử đầy đủ của 1 record theo thời gian.
    /// </summary>
    public async Task<TemporalEntityHistoryDto?> GetEntityHistoryAsync(
        string tableName, Guid recordId, DateTime? fromTime = null, DateTime? toTime = null,
        CancellationToken ct = default)
    {
        if (!_temporalTables.Contains(tableName))
            throw new ArgumentException($"Table '{tableName}' is not a temporal table");

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var timeClause = (fromTime.HasValue, toTime.HasValue) switch
        {
            (true, true) => "AND SysStartTime >= @fromTime AND SysStartTime <= @toTime",
            (true, false) => "AND SysStartTime >= @fromTime",
            (false, true) => "AND SysStartTime <= @toTime",
            _ => ""
        };

        var sql = $@"
            SELECT * FROM [tenant].[{tableName}]
            FOR SYSTEM_TIME ALL
            WHERE id = @recordId {timeClause}
            ORDER BY SysStartTime DESC";

        using var multi = await conn.QueryMultipleAsync(sql, new { recordId, fromTime, toTime });

        var history = new List<HistoryRecordDto>();
        var rows = await multi.ReadAsync<dynamic>();
        foreach (var row in rows)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in row)
            {
                var key = ((System.Reflection.PropertyInfo)prop.Key)?.Name ?? prop.Key.ToString();
                dict[key] = prop.Value;
            }

            var sysEnd = (DateTime)row.SysEndTime;
            var isCurrent = sysEnd == new DateTime(9999, 12, 31, 23, 59, 59);

            history.Add(new HistoryRecordDto
            {
                ValidFrom = (DateTime)row.SysStartTime,
                ValidTo = sysEnd,
                IsCurrent = isCurrent,
                Values = dict,
                ChangedBy = "System"
            });
        }

        if (!history.Any()) return null;

        return new TemporalEntityHistoryDto
        {
            RecordId = recordId,
            TableName = tableName,
            TotalVersions = history.Count,
            FirstVersionAt = history.Last().ValidFrom,
            LastVersionAt = history.First().ValidFrom,
            History = history
        };
    }

    /// <summary>
    /// Query temporal data tại 1 thời điểm.
    /// </summary>
    public async Task<List<T>> GetAsOfAsync<T>(
        string tableName, DateTime asOf,
        string? whereClause = null,
        CancellationToken ct = default) where T : class
    {
        if (!_temporalTables.Contains(tableName))
            throw new ArgumentException($"Table '{tableName}' is not a temporal table");

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM [tenant].[{tableName}] FOR SYSTEM_TIME AS OF @asOf {(whereClause != null ? "WHERE " + whereClause : "")}";
        var result = await conn.QueryAsync<T>(sql, new { asOf });
        return result.ToList();
    }

    /// <summary>
    /// So sánh dữ liệu giữa 2 thời điểm.
    /// </summary>
    public async Task<HistoryComparisonDto> CompareVersionsAsync(
        string tableName, Guid recordId, DateTime fromTime, DateTime toTime,
        CancellationToken ct = default)
    {
        if (!_temporalTables.Contains(tableName))
            throw new ArgumentException($"Table '{tableName}' is not a temporal table");

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Get row at fromTime
        var fromSql = $"SELECT * FROM [tenant].[{tableName}] FOR SYSTEM_TIME AS OF @fromTime WHERE id = @recordId";
        var fromRow = await conn.QueryFirstOrDefaultAsync<dynamic>(fromSql, new { fromTime, recordId });

        // Get row at toTime
        var toSql = $"SELECT * FROM [tenant].[{tableName}] FOR SYSTEM_TIME AS OF @toTime WHERE id = @recordId";
        var toRow = await conn.QueryFirstOrDefaultAsync<dynamic>(toSql, new { toTime, recordId });

        if (fromRow == null || toRow == null)
            throw new InvalidOperationException("Record not found at one of the time points");

        var changes = new Dictionary<string, FieldChange>();
        var allKeys = ((IDictionary<string, object>)fromRow).Keys.Union(((IDictionary<string, object>)toRow).Keys);

        foreach (var key in allKeys)
        {
            if (key.StartsWith("Sys")) continue;

            var fromDict = (IDictionary<string, object>)fromRow;
            var toDict = (IDictionary<string, object>)toRow;
            var fromVal = fromDict.ContainsKey(key) ? fromDict[key] : null;
            var toVal = toDict.ContainsKey(key) ? toDict[key] : null;

            if (!Equals(fromVal?.ToString(), toVal?.ToString()))
            {
                changes[key] = new FieldChange
                {
                    FieldName = key,
                    OldValue = fromVal,
                    NewValue = toVal,
                    ChangeType = "UPDATED"
                };
            }
        }

        return new HistoryComparisonDto
        {
            RecordId = recordId,
            FromTime = fromTime,
            ToTime = toTime,
            Changes = changes
        };
    }

    /// <summary>
    /// Undo/Rollback 1 record về 1 thời điểm trước đó.
    /// </summary>
    public async Task<bool> RestoreToAsync(
        string tableName, Guid recordId, DateTime targetTime,
        Guid? userId = null, string? userName = null,
        CancellationToken ct = default)
    {
        if (!_temporalTables.Contains(tableName))
            throw new ArgumentException($"Table '{tableName}' is not a temporal table");

        // Get old version
        var history = await GetEntityHistoryAsync(tableName, recordId, null, targetTime, ct);
        if (history == null) return false;

        var targetVersion = history.History
            .Where(h => h.ValidFrom <= targetTime)
            .OrderByDescending(h => h.ValidFrom)
            .FirstOrDefault();

        if (targetVersion == null) return false;

        // Update current record to match old version
        var updates = targetVersion.Values
            .Where(kvp => !kvp.Key.StartsWith("Sys") && kvp.Key != "id")
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var setClauses = updates.Keys.Select(k => $"{k} = @{k}").ToList();
        var sql = $"UPDATE [tenant].[{tableName}] SET {string.Join(", ", setClauses)} WHERE id = @id";

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var parameters = new DynamicParameters();
        parameters.Add("id", recordId);
        foreach (var kvp in updates)
        {
            parameters.Add(kvp.Key, kvp.Value ?? DBNull.Value);
        }

        var rowsAffected = await conn.ExecuteAsync(sql, parameters);

        if (rowsAffected > 0)
        {
            await _auditService.LogAsync(
                tableName: tableName,
                recordId: recordId,
                action: "RESTORE",
                operation: $"RESTORE_TO_{targetTime:yyyyMMddHHmmss}",
                oldValues: updates,
                newValues: null,
                userId: userId,
                userName: userName,
                source: "API",
                notes: $"Restored to version at {targetTime:yyyy-MM-dd HH:mm:ss}",
                ct: ct);

            _logger.LogInformation("Restored {Table}/{Id} to version at {Time}", tableName, recordId, targetTime);
        }

        return rowsAffected > 0;
    }

    /// <summary>
    /// Lấy tất cả temporal tables.
    /// </summary>
    public IReadOnlyList<string> GetTemporalTables() => _temporalTables.ToList();
}
