using System.Text.Json;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Linq.Expressions;

namespace ERP.FMC.Application.Services;

/// <summary>
/// Service ghi log và query audit logs.
/// </summary>
public class AuditService : IAuditService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IUnitOfWork uow, ILogger<AuditService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    /// <summary>
    /// Ghi log cho 1 action.
    /// </summary>
    public async Task LogAsync(
        string tableName,
        Guid recordId,
        string action,
        object? oldValues = null,
        object? newValues = null,
        string? operation = null,
        Guid? userId = null,
        string? userName = null,
        string source = "API",
        string? requestId = null,
        string? ipAddress = null,
        string? notes = null,
        CancellationToken ct = default)
    {
        var changedFields = DetectChangedFields(oldValues, newValues);

        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            TableName = tableName,
            RecordId = recordId,
            Action = action,
            Operation = operation,
            OldValues = oldValues != null ? JsonSerializer.Serialize(oldValues) : null,
            NewValues = newValues != null ? JsonSerializer.Serialize(newValues) : null,
            ChangedFields = JsonSerializer.Serialize(changedFields),
            UserId = userId,
            UserName = userName,
            Source = source,
            ValidFrom = DateTime.UtcNow,
            RequestId = requestId,
            IpAddress = ipAddress,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        await _uow.AuditLogs.AddAsync(log);
        await _uow.SaveChangesAsync();
    }

    /// <summary>
    /// Lấy audit logs theo filter.
    /// </summary>
    public async Task<PagedResult<AuditLogDto>> GetLogsAsync(AuditLogFilterDto filter, CancellationToken ct = default)
    {
        var all = await _uow.AuditLogs.GetAllAsync();
        var query = all.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.TableName))
            query = query.Where(l => l.TableName == filter.TableName);
        if (filter.RecordId.HasValue)
            query = query.Where(l => l.RecordId == filter.RecordId);
        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(l => l.Action == filter.Action);
        if (filter.UserId.HasValue)
            query = query.Where(l => l.UserId == filter.UserId);
        if (!string.IsNullOrWhiteSpace(filter.Source))
            query = query.Where(l => l.Source == filter.Source);
        if (filter.FromDate.HasValue)
            query = query.Where(l => l.CreatedAt >= filter.FromDate);
        if (filter.ToDate.HasValue)
            query = query.Where(l => l.CreatedAt <= filter.ToDate);

        var total = query.Count();
        var items = query.OrderByDescending(l => l.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(l => new AuditLogDto
            {
                Id = l.Id,
                TableName = l.TableName,
                RecordId = l.RecordId,
                Action = l.Action,
                Operation = l.Operation,
                OldValues = l.OldValues,
                NewValues = l.NewValues,
                ChangedFields = l.ChangedFields,
                UserId = l.UserId,
                UserName = l.UserName,
                Source = l.Source,
                CreatedAt = l.CreatedAt,
                IpAddress = l.IpAddress,
                Notes = l.Notes
            })
            .ToList();

        return new PagedResult<AuditLogDto>
        {
            Items = items,
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    /// <summary>
    /// Lấy lịch sử thay đổi của 1 record (audit_logs).
    /// </summary>
    public async Task<List<AuditLogDto>> GetRecordHistoryAsync(
        string tableName, Guid recordId, CancellationToken ct = default)
    {
        var logs = (await _uow.AuditLogs.GetAllAsync())
            .Where(l => l.TableName == tableName && l.RecordId == recordId)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new AuditLogDto
            {
                Id = l.Id,
                TableName = l.TableName,
                RecordId = l.RecordId,
                Action = l.Action,
                Operation = l.Operation,
                OldValues = l.OldValues,
                NewValues = l.NewValues,
                ChangedFields = l.ChangedFields,
                UserId = l.UserId,
                UserName = l.UserName,
                Source = l.Source,
                CreatedAt = l.CreatedAt,
                Notes = l.Notes
            })
            .ToList();

        return logs;
    }

    /// <summary>
    /// So sánh 2 phiên bản của 1 record.
    /// </summary>
    public async Task<HistoryComparisonDto?> CompareAsync(
        string tableName, Guid recordId, DateTime fromTime, DateTime toTime,
        CancellationToken ct = default)
    {
        var logs = (await _uow.AuditLogs.GetAllAsync())
            .Where(l => l.TableName == tableName &&
                       l.RecordId == recordId &&
                       l.CreatedAt >= fromTime &&
                       l.CreatedAt <= toTime)
            .OrderBy(l => l.CreatedAt)
            .ToList();

        if (!logs.Any()) return null;

        var oldValues = logs.First().OldValues;
        var newValues = logs.Last().NewValues;

        return new HistoryComparisonDto
        {
            RecordId = recordId,
            FromTime = fromTime,
            ToTime = toTime,
            Changes = CompareJsonValues(oldValues, newValues)
        };
    }

    /// <summary>
    /// Audit stats summary.
    /// </summary>
    public async Task<Dictionary<string, object>> GetStatsAsync(CancellationToken ct = default)
    {
        var all = await _uow.AuditLogs.GetAllAsync();

        return new Dictionary<string, object>
        {
            ["TotalLogs"] = all.Count,
            ["LogsByAction"] = all.GroupBy(l => l.Action).ToDictionary(g => g.Key, g => g.Count()),
            ["LogsByTable"] = all.GroupBy(l => l.TableName).ToDictionary(g => g.Key, g => g.Count()),
            ["LogsBySource"] = all.GroupBy(l => l.Source ?? "Unknown").ToDictionary(g => g.Key, g => g.Count()),
            ["Last24h"] = all.Count(l => l.CreatedAt >= DateTime.UtcNow.AddDays(-1)),
            ["Last7Days"] = all.Count(l => l.CreatedAt >= DateTime.UtcNow.AddDays(-7)),
            ["Last30Days"] = all.Count(l => l.CreatedAt >= DateTime.UtcNow.AddDays(-30))
        };
    }

    // ============ Helpers ============

    private static List<string> DetectChangedFields(object? oldObj, object? newObj)
    {
        if (oldObj == null || newObj == null)
            return new List<string>();

        var oldJson = JsonSerializer.Serialize(oldObj);
        var newJson = JsonSerializer.Serialize(newObj);

        try
        {
            var oldDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(oldJson) ?? new();
            var newDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(newJson) ?? new();

            var changed = new List<string>();
            var allKeys = oldDict.Keys.Union(newDict.Keys);

            foreach (var key in allKeys)
            {
                var oldVal = oldDict.ContainsKey(key) ? oldDict[key].GetRawText() : null;
                var newVal = newDict.ContainsKey(key) ? newDict[key].GetRawText() : null;
                if (oldVal != newVal) changed.Add(key);
            }
            return changed;
        }
        catch
        {
            return new List<string>();
        }
    }

    private static Dictionary<string, FieldChange> CompareJsonValues(string? oldJson, string? newJson)
    {
        var result = new Dictionary<string, FieldChange>();

        if (string.IsNullOrEmpty(oldJson) && string.IsNullOrEmpty(newJson))
            return result;

        var oldDict = string.IsNullOrEmpty(oldJson)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(oldJson) ?? new();

        var newDict = string.IsNullOrEmpty(newJson)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(newJson) ?? new();

        var allKeys = oldDict.Keys.Union(newDict.Keys);

        foreach (var key in allKeys)
        {
            var hasOld = oldDict.TryGetValue(key, out var oldVal);
            var hasNew = newDict.TryGetValue(key, out var newVal);

            string changeType;
            if (!hasOld) changeType = "ADDED";
            else if (!hasNew) changeType = "REMOVED";
            else if (oldVal.GetRawText() != newVal.GetRawText()) changeType = "UPDATED";
            else continue;

            result[key] = new FieldChange
            {
                FieldName = key,
                OldValue = hasOld ? (object?)oldVal.GetRawText() : null,
                NewValue = hasNew ? (object?)newVal.GetRawText() : null,
                ChangeType = changeType
            };
        }

        return result;
    }
}
