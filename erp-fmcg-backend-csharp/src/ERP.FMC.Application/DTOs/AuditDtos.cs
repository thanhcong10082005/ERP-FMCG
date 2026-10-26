namespace ERP.FMC.Application.DTOs;

// ============ Audit Log DTOs ============
public class AuditLogDto
{
    public Guid Id { get; set; }
    public string TableName { get; set; } = string.Empty;
    public Guid RecordId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Operation { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? ChangedFields { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? Notes { get; set; }
}

public class AuditLogFilterDto
{
    public string? TableName { get; set; }
    public Guid? RecordId { get; set; }
    public string? Action { get; set; }
    public Guid? UserId { get; set; }
    public string? Source { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

// ============ Temporal History DTOs ============
public class HistoryRecordDto
{
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool IsCurrent { get; set; }
    public Dictionary<string, object?> Values { get; set; } = new();
    public string ChangedBy { get; set; } = string.Empty;
    public string? ChangeReason { get; set; }
}

public class HistoryComparisonDto
{
    public Guid RecordId { get; set; }
    public DateTime FromTime { get; set; }
    public DateTime ToTime { get; set; }
    public Dictionary<string, FieldChange> Changes { get; set; } = new();
    public int TotalChanges => Changes.Count;
}

public class FieldChange
{
    public string FieldName { get; set; } = string.Empty;
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
    public bool Changed => !Equals(OldValue, NewValue);
    public string ChangeType { get; set; } = "UPDATED"; // ADDED, REMOVED, UPDATED
}

public class TemporalEntityHistoryDto
{
    public Guid RecordId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public int TotalVersions { get; set; }
    public DateTime FirstVersionAt { get; set; }
    public DateTime LastVersionAt { get; set; }
    public List<HistoryRecordDto> History { get; set; } = new();
}

// ============ Import History DTOs ============
public class ImportHistoryDto
{
    public Guid Id { get; set; }
    public string JobCode { get; set; } = string.Empty;
    public string ImportType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int ErrorRows { get; set; }
    public int WarningRows { get; set; }
    public double SuccessRate => TotalRows > 0 ? (double)SuccessRows / TotalRows * 100 : 0;
    public TimeSpan? Duration => CompletedAt.HasValue && UploadedAt != default
        ? CompletedAt - UploadedAt : null;
    public DateTime UploadedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsCommitted { get; set; }
    public Guid? UploadedBy { get; set; }
}

public class ReImportRequestDto
{
    public Guid OriginalJobId { get; set; }
    public bool KeepResolvedErrors { get; set; } = true;
    public bool SkipValidation { get; set; } = false;
    public Dictionary<int, Dictionary<string, string?>> RowOverrides { get; set; } = new();
}

// ============ Batch Resolve DTOs ============
public class BatchResolveRequestDto
{
    public List<BatchErrorResolution> Resolutions { get; set; } = new();
    public bool AutoTrimWhitespace { get; set; }
    public bool AutoFixPhoneFormat { get; set; }
    public bool AutoFixEmailCase { get; set; }
}

public class BatchErrorResolution
{
    public Guid ErrorId { get; set; }
    public string ResolvedValue { get; set; } = string.Empty;
}

public class BatchResolveResultDto
{
    public int TotalRequested { get; set; }
    public int Successful { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}

// ============ Undo DTOs ============
public class UndoRequestDto
{
    public Guid JobId { get; set; }
    public DateTime? AsOfTime { get; set; }  // null = full rollback
    public bool KeepAuditLog { get; set; } = true;
}

public class UndoResultDto
{
    public Guid JobId { get; set; }
    public int RowsDeleted { get; set; }
    public int RowsReverted { get; set; }
    public TimeSpan Duration { get; set; }
    public string Status { get; set; } = string.Empty;
}
