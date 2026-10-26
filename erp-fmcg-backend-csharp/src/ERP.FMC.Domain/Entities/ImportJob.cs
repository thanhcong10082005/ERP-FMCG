using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Job import dữ liệu (cho staging pipeline).
/// </summary>
[Table("import_jobs", Schema = "tenant")]
public class ImportJob : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string JobCode { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string ImportType { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Status { get; set; } = "PENDING";

    [MaxLength(255)]
    public string? FileName { get; set; }

    [MaxLength(500)]
    public string? FilePath { get; set; }

    public long FileSize { get; set; } = 0;

    [MaxLength(64)]
    public string? FileHash { get; set; }

    public int TotalRows { get; set; } = 0;

    public int ProcessedRows { get; set; } = 0;

    public int SuccessRows { get; set; } = 0;

    public int ErrorRows { get; set; } = 0;

    public int WarningRows { get; set; } = 0;

    [MaxLength(20)]
    public string ErrorHandling { get; set; } = "SKIP";

    public Guid? UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public bool IsCommitted { get; set; } = false;

    public DateTime? CommittedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    [MaxLength(1000)]
    public string? ErrorSummary { get; set; }

    public ICollection<ImportStaging> StagingRows { get; set; } = new List<ImportStaging>();
}
