using System.Text.Json;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Application.Services;

public class ImportService : IImportService
{
    private readonly IUnitOfWork _uow;
    private readonly IAuditService _auditService;
    private readonly ILogger<ImportService> _logger;

    public ImportService(IUnitOfWork uow, IAuditService auditService, ILogger<ImportService> logger)
    {
        _uow = uow;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<Guid> CreateJobAsync(string importType, string fileName, Stream fileStream,
        string errorHandling = "SKIP", Guid? uploadedBy = null, CancellationToken ct = default)
    {
        var job = new ImportJob
        {
            Id = Guid.NewGuid(),
            JobCode = $"IMP-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}",
            ImportType = importType.ToUpper(),
            Status = "PENDING",
            FileName = fileName,
            ErrorHandling = errorHandling,
            UploadedBy = uploadedBy,
            UploadedAt = DateTime.UtcNow
        };
        await _uow.ImportJobs.AddAsync(job);
        await _uow.SaveChangesAsync();
        _logger.LogInformation("Created import job {Code} type={Type}", job.JobCode, importType);
        return job.Id;
    }

    public async Task ProcessImportAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _uow.ImportJobs.GetByIdAsync(jobId);
        if (job == null) throw new InvalidOperationException($"Job {jobId} not found");
        job.Status = "VALIDATING";
        await _uow.SaveChangesAsync();
    }

    public async Task<int> CommitImportAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _uow.ImportJobs.GetByIdAsync(jobId);
        if (job == null) throw new InvalidOperationException($"Job {jobId} not found");

        var stagingRows = (await _uow.ImportStaging.FindAsync(s => s.JobId == jobId && s.ValidationStatus == "VALID"))
            .ToList();

        await _uow.BeginTransactionAsync();
        try
        {
            int inserted = 0;
            foreach (var row in stagingRows)
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(row.ParsedData ?? "{}");
                var (table, id) = await InsertToMainTableAsync(job.ImportType, parsed);
                if (id.HasValue)
                {
                    row.TargetId = id;
                    row.TargetTable = table;
                    row.IsPreview = false;
                    await _auditService.LogAsync(
                        tableName: table,
                        recordId: id.Value,
                        action: "INSERT",
                        operation: $"IMPORT_{job.ImportType}",
                        newValues: parsed,
                        userId: job.UploadedBy,
                        source: "IMPORT",
                        notes: $"Imported via job {job.JobCode}",
                        ct: ct);
                    inserted++;
                }
                await _uow.ImportStaging.UpdateAsync(row);
            }

            job.IsCommitted = true;
            job.CommittedAt = DateTime.UtcNow;
            job.Status = "COMPLETED";
            job.SuccessRows = inserted;
            await _uow.SaveChangesAsync();
            await _uow.CommitTransactionAsync();
            return inserted;
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }

    public async Task<bool> RollbackImportAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _uow.ImportJobs.GetByIdAsync(jobId);
        if (job == null) return false;
        if (!job.IsCommitted) throw new InvalidOperationException("Job not committed");

        var staged = (await _uow.ImportStaging.FindAsync(s => s.JobId == jobId && !s.IsPreview && s.TargetId != null))
            .ToList();

        await _uow.BeginTransactionAsync();
        try
        {
            int deleted = 0;
            foreach (var row in staged)
            {
                if (row.TargetId == null) continue;
                switch (row.TargetTable)
                {
                    case "partners":
                        var p = await _uow.Partners.GetByIdAsync(row.TargetId.Value);
                        if (p != null)
                        {
                            await _auditService.LogAsync(
                                tableName: "partners", recordId: row.TargetId.Value,
                                action: "DELETE", operation: "ROLLBACK_IMPORT",
                                oldValues: p, userId: job.UploadedBy,
                                source: "IMPORT", notes: $"Rollback job {job.JobCode}", ct: ct);
                            await _uow.Partners.DeleteAsync(p);
                            deleted++;
                        }
                        break;
                    case "products":
                        var prod = await _uow.Products.GetByIdAsync(row.TargetId.Value);
                        if (prod != null)
                        {
                            await _auditService.LogAsync(
                                tableName: "products", recordId: row.TargetId.Value,
                                action: "DELETE", operation: "ROLLBACK_IMPORT",
                                oldValues: prod, userId: job.UploadedBy,
                                source: "IMPORT", notes: $"Rollback job {job.JobCode}", ct: ct);
                            await _uow.Products.DeleteAsync(prod);
                            deleted++;
                        }
                        break;
                }
            }
            await _uow.SaveChangesAsync();
            await _uow.CommitTransactionAsync();

            job.IsCommitted = false;
            job.CommittedAt = null;
            job.Status = "ROLLED_BACK";
            await _uow.SaveChangesAsync();
            _logger.LogInformation("Rolled back job {JobCode}, deleted {Count} rows", job.JobCode, deleted);
            return true;
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }

    public async Task<PagedResult<ImportJobDto>> GetJobsAsync(ImportJobFilterDto filter, CancellationToken ct = default)
    {
        var all = await _uow.ImportJobs.GetAllAsync();
        var query = all.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(j => j.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.ImportType))
            query = query.Where(j => j.ImportType == filter.ImportType);

        var list = query.OrderByDescending(j => j.UploadedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize).ToList();

        return new PagedResult<ImportJobDto>
        {
            Items = list.Select(MapJob).ToList(),
            TotalCount = query.Count(),
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<ImportJobDto?> GetJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _uow.ImportJobs.GetByIdAsync(jobId);
        return job == null ? null : MapJob(job);
    }

    public async Task<ImportJobDto> UploadAndProcessAsync(string importType, string fileName, Stream fileStream,
        string errorHandling = "SKIP", CancellationToken ct = default)
    {
        var job = new ImportJob
        {
            Id = Guid.NewGuid(),
            JobCode = $"IMP-{DateTime.UtcNow:yyyyMMddHHmmss}",
            ImportType = importType.ToUpper(),
            Status = "PENDING",
            FileName = fileName,
            ErrorHandling = errorHandling,
            UploadedAt = DateTime.UtcNow
        };
        await _uow.ImportJobs.AddAsync(job);
        await _uow.SaveChangesAsync();
        return MapJob(job);
    }

    public async Task<List<ImportStagingPreviewDto>> PreviewAsync(Guid jobId, int takeCount = 10, CancellationToken ct = default)
    {
        var rows = (await _uow.ImportStaging.FindAsync(s => s.JobId == jobId))
            .Take(takeCount).ToList();

        // ImportError.StagingId la Guid? (co the NULL) -> bo dong loi khong gan
        // staging nao truoc khi group, de key cua ToDictionary la Guid
        // (non-nullable) va khop duoc voi ImportStaging.Id.
        var errors = (await _uow.ImportErrors.FindAsync(e => e.JobId == jobId))
            .Where(e => e.StagingId.HasValue)
            .GroupBy(e => e.StagingId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(r => new ImportStagingPreviewDto
        {
            Id = r.Id,
            RowNumber = r.RowNumber,
            Fields = JsonSerializer.Deserialize<Dictionary<string, string?>>(r.ParsedData ?? r.RawData ?? "{}") ?? new(),
            ValidationStatus = r.ValidationStatus,
            Errors = (errors.GetValueOrDefault(r.Id) ?? new())
                .Where(e => e.Severity == "ERROR")
                .Select(e => new ImportErrorDto
                {
                    Id = e.Id, RowNumber = e.RowNumber, FieldName = e.FieldName,
                    FieldValue = e.FieldValue, ErrorCode = e.ErrorCode,
                    ErrorMessage = e.ErrorMessage, SuggestedFix = e.ResolvedValue
                }).ToList(),
            Warnings = (errors.GetValueOrDefault(r.Id) ?? new())
                .Where(e => e.Severity == "WARNING")
                .Select(e => new ImportWarningDto
                {
                    FieldName = e.FieldName, FieldValue = e.FieldValue,
                    ErrorCode = e.ErrorCode, ErrorMessage = e.ErrorMessage,
                    SuggestedFix = e.ResolvedValue
                }).ToList()
        }).ToList();
    }

    public async Task<ImportErrorSummaryDto> GetErrorSummaryAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _uow.ImportJobs.GetByIdAsync(jobId);
        var errors = (await _uow.ImportErrors.FindAsync(e => e.JobId == jobId)).ToList();

        return new ImportErrorSummaryDto
        {
            JobId = jobId,
            JobCode = job?.JobCode ?? "",
            Status = job?.Status ?? "",
            Summary = new ImportResultSummary
            {
                TotalRows = job?.TotalRows ?? 0,
                SuccessRows = job?.SuccessRows ?? 0,
                ErrorRows = job?.ErrorRows ?? 0,
                WarningRows = job?.WarningRows ?? 0
            },
            Errors = errors.Select(e => new ImportErrorDto
            {
                Id = e.Id, JobId = e.JobId, StagingId = e.StagingId,
                RowNumber = e.RowNumber, FieldName = e.FieldName,
                FieldValue = e.FieldValue, ErrorCode = e.ErrorCode,
                ErrorMessage = e.ErrorMessage, Severity = e.Severity,
                IsResolved = e.IsResolved, ResolvedAt = e.ResolvedAt
            }).ToList(),
            ErrorCountByField = errors.GroupBy(e => e.FieldName ?? "").ToDictionary(g => g.Key, g => g.Count()),
            ErrorCountByCode = errors.GroupBy(e => e.ErrorCode).ToDictionary(g => g.Key, g => g.Count())
        };
    }

    public async Task<int> ResolveErrorsAsync(Guid jobId, List<ErrorResolutionDto> resolutions, CancellationToken ct = default)
    {
        int resolved = 0;
        foreach (var r in resolutions)
        {
            var err = (await _uow.ImportErrors.FindAsync(e => e.Id == r.ErrorId)).FirstOrDefault();
            if (err != null)
            {
                err.ResolvedValue = r.ResolvedValue;
                err.ResolvedBy = r.ResolvedBy;
                err.ResolvedAt = DateTime.UtcNow;
                err.IsResolved = true;
                await _uow.ImportErrors.UpdateAsync(err);
                resolved++;
            }
        }
        await _uow.SaveChangesAsync();
        return resolved;
    }

    // ============ Phương thức nền tảng dữ liệu ============

    public async Task<List<ImportHistoryDto>> GetImportHistoryAsync(
        string? importType = null, int limit = 50, CancellationToken ct = default)
    {
        var all = await _uow.ImportJobs.GetAllAsync();
        var query = all.AsQueryable();

        if (!string.IsNullOrWhiteSpace(importType))
            query = query.Where(j => j.ImportType == importType);

        return query.OrderByDescending(j => j.UploadedAt)
            .Take(limit)
            .Select(j => new ImportHistoryDto
            {
                Id = j.Id,
                JobCode = j.JobCode,
                ImportType = j.ImportType,
                Status = j.Status,
                FileName = j.FileName,
                FileSize = j.FileSize,
                TotalRows = j.TotalRows,
                SuccessRows = j.SuccessRows,
                ErrorRows = j.ErrorRows,
                WarningRows = j.WarningRows,
                UploadedAt = j.UploadedAt,
                CompletedAt = j.CompletedAt,
                IsCommitted = j.IsCommitted,
                UploadedBy = j.UploadedBy
            }).ToList();
    }

    public async Task<BatchResolveResultDto> BatchResolveErrorsAsync(
        Guid jobId, BatchResolveRequestDto request, CancellationToken ct = default)
    {
        var result = new BatchResolveResultDto { TotalRequested = request.Resolutions.Count };

        var errorsToResolve = new List<ImportError>();
        foreach (var res in request.Resolutions)
        {
            var err = (await _uow.ImportErrors.FindAsync(e => e.Id == res.ErrorId)).FirstOrDefault();
            if (err != null && !err.IsResolved)
            {
                // Auto-fix options
                var resolvedValue = res.ResolvedValue;
                if (request.AutoTrimWhitespace)
                    resolvedValue = resolvedValue?.Trim();
                if (request.AutoFixPhoneFormat)
                    resolvedValue = System.Text.RegularExpressions.Regex.Replace(resolvedValue ?? "", @"[^\d]", "");

                err.ResolvedValue = resolvedValue;
                err.ResolvedAt = DateTime.UtcNow;
                err.IsResolved = true;
                errorsToResolve.Add(err);
                result.Successful++;
            }
            else
            {
                result.Failed++;
                result.Errors.Add($"Error {res.ErrorId} not found or already resolved");
            }
        }

        await _uow.SaveChangesAsync();
        _logger.LogInformation("Batch resolved {Count} errors for job {JobId}", result.Successful, jobId);

        return result;
    }

    public async Task<Guid> ReImportAsync(ReImportRequestDto request, CancellationToken ct = default)
    {
        var originalJob = await _uow.ImportJobs.GetByIdAsync(request.OriginalJobId);
        if (originalJob == null)
            throw new InvalidOperationException($"Original job {request.OriginalJobId} not found");

        // Create new job with REIMPORT- prefix
        var newJob = new ImportJob
        {
            Id = Guid.NewGuid(),
            JobCode = $"IMP-RE-{originalJob.JobCode}",
            ImportType = originalJob.ImportType,
            Status = "PENDING",
            FileName = originalJob.FileName,
            FilePath = originalJob.FilePath,
            FileSize = originalJob.FileSize,
            ErrorHandling = originalJob.ErrorHandling,
            UploadedBy = originalJob.UploadedBy,
            UploadedAt = DateTime.UtcNow,
            ErrorSummary = $"Re-import of job {originalJob.JobCode}"
        };

        await _uow.ImportJobs.AddAsync(newJob);
        await _uow.SaveChangesAsync();

        // Copy staging rows from original with overrides
        var originalRows = (await _uow.ImportStaging.FindAsync(s => s.JobId == request.OriginalJobId)).ToList();
        var newRows = new List<ImportStaging>();

        foreach (var row in originalRows)
        {
            var newRow = new ImportStaging
            {
                Id = Guid.NewGuid(),
                JobId = newJob.Id,
                RowNumber = row.RowNumber,
                RowHash = row.RowHash,
                RawData = row.RawData,
                ParsedData = row.ParsedData,
                ValidationStatus = "PENDING",
                IsPreview = true,
                SourceFile = row.SourceFile,
                SourceSheet = row.SourceSheet
            };

            // Apply overrides if any
            if (request.RowOverrides.TryGetValue(row.RowNumber, out var overrides))
            {
                var rawData = JsonSerializer.Deserialize<Dictionary<string, string?>>(row.RawData ?? "{}") ?? new();
                foreach (var kv in overrides)
                    rawData[kv.Key] = kv.Value;
                newRow.RawData = JsonSerializer.Serialize(rawData);
                newRow.ValidationStatus = "OVERRIDDEN";
            }

            newRows.Add(newRow);
        }

        foreach (var r in newRows)
            await _uow.ImportStaging.AddAsync(r);

        // Copy resolved errors
        if (request.KeepResolvedErrors && _uow.ImportErrors != null)
        {
            var allErrors = await _uow.ImportErrors.GetAllAsync();
            var resolvedErrors = allErrors.Where(e => e.JobId == request.OriginalJobId && e.IsResolved).ToList();
            foreach (var err in resolvedErrors)
            {
                var stagingMatch = newRows.FirstOrDefault(r => r.RowNumber == err.RowNumber);
                if (stagingMatch != null && !string.IsNullOrEmpty(err.ResolvedValue))
                {
                    err.Id = Guid.NewGuid();
                    err.JobId = newJob.Id;
                    err.StagingId = stagingMatch.Id;
                    await _uow.ImportErrors.AddAsync(err);
                }
            }
        }

        await _uow.SaveChangesAsync();
        await _auditService.LogAsync(
            tableName: "import_jobs", recordId: newJob.Id,
            action: "INSERT", operation: "REIMPORT",
            newValues: new { OriginalJob = originalJob.JobCode },
            userId: originalJob.UploadedBy,
            source: "API",
            notes: $"Re-imported from {originalJob.JobCode}",
            ct: ct);

        _logger.LogInformation("Created re-import job {NewCode} from {OldCode}",
            newJob.JobCode, originalJob.JobCode);

        return newJob.Id;
    }

    public async Task<UndoResultDto> UndoImportAsync(UndoRequestDto request, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new UndoResultDto { JobId = request.JobId };

        var job = await _uow.ImportJobs.GetByIdAsync(request.JobId);
        if (job == null)
            throw new InvalidOperationException($"Job {request.JobId} not found");
        if (!job.IsCommitted)
            throw new InvalidOperationException("Job not committed, nothing to undo");

        var staged = (await _uow.ImportStaging.FindAsync(s => s.JobId == request.JobId && !s.IsPreview && s.TargetId != null))
            .ToList();

        // Filter by AsOfTime if specified
        if (request.AsOfTime.HasValue)
        {
            staged = staged.Where(s => s.CreatedAt <= request.AsOfTime.Value).ToList();
        }

        await _uow.BeginTransactionAsync();
        try
        {
            int deleted = 0, reverted = 0;
            foreach (var row in staged)
            {
                if (row.TargetId == null) continue;
                switch (row.TargetTable)
                {
                    case "partners":
                        var p = await _uow.Partners.GetByIdAsync(row.TargetId.Value);
                        if (p != null)
                        {
                            await _auditService.LogAsync(
                                tableName: "partners", recordId: row.TargetId.Value,
                                action: "DELETE", operation: "UNDO_IMPORT",
                                oldValues: p, userId: job.UploadedBy,
                                source: "API",
                                notes: $"Undo job {job.JobCode}", ct: ct);
                            await _uow.Partners.DeleteAsync(p);
                            deleted++;
                        }
                        break;
                    case "products":
                        var prod = await _uow.Products.GetByIdAsync(row.TargetId.Value);
                        if (prod != null)
                        {
                            await _auditService.LogAsync(
                                tableName: "products", recordId: row.TargetId.Value,
                                action: "DELETE", operation: "UNDO_IMPORT",
                                oldValues: prod, userId: job.UploadedBy,
                                source: "API",
                                notes: $"Undo job {job.JobCode}", ct: ct);
                            await _uow.Products.DeleteAsync(prod);
                            deleted++;
                        }
                        break;
                }
            }
            await _uow.SaveChangesAsync();
            await _uow.CommitTransactionAsync();

            job.IsCommitted = false;
            job.CommittedAt = null;
            job.Status = "UNDONE";
            await _uow.SaveChangesAsync();

            sw.Stop();
            result.RowsDeleted = deleted;
            result.RowsReverted = reverted;
            result.Duration = sw.Elapsed;
            result.Status = "UNDONE";

            return result;
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }

    // ============ Helpers ============

    private async Task<(string table, Guid? id)> InsertToMainTableAsync(
        string importType, Dictionary<string, object>? parsed)
    {
        if (parsed == null) return (string.Empty, null);

        switch (importType.ToUpper())
        {
            case "CUSTOMERS":
            case "PARTNERS":
                var partner = new Partner
                {
                    Id = Guid.NewGuid(),
                    PartnerCode = parsed.GetValueOrDefault("partner_code")?.ToString() ?? $"IMP-{Guid.NewGuid():N}".Substring(0, 8),
                    PartnerName = parsed.GetValueOrDefault("partner_name")?.ToString() ?? "",
                    PartnerType = parsed.GetValueOrDefault("partner_type")?.ToString() ?? "STORE",
                    Phone = parsed.GetValueOrDefault("phone")?.ToString(),
                    Email = parsed.GetValueOrDefault("email")?.ToString(),
                    Address = parsed.GetValueOrDefault("address")?.ToString(),
                    RouteCode = parsed.GetValueOrDefault("route_code")?.ToString(),
                    IsActive = true
                };
                await _uow.Partners.AddAsync(partner);
                await _uow.SaveChangesAsync();
                return ("partners", partner.Id);

            case "PRODUCTS":
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    Sku = parsed.GetValueOrDefault("sku")?.ToString() ?? $"SKU-{Guid.NewGuid():N}".Substring(0, 8),
                    ProductName = parsed.GetValueOrDefault("product_name")?.ToString() ?? "",
                    ShortName = parsed.GetValueOrDefault("short_name")?.ToString(),
                    Brand = parsed.GetValueOrDefault("brand")?.ToString(),
                    BaseUom = "CAI",
                    BasePrice = decimal.TryParse(parsed.GetValueOrDefault("selling_price")?.ToString(), out var p) ? p : 0,
                    VatRate = decimal.TryParse(parsed.GetValueOrDefault("vat_rate")?.ToString(), out var v) ? v : 8.0m,
                    IsActive = true
                };
                await _uow.Products.AddAsync(product);
                await _uow.SaveChangesAsync();
                return ("products", product.Id);

            default:
                _logger.LogWarning("Import type {Type} not implemented yet", importType);
                return (string.Empty, null);
        }
    }

    private static ImportJobDto MapJob(ImportJob j) => new()
    {
        Id = j.Id,
        JobCode = j.JobCode,
        ImportType = j.ImportType,
        Status = j.Status,
        FileName = j.FileName,
        FileSize = j.FileSize,
        TotalRows = j.TotalRows,
        ProcessedRows = j.ProcessedRows,
        SuccessRows = j.SuccessRows,
        ErrorRows = j.ErrorRows,
        WarningRows = j.WarningRows,
        ErrorHandling = j.ErrorHandling,
        UploadedAt = j.UploadedAt,
        IsCommitted = j.IsCommitted,
        CommittedAt = j.CommittedAt,
        CompletedAt = j.CompletedAt,
        ErrorSummary = j.ErrorSummary
    };
}
