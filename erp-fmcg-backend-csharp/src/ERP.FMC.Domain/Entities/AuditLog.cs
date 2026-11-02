using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Audit log - lưu lại tất cả thay đổi dữ liệu.
/// </summary>
[Table("audit_logs", Schema = "tenant")]
public class AuditLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string TableName { get; set; } = string.Empty;

    public Guid RecordId { get; set; }

    [Required, MaxLength(20)]
    public string Action { get; set; } = string.Empty; // INSERT, UPDATE, DELETE, RESTORE

    /// <summary>
    /// Mô tả thao tác, ví dụ "CREATE_ORDER" hoặc "PUT /api/sales/trips/{guid}/start".
    /// ★ Độ dài phải khớp cột [operation] trong DB (NVARCHAR(500)).
    ///   Trước đây là 50 ⇒ path có GUID dài bị SQL Server cắt báo lỗi 2628
    ///   ⇒ mất toàn bộ audit log cho request đó.
    /// </summary>
    [MaxLength(500)]
    public string? Operation { get; set; }

    public string? OldValues { get; set; }     // JSON
    public string? NewValues { get; set; }     // JSON
    public string? ChangedFields { get; set; } // JSON array

    public Guid? UserId { get; set; }

    [MaxLength(255)]
    public string? UserName { get; set; }

    [MaxLength(50)]
    public string? Source { get; set; } // API, IMPORT, BATCH, SYSTEM

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    [MaxLength(100)]
    public string? RequestId { get; set; }

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
