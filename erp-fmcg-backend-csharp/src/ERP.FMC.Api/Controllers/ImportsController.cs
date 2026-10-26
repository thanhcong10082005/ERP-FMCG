using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace ERP.FMC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ImportsController : ControllerBase
{
    private readonly IImportService _importService;
    private readonly ImportStagingService _stagingService;
    private readonly IWebHostEnvironment _env;

    public ImportsController(
        IImportService importService,
        ImportStagingService stagingService,
        IWebHostEnvironment env)
    {
        _importService = importService;
        _stagingService = stagingService;
        _env = env;
    }

    /// <summary>Upload file + parse + validate (full pipeline).</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100_000_000)]
    public async Task<ActionResult<ImportJobDto>> Upload(
        [FromForm] string importType,
        [FromForm] string errorHandling = "SKIP",
        [FromForm] IFormFile? file = null,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "File is required" });

        var job = await _importService.UploadAndProcessAsync(importType, file.FileName, file.OpenReadStream(), errorHandling, ct);
        var jobId = job.Id;

        try
        {
            await _stagingService.ParseAndStageAsync(jobId, ct);
            await _stagingService.ValidateAllAsync(jobId, ct);

            var errors = await _importService.GetErrorSummaryAsync(jobId, ct);
            if (errors.Errors.All(e => e.IsResolved))
                await _importService.CommitImportAsync(jobId, ct);

            var result = await _importService.GetJobAsync(jobId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Get job list (paginated).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ImportJobDto>>> GetJobs(
        [FromQuery] ImportJobFilterDto filter, CancellationToken ct)
        => Ok(await _importService.GetJobsAsync(filter, ct));

    /// <summary>Get import history (recent jobs).</summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<ImportHistoryDto>>> GetHistory(
        [FromQuery] string? importType = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Ok(await _importService.GetImportHistoryAsync(importType, limit, ct));

    /// <summary>Get job details.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ImportJobDto>> GetJob(Guid id, CancellationToken ct)
    {
        var job = await _importService.GetJobAsync(id, ct);
        return job == null ? NotFound() : Ok(job);
    }

    /// <summary>Preview staging rows.</summary>
    [HttpGet("{id:guid}/preview")]
    public async Task<ActionResult<List<ImportStagingPreviewDto>>> Preview(
        Guid id, [FromQuery] int takeCount = 10, CancellationToken ct = default)
        => Ok(await _importService.PreviewAsync(id, takeCount, ct));

    /// <summary>Get error summary.</summary>
    [HttpGet("{id:guid}/errors")]
    public async Task<ActionResult<ImportErrorSummaryDto>> GetErrors(Guid id, CancellationToken ct)
        => Ok(await _importService.GetErrorSummaryAsync(id, ct));

    /// <summary>Resolve single error.</summary>
    [HttpPost("{id:guid}/resolve")]
    public async Task<ActionResult<int>> ResolveErrors(
        Guid id, [FromBody] List<ErrorResolutionDto> resolutions, CancellationToken ct)
        => Ok(await _importService.ResolveErrorsAsync(id, resolutions, ct));

    /// <summary>Batch resolve errors với auto-fix options.</summary>
    [HttpPost("{id:guid}/resolve-batch")]
    public async Task<ActionResult<BatchResolveResultDto>> BatchResolve(
        Guid id, [FromBody] BatchResolveRequestDto request, CancellationToken ct)
        => Ok(await _importService.BatchResolveErrorsAsync(id, request, ct));

    /// <summary>Commit import.</summary>
    [HttpPost("{id:guid}/commit")]
    public async Task<ActionResult> Commit(Guid id, CancellationToken ct)
    {
        try
        {
            var count = await _importService.CommitImportAsync(id, ct);
            return Ok(new { committedRows = count });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Rollback import.</summary>
    [HttpPost("{id:guid}/rollback")]
    public async Task<ActionResult> Rollback(Guid id, CancellationToken ct)
    {
        try
        {
            await _importService.RollbackImportAsync(id, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Undo import với audit trail.</summary>
    [HttpPost("{id:guid}/undo")]
    public async Task<ActionResult<UndoResultDto>> Undo(
        Guid id, [FromBody] UndoRequestDto request, CancellationToken ct)
    {
        try
        {
            request.JobId = id;
            var result = await _importService.UndoImportAsync(request, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Re-import: tạo job mới từ job cũ.</summary>
    [HttpPost("reimport")]
    public async Task<ActionResult<Guid>> ReImport([FromBody] ReImportRequestDto request, CancellationToken ct)
    {
        try
        {
            var newJobId = await _importService.ReImportAsync(request, ct);
            return Ok(new { newJobId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
