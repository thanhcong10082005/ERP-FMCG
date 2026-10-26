using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Dòng dữ liệu tạm trong staging.
/// </summary>
[Table("import_staging", Schema = "tenant")]
public class ImportStaging
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid JobId { get; set; }

    [ForeignKey(nameof(JobId))]
    public ImportJob? Job { get; set; }

    public int RowNumber { get; set; }

    [MaxLength(64)]
    public string? RowHash { get; set; }

    public string? RawData { get; set; }
    public string? ParsedData { get; set; }

    [MaxLength(20)]
    public string ValidationStatus { get; set; } = "PENDING";

    public string? ValidationErrors { get; set; }
    public string? ValidationWarnings { get; set; }

    public Guid? TargetId { get; set; }

    [MaxLength(50)]
    public string? TargetTable { get; set; }

    public bool IsPreview { get; set; } = true;

    [MaxLength(255)]
    public string? SourceFile { get; set; }

    [MaxLength(100)]
    public string? SourceSheet { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ImportError> Errors { get; set; } = new List<ImportError>();
}

/// <summary>
/// Lỗi validation cho từng dòng.
/// </summary>
[Table("import_errors", Schema = "tenant")]
public class ImportError
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid JobId { get; set; }

    public Guid? StagingId { get; set; }

    [ForeignKey(nameof(StagingId))]
    public ImportStaging? Staging { get; set; }

    public int RowNumber { get; set; }

    [MaxLength(100)]
    public string? FieldName { get; set; }

    public string? FieldValue { get; set; }

    [Required, MaxLength(50)]
    public string ErrorCode { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string ErrorMessage { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Severity { get; set; } = "ERROR";

    [MaxLength(500)]
    public string? SuggestedFix { get; set; }

    public string? ResolvedValue { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool IsResolved { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Mapping từ cột Excel → DB field.
/// </summary>
[Table("import_field_mappings", Schema = "tenant")]
public class ImportFieldMapping
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string ImportType { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ExcelColumn { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string DbField { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? DataType { get; set; }

    public bool IsRequired { get; set; } = false;

    [MaxLength(255)]
    public string? DefaultValue { get; set; }

    public string? ValidationRule { get; set; }

    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
