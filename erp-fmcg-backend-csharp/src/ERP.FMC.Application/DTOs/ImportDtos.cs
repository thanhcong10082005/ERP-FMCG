namespace ERP.FMC.Application.DTOs;

// ============ Import Job DTOs ============
public class ImportJobDto
{
    public Guid Id { get; set; }
    public string JobCode { get; set; } = string.Empty;
    public string ImportType { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public int SuccessRows { get; set; }
    public int ErrorRows { get; set; }
    public int WarningRows { get; set; }
    public string ErrorHandling { get; set; } = "SKIP";
    public DateTime UploadedAt { get; set; }
    public bool IsCommitted { get; set; }
    public DateTime? CommittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorSummary { get; set; }
}

public class ImportJobSummaryDto
{
    public Guid Id { get; set; }
    public string JobCode { get; set; } = string.Empty;
    public string ImportType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int ErrorRows { get; set; }
    public double Progress => TotalRows > 0 ? (double)SuccessRows / TotalRows * 100 : 0;
    public DateTime UploadedAt { get; set; }
}

public class CreateImportJobDto
{
    public string ImportType { get; set; } = string.Empty;
    public string ErrorHandling { get; set; } = "SKIP";
}

public class ImportJobFilterDto
{
    public string? Status { get; set; }
    public string? ImportType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// ============ Staging Row DTOs ============
public class ImportStagingRowDto
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public int RowNumber { get; set; }
    public string? RawData { get; set; }
    public string? ParsedData { get; set; }
    public string ValidationStatus { get; set; } = "PENDING";
    public string? ValidationErrors { get; set; }
    public string? ValidationWarnings { get; set; }
    public Guid? TargetId { get; set; }
    public string? TargetTable { get; set; }
    public bool IsPreview { get; set; }
}

public class ImportStagingPreviewDto
{
    public Guid Id { get; set; }
    public int RowNumber { get; set; }
    public Dictionary<string, string?> Fields { get; set; } = new();
    public string ValidationStatus { get; set; } = "PENDING";
    public List<ImportErrorDto> Errors { get; set; } = new();
    public List<ImportWarningDto> Warnings { get; set; } = new();
}

// ============ Error DTOs ============
public class ImportErrorDto
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid? StagingId { get; set; }
    public int RowNumber { get; set; }
    public string? FieldName { get; set; }
    public string? FieldValue { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string Severity { get; set; } = "ERROR";
    public string? SuggestedFix { get; set; }
    public string? ResolvedValue { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class ImportWarningDto
{
    public string? FieldName { get; set; }
    public string? FieldValue { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string? SuggestedFix { get; set; }
}

// ============ Error Resolution DTOs ============
public class ErrorResolutionDto
{
    public Guid ErrorId { get; set; }
    public string ResolvedValue { get; set; } = string.Empty;
    public Guid? ResolvedBy { get; set; }
}

// ============ Error Summary ============
public class ImportErrorSummaryDto
{
    public Guid JobId { get; set; }
    public string JobCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public ImportResultSummary Summary { get; set; } = new();
    public List<ImportErrorDto> Errors { get; set; } = new();
    public Dictionary<string, int> ErrorCountByField { get; set; } = new();
    public Dictionary<string, int> ErrorCountByCode { get; set; } = new();
}

public class ImportResultSummary
{
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int ErrorRows { get; set; }
    public int WarningRows { get; set; }
    public int ResolvedRows { get; set; }
    public double SuccessRate => TotalRows > 0 ? (double)SuccessRows / TotalRows * 100 : 0;
}

// ============ Validation Result ============
public class ValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<ValidationError> Errors { get; set; } = new();
    public List<ValidationWarning> Warnings { get; set; } = new();
    public Dictionary<string, object?> ParsedValues { get; set; } = new();
}

public class ValidationError
{
    public string FieldName { get; set; } = string.Empty;
    public string? FieldValue { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string? SuggestedFix { get; set; }
}

public class ValidationWarning
{
    public string FieldName { get; set; } = string.Empty;
    public string? FieldValue { get; set; }
    public string WarningCode { get; set; } = string.Empty;
    public string WarningMessage { get; set; } = string.Empty;
    public string? SuggestedFix { get; set; }
}
