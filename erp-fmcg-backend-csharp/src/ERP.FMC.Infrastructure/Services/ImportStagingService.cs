using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ClosedXML.Excel;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Services;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Infrastructure.Services;

/// <summary>
/// Full import staging pipeline service.
/// </summary>
public class ImportStagingService
{
    private readonly IUnitOfWork _uow;
    private readonly ValidationEngine _validator;
    private readonly BulkOperationService _bulkOps;
    private readonly ILogger<ImportStagingService> _logger;
    private readonly string _uploadFolder;

    public ImportStagingService(
        IUnitOfWork uow,
        ValidationEngine validator,
        BulkOperationService bulkOps,
        ILogger<ImportStagingService> logger)
    {
        _uow = uow;
        _validator = validator;
        _bulkOps = bulkOps;
        _logger = logger;
        _uploadFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ERP_FMCG", "Uploads");
        Directory.CreateDirectory(_uploadFolder);
    }

    /// <summary>
    /// Tạo job mới và lưu file upload.
    /// </summary>
    public async Task<ImportJob> CreateJobAsync(
        string importType,
        string fileName,
        Stream fileStream,
        string errorHandling = "SKIP",
        Guid? uploadedBy = null)
    {
        // Generate job code
        var jobCode = $"IMP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";

        // Save file
        var filePath = Path.Combine(_uploadFolder, $"{jobCode}_{fileName}");
        await using (var fs = new FileStream(filePath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fs);
        }

        // Calculate hash
        var hash = await ComputeFileHashAsync(filePath);
        var fileSize = new FileInfo(filePath).Length;

        // Create job
        var job = new ImportJob
        {
            Id = Guid.NewGuid(),
            JobCode = jobCode,
            ImportType = importType.ToUpper(),
            Status = "PENDING",
            FileName = fileName,
            FilePath = filePath,
            FileSize = fileSize,
            FileHash = hash,
            ErrorHandling = errorHandling,
            UploadedBy = uploadedBy,
            UploadedAt = DateTime.UtcNow
        };

        await _uow.ImportJobs.AddAsync(job);
        await _uow.SaveChangesAsync();

        _logger.LogInformation("Created import job {JobCode} for {File} ({Size:N0} bytes)",
            jobCode, fileName, fileSize);

        return job;
    }

    /// <summary>
    /// Parse file và ghi vào staging table.
    /// </summary>
    public async Task ParseAndStageAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _uow.ImportJobs.GetByIdAsync(jobId);
        if (job == null) throw new InvalidOperationException($"Job {jobId} not found");

        job.Status = "PARSING";
        await _uow.SaveChangesAsync();

        try
        {
            var extension = Path.GetExtension(job.FilePath ?? string.Empty).ToLower();
            Dictionary<int, Dictionary<string, string?>> rows;

            if (extension == ".csv")
                rows = await ParseCsvAsync(job.FilePath!, ct);
            else if (extension == ".xlsx" || extension == ".xls")
                rows = await ParseExcelAsync(job.FilePath!, ct);
            else
                throw new NotSupportedException($"File format '{extension}' not supported. Use CSV or XLSX.");

            job.TotalRows = rows.Count;
            job.Status = "VALIDATING";
            await _uow.SaveChangesAsync();

            // Insert into staging
            var stagingRows = rows.Select(kvp => new ImportStaging
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                RowNumber = kvp.Key,
                RawData = JsonSerializer.Serialize(kvp.Value),
                RowHash = ComputeRowHash(kvp.Value),
                ValidationStatus = "PENDING",
                IsPreview = true,
                SourceFile = job.FileName
            }).ToList();

            // Bulk insert staging rows
            foreach (var batch in stagingRows.Chunk(5000))
            {
                foreach (var row in batch)
                    await _uow.ImportStaging.AddAsync(row);
                await _uow.SaveChangesAsync();
            }

            job.ProcessedRows = rows.Count;
            await _uow.SaveChangesAsync();

            _logger.LogInformation("Parsed {Count} rows for job {JobCode}", rows.Count, job.JobCode);
        }
        catch (Exception ex)
        {
            job.Status = "FAILED";
            job.ErrorSummary = ex.Message;
            await _uow.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// Validate all rows in staging.
    /// </summary>
    public async Task ValidateAllAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _uow.ImportJobs.GetByIdAsync(jobId);
        if (job == null) throw new InvalidOperationException($"Job {jobId} not found");

        var stagingRows = (await _uow.ImportStaging.FindAsync(s => s.JobId == jobId)).ToList();

        int success = 0, errors = 0, warnings = 0;
        var errorBatch = new List<ImportError>();

        foreach (var row in stagingRows)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Parse row
                var rawData = JsonSerializer.Deserialize<Dictionary<string, string?>>(row.RawData ?? "{}")
                    ?? new Dictionary<string, string?>();

                // Normalize keys (lowercase)
                var normalized = rawData.ToDictionary(
                    k => k.Key.ToLower().Replace(" ", "_"),
                    v => v.Value);

                // Validate
                var result = _validator.Validate(job.ImportType, normalized, row.RowNumber);

                // Update staging row
                row.ValidationStatus = result.IsValid ? "VALID" : "ERROR";
                row.ParsedData = JsonSerializer.Serialize(result.ParsedValues);

                if (result.Errors.Any())
                    row.ValidationErrors = JsonSerializer.Serialize(result.Errors.Select(e => new
                    {
                        e.FieldName,
                        e.FieldValue,
                        e.ErrorCode,
                        e.ErrorMessage,
                        e.SuggestedFix
                    }));

                if (result.Warnings.Any())
                    row.ValidationWarnings = JsonSerializer.Serialize(result.Warnings.Select(w => new
                    {
                        w.FieldName,
                        w.FieldValue,
                        w.WarningCode,
                        w.WarningMessage,
                        w.SuggestedFix
                    }));

                // Count
                if (result.IsValid)
                {
                    if (result.Warnings.Any()) warnings++;
                    success++;
                }
                else
                {
                    errors++;
                }

                // Create error records
                foreach (var err in result.Errors)
                {
                    errorBatch.Add(new ImportError
                    {
                        Id = Guid.NewGuid(),
                        JobId = jobId,
                        StagingId = row.Id,
                        RowNumber = row.RowNumber,
                        FieldName = err.FieldName,
                        FieldValue = err.FieldValue,
                        ErrorCode = err.ErrorCode,
                        ErrorMessage = err.ErrorMessage,
                        Severity = "ERROR",
                        SuggestedFix = err.SuggestedFix
                    });
                }

                foreach (var warn in result.Warnings)
                {
                    errorBatch.Add(new ImportError
                    {
                        Id = Guid.NewGuid(),
                        JobId = jobId,
                        StagingId = row.Id,
                        RowNumber = row.RowNumber,
                        FieldName = warn.FieldName,
                        FieldValue = warn.FieldValue,
                        ErrorCode = warn.WarningCode,
                        ErrorMessage = warn.WarningMessage,
                        Severity = "WARNING",
                        SuggestedFix = warn.SuggestedFix
                    });
                }

                await _uow.ImportStaging.UpdateAsync(row);
                job.ProcessedRows++;

                // Save in batches
                if (errorBatch.Count >= 1000)
                {
                    foreach (var err in errorBatch)
                        await _uow.ImportErrors.AddAsync(err);
                    await _uow.SaveChangesAsync();
                    errorBatch.Clear();
                }

                if (job.ProcessedRows % 5000 == 0)
                    await _uow.SaveChangesAsync();

            }
            catch (Exception ex)
            {
                row.ValidationStatus = "ERROR";
                row.ValidationErrors = JsonSerializer.Serialize(new[] { new { ErrorCode = "PARSE_ERROR", ErrorMessage = ex.Message } });
                errors++;
                await _uow.ImportStaging.UpdateAsync(row);
            }
        }

        // Save remaining errors
        foreach (var err in errorBatch)
            await _uow.ImportErrors.AddAsync(err);

        // Update job
        job.SuccessRows = success;
        job.ErrorRows = errors;
        job.WarningRows = warnings;
        job.Status = errors > 0 && job.ErrorHandling == "STOP" ? "FAILED" : "VALIDATED";
        job.CompletedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync();

        _logger.LogInformation("Validated job {JobCode}: {Success} valid, {Errors} errors, {Warnings} warnings",
            job.JobCode, success, errors, warnings);
    }

    /// <summary>
    /// Preview rows (lấy sample từ staging).
    /// </summary>
    public async Task<List<ImportStagingPreviewDto>> PreviewAsync(Guid jobId, int take = 10, CancellationToken ct = default)
    {
        var rows = (await _uow.ImportStaging.FindAsync(s => s.JobId == jobId))
            .OrderBy(s => s.RowNumber)
            .Take(take)
            .ToList();

        var errors = (await _uow.ImportErrors.FindAsync(e => e.JobId == jobId))
            // StagingId có thể NULL (lỗi không gắn được dòng staging).
            // Bỏ các dòng lỗi không gắn staging để key của dictionary luôn non-null.
            .Where(e => e.StagingId.HasValue)
            .GroupBy(e => e.StagingId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(r => new ImportStagingPreviewDto
        {
            Id = r.Id,
            RowNumber = r.RowNumber,
            Fields = JsonSerializer.Deserialize<Dictionary<string, string?>>(r.ParsedData ?? r.RawData ?? "{}")
                ?? new Dictionary<string, string?>(),
            ValidationStatus = r.ValidationStatus,
            Errors = errors.GetValueOrDefault(r.Id, new List<ImportError>())
                .Where(e => e.Severity == "ERROR")
                .Select(e => new ImportErrorDto
                {
                    Id = e.Id,
                    FieldName = e.FieldName,
                    FieldValue = e.FieldValue,
                    ErrorCode = e.ErrorCode,
                    ErrorMessage = e.ErrorMessage,
                    SuggestedFix = e.ResolvedValue
                }).ToList(),
            Warnings = errors.GetValueOrDefault(r.Id, new List<ImportError>())
                .Where(e => e.Severity == "WARNING")
                .Select(e => new ImportWarningDto
                {
                    FieldName = e.FieldName,
                    FieldValue = e.FieldValue,
                    ErrorCode = e.ErrorCode,
                    ErrorMessage = e.ErrorMessage,
                    SuggestedFix = e.ResolvedValue
                }).ToList()
        }).ToList();
    }

    /// <summary>
    /// Commit: Insert valid rows vào bảng chính.
    /// </summary>
    public async Task<int> CommitAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _uow.ImportJobs.GetByIdAsync(jobId);
        if (job == null) throw new InvalidOperationException($"Job {jobId} not found");
        if (job.Status != "VALIDATED")
            throw new InvalidOperationException($"Job status is {job.Status}, must be VALIDATED to commit");

        await _uow.BeginTransactionAsync();
        try
        {
            job.Status = "IMPORTING";
            await _uow.SaveChangesAsync();

            var stagingRows = (await _uow.ImportStaging.FindAsync(
                s => s.JobId == jobId && s.ValidationStatus == "VALID"))
                .ToList();

            int inserted = 0;
            foreach (var row in stagingRows)
            {
                ct.ThrowIfCancellationRequested();

                var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    row.ParsedData ?? "{}");

                switch (job.ImportType)
                {
                    case "CUSTOMERS":
                    case "PARTNERS":
                        var partner = MapToPartner(parsed, job);
                        await _uow.Partners.AddAsync(partner);
                        row.TargetId = partner.Id;
                        row.TargetTable = "partners";
                        inserted++;
                        break;

                    case "PRODUCTS":
                        var product = MapToProduct(parsed, job);
                        await _uow.Products.AddAsync(product);
                        row.TargetId = product.Id;
                        row.TargetTable = "products";
                        inserted++;
                        break;

                    default:
                        _logger.LogWarning("Import type {Type} not implemented yet", job.ImportType);
                        break;
                }

                row.IsPreview = false;

                if (inserted % 1000 == 0)
                    await _uow.SaveChangesAsync();
            }

            await _uow.SaveChangesAsync();

            // Mark committed
            job.IsCommitted = true;
            job.CommittedAt = DateTime.UtcNow;
            job.Status = "COMPLETED";
            job.SuccessRows = inserted;
            await _uow.SaveChangesAsync();

            await _uow.CommitTransactionAsync();

            _logger.LogInformation("Committed job {JobCode}: {Inserted} rows inserted", job.JobCode, inserted);

            return inserted;
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            job.Status = "FAILED";
            await _uow.SaveChangesAsync();
            throw;
        }
    }

    /// <summary>
    /// Rollback: Xóa các rows đã commit.
    /// </summary>
    public async Task RollbackAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _uow.ImportJobs.GetByIdAsync(jobId);
        if (job == null) throw new InvalidOperationException($"Job {jobId} not found");
        if (!job.IsCommitted)
            throw new InvalidOperationException("Job not committed, nothing to rollback");

        await _uow.BeginTransactionAsync();
        try
        {
            var staged = (await _uow.ImportStaging.FindAsync(
                s => s.JobId == jobId && !s.IsPreview && s.TargetId != null))
                .ToList();

            foreach (var row in staged)
            {
                if (row.TargetId == null) continue;

                switch (row.TargetTable)
                {
                    case "partners":
                        var p = await _uow.Partners.GetByIdAsync(row.TargetId.Value);
                        if (p != null) await _uow.Partners.DeleteAsync(p);
                        break;
                    case "products":
                        var prod = await _uow.Products.GetByIdAsync(row.TargetId.Value);
                        if (prod != null) await _uow.Products.DeleteAsync(prod);
                        break;
                }
            }

            await _uow.SaveChangesAsync();

            job.IsCommitted = false;
            job.CommittedAt = null;
            job.Status = "ROLLBACK";
            await _uow.SaveChangesAsync();

            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }

    // ============ Private helpers ============

    private async Task<Dictionary<int, Dictionary<string, string?>>> ParseCsvAsync(string path, CancellationToken ct)
    {
        var result = new Dictionary<int, Dictionary<string, string?>>();
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ",",
            BadDataFound = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim
        };

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, config);

        await csv.ReadAsync();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? Array.Empty<string>();

        int rowNum = 1;
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            var row = new Dictionary<string, string?>();
            for (int i = 0; i < headers.Length; i++)
            {
                try { row[headers[i]] = csv.GetField(i); }
                catch { row[headers[i]] = null; }
            }
            result[rowNum++] = row;
        }

        return result;
    }

    private Task<Dictionary<int, Dictionary<string, string?>>> ParseExcelAsync(string path, CancellationToken ct)
    {
        var result = new Dictionary<int, Dictionary<string, string?>>();
        using var workbook = new XLWorkbook(path);
        var ws = workbook.Worksheet(1);

        var rows = ws.RangeUsed()?.RowsUsed().Skip(1).ToList() ?? new List<IXLRangeRow>();
        if (!rows.Any())
        {
            // Try reading first sheet
            ws = workbook.Worksheets.First();
            rows = ws.RangeUsed()?.RowsUsed().Skip(1).ToList() ?? new List<IXLRangeRow>();
        }

        var headerRow = ws.Row(1);
        var headers = new List<string>();
        foreach (var cell in headerRow.CellsUsed())
            headers.Add(cell.GetString());

        int rowNum = 1;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var data = new Dictionary<string, string?>();
            int col = 0;
            foreach (var cell in row.CellsUsed())
            {
                if (col < headers.Count)
                    data[headers[col]] = cell.GetString();
                col++;
            }
            result[rowNum++] = data;
        }

        return Task.FromResult(result);
    }

    private static async Task<string> ComputeFileHashAsync(string path)
    {
        using var sha256 = SHA256.Create();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        var hash = await sha256.ComputeHashAsync(fs);
        return Convert.ToHexString(hash);
    }

    private static string ComputeRowHash(Dictionary<string, string?> row)
    {
        var json = JsonSerializer.Serialize(row.OrderBy(k => k.Key));
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private Partner MapToPartner(Dictionary<string, object>? parsed, ImportJob job)
    {
        var p = new Partner();
        if (parsed == null) return p;

        p.Id = Guid.NewGuid();

        if (TryGet(parsed, "partner_code", out string pc)) p.PartnerCode = pc;
        else if (TryGet(parsed, "code", out pc)) p.PartnerCode = pc;
        else p.PartnerCode = $"IMP-{job.JobCode}";

        if (TryGet(parsed, "partner_name", out string pn)) p.PartnerName = pn;
        else if (TryGet(parsed, "name", out pn)) p.PartnerName = pn;

        if (TryGet(parsed, "partner_type", out string pt)) p.PartnerType = pt;
        else p.PartnerType = "STORE";

        if (TryGet(parsed, "phone", out string ph)) p.Phone = ph;
        if (TryGet(parsed, "email", out string em)) p.Email = em;
        if (TryGet(parsed, "address", out string ad)) p.Address = ad;
        if (TryGet(parsed, "route_code", out string rc)) p.RouteCode = rc;
        if (TryGet(parsed, "district", out string dist)) p.District = dist;
        if (TryGet(parsed, "province", out string prov)) p.Province = prov;

        if (TryGetDecimal(parsed, "credit_limit", out decimal cl)) p.CreditLimit = cl;

        p.IsActive = true;

        return p;
    }

    private Product MapToProduct(Dictionary<string, object>? parsed, ImportJob job)
    {
        var p = new Product();
        if (parsed == null) return p;

        p.Id = Guid.NewGuid();

        if (TryGet(parsed, "sku", out string sku)) p.Sku = sku;
        else p.Sku = $"IMP-{job.JobCode}";

        if (TryGet(parsed, "product_name", out string pn)) p.ProductName = pn;
        else if (TryGet(parsed, "name", out pn)) p.ProductName = pn;

        if (TryGet(parsed, "short_name", out string sn)) p.ShortName = sn;
        if (TryGet(parsed, "brand", out string br)) p.Brand = br;

        if (TryGetDecimal(parsed, "selling_price", out decimal sp)) p.BasePrice = sp;
        if (TryGetDecimal(parsed, "vat_rate", out decimal vr)) p.VatRate = vr;
        else p.VatRate = 8.0m;

        if (TryGetDecimal(parsed, "weight_per_unit", out decimal wp)) p.WeightGram = wp;
        if (TryGetDecimal(parsed, "pack_size_ml", out decimal ps)) p.VolumeMl = ps;

        p.BaseUom = "CAI";
        p.IsActive = true;

        return p;
    }

    private static bool TryGet(Dictionary<string, object>? dict, string key, out string value)
    {
        value = string.Empty;
        if (dict == null || !dict.ContainsKey(key)) return false;
        value = dict[key]?.ToString() ?? "";
        return true;
    }

    private static bool TryGetDecimal(Dictionary<string, object>? dict, string key, out decimal value)
    {
        value = 0;
        if (dict == null || !dict.ContainsKey(key)) return false;
        if (decimal.TryParse(dict[key]?.ToString(), out value)) return true;
        return false;
    }
}
