using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Số dư tồn kho theo kho + sản phẩm.
/// </summary>
[Table("inventory_balances", Schema = "tenant")]
public class InventoryBalance : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WarehouseId { get; set; }

    [ForeignKey(nameof(WarehouseId))]
    public Warehouse? Warehouse { get; set; }

    public Guid ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    public decimal OnHand { get; set; } = 0;

    public decimal Reserved { get; set; } = 0;

    public decimal? Available { get; set; }

    public decimal LastCost { get; set; } = 0;

    public DateTime? LastMovementAt { get; set; }
}
