using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Bảng giá bán.
/// </summary>
[Table("price_lists", Schema = "tenant")]
public class PriceList : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string PriceListCode { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string PriceListName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string ApplyTo { get; set; } = "ALL"; // ALL, SEGMENT, ROUTE, PARTNER

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public Guid? ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    public decimal UnitPrice { get; set; } = 0;

    public decimal? MinQty { get; set; }

    public decimal? MaxQty { get; set; }

    public int Priority { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime SysStartTime { get; set; }
    public DateTime SysEndTime { get; set; }
}
