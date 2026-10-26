using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Danh mục sản phẩm (phân cấp).
/// </summary>
[Table("categories", Schema = "tenant")]
public class Category : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string CategoryCode { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string CategoryName { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    [ForeignKey(nameof(ParentId))]
    public Category? Parent { get; set; }

    public int SortOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;
}
