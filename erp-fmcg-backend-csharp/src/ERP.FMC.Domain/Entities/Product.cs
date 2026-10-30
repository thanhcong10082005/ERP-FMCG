using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Sản phẩm.
/// </summary>
[Table("products", Schema = "tenant")]
public class Product : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string Sku { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string ProductName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? ShortName { get; set; }

    [MaxLength(100)]
    public string? Brand { get; set; }

    public Guid? CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category? Category { get; set; }

    [MaxLength(20)]
    public string BaseUom { get; set; } = "CAI";

    public decimal BasePrice { get; set; } = 0;

    public decimal VatRate { get; set; } = 8.0m;

    [MaxLength(50)]
    public string? Barcode { get; set; }

    public decimal? WeightGram { get; set; } = 0;

    public decimal? VolumeMl { get; set; } = 0;

    public decimal MinStock { get; set; } = 0;

    public decimal MaxStock { get; set; } = 0;

    /// <summary>ngưỡng cảnh báo tồn kho thấp.</summary>
    public decimal ReorderPoint { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime SysStartTime { get; set; }
    public DateTime SysEndTime { get; set; }
}
