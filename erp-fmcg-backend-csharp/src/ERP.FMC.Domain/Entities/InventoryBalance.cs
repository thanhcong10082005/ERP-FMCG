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

    /// <summary>
    /// Đơn vị tính của số dư tồn.
    /// ★ BỔ SUNG: entity này THIẾU property này trong khi
    ///   bảng `inventory_balances` CÓ cột `uom_code` (NOT NULL) và có
    ///   UNIQUE key (warehouse_id, product_id, uom_code).
    ///   Hậu quả: EF đọc/ghi sai cột ⇒ không thể phân biệt tồn theo
    ///   đơn vị tính (CAI / RCS / PC) ⇒ tính "số lượng xuất tối đa" sai
    ///   ⇒ sinh tồn âm.
    /// </summary>
    [Required, MaxLength(20)]
    public string UomCode { get; set; } = "CAI";

    public decimal OnHand { get; set; } = 0;

    public decimal Reserved { get; set; } = 0;

    public decimal? Available { get; set; }

    /// <summary>
    /// COMPUTED PERSISTED trong SQL Server
    ///   (quantity_on_hand * average_cost) -> chỉ đọc, không ghi.
    /// </summary>
    public decimal TotalValue { get; set; }

    public decimal LastCost { get; set; } = 0;

    public DateTime? LastMovementAt { get; set; }
}
