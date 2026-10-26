using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace ERP.FMC.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Produces("application/json")]
public class AuditController : ControllerBase
{
    private readonly IAuditService _auditService;
    private readonly TemporalHistoryService _temporalService;

    public AuditController(
        IAuditService auditService,
        TemporalHistoryService temporalService)
    {
        _auditService = auditService;
        _temporalService = temporalService;
    }

    /// <summary>
    /// Get audit logs theo filter.
    /// </summary>
    [HttpGet("logs")]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> GetLogs(
        [FromQuery] AuditLogFilterDto filter, CancellationToken ct)
        => Ok(await _auditService.GetLogsAsync(filter, ct));

    /// <summary>
    /// Get audit stats summary.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<Dictionary<string, object>>> GetStats(CancellationToken ct)
        => Ok(await _auditService.GetStatsAsync(ct));

    /// <summary>
    /// Get history của 1 record qua audit logs.
    /// </summary>
    [HttpGet("records/{tableName}/{recordId:guid}")]
    public async Task<ActionResult<List<AuditLogDto>>> GetRecordHistory(
        string tableName, Guid recordId, CancellationToken ct)
        => Ok(await _auditService.GetRecordHistoryAsync(tableName, recordId, ct));

    /// <summary>
    /// So sánh 2 phiên bản của 1 record qua audit logs.
    /// </summary>
    [HttpGet("compare/{tableName}/{recordId:guid}")]
    public async Task<ActionResult<HistoryComparisonDto>> Compare(
        string tableName, Guid recordId,
        [FromQuery] DateTime fromTime, [FromQuery] DateTime toTime,
        CancellationToken ct)
    {
        var result = await _auditService.CompareAsync(tableName, recordId, fromTime, toTime, ct);
        return result == null ? NotFound() : Ok(result);
    }

    // ============ Temporal Tables ============

    /// <summary>
    /// Lấy danh sách các temporal tables.
    /// </summary>
    [HttpGet("temporal/tables")]
    public ActionResult<IReadOnlyList<string>> GetTemporalTables()
        => Ok(_temporalService.GetTemporalTables());

    /// <summary>
    /// Lấy full history của 1 record từ Temporal Table.
    /// </summary>
    [HttpGet("temporal/{tableName}/{recordId:guid}")]
    public async Task<ActionResult<TemporalEntityHistoryDto>> GetTemporalHistory(
        string tableName, Guid recordId,
        [FromQuery] DateTime? fromTime = null,
        [FromQuery] DateTime? toTime = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _temporalService.GetEntityHistoryAsync(tableName, recordId, fromTime, toTime, ct);
            return result == null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// So sánh 2 versions của record tại 2 thời điểm (Temporal).
    /// </summary>
    [HttpGet("temporal/{tableName}/{recordId:guid}/compare")]
    public async Task<ActionResult<HistoryComparisonDto>> CompareTemporalVersions(
        string tableName, Guid recordId,
        [FromQuery] DateTime fromTime, [FromQuery] DateTime toTime,
        CancellationToken ct)
    {
        try
        {
            var result = await _temporalService.CompareVersionsAsync(tableName, recordId, fromTime, toTime, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Restore 1 record về phiên bản tại 1 thời điểm.
    /// </summary>
    [HttpPost("temporal/{tableName}/{recordId:guid}/restore")]
    public async Task<ActionResult> RestoreTo(
        string tableName, Guid recordId,
        [FromQuery] DateTime targetTime,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? userName = null,
        CancellationToken ct = default)
    {
        try
        {
            var ok = await _temporalService.RestoreToAsync(tableName, recordId, targetTime, userId, userName, ct);
            return ok ? Ok(new { restored = true, targetTime }) : NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
