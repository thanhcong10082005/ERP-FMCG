using ERP.FMC.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Sổ cái tồn kho (movements).
///
/// Lưu ý: bảng <c>tenant.stock_ledger</c> lưu số lượng theo 2 cột
/// <c>quantity_in</c> / <c>quantity_out</c> (chứ không phải 1 cột <c>quantity</c>),
/// nên entity có thêm 2 property và helper <see cref="Quantity"/>.
/// </summary>
[Table("stock_ledger", Schema = "tenant")]
public class StockLedger
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WarehouseId { get; set; }

    [ForeignKey(nameof(WarehouseId))]
    public Warehouse? Warehouse { get; set; }

    public Guid ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    public Guid? PartnerId { get; set; }

    /// <summary>Loại giao dịch: IN, OUT, TRANSFER, ADJUST.</summary>
    [Required, MaxLength(20)]
    public string TransactionType { get; set; } = "IN";

    [Required, MaxLength(20)]
    public string UomCode { get; set; } = "CAI";

    public decimal QuantityIn { get; set; } = 0;

    public decimal QuantityOut { get; set; } = 0;

    public decimal? UnitCost { get; set; } = 0;

    [MaxLength(50)]
    public string? ReferenceType { get; set; } // SO, PO, ADJ, TR

    public Guid? ReferenceId { get; set; }

    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    public DateTime TransactionTime { get; set; } = DateTime.UtcNow;

    [MaxLength(255)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid? CreatedBy { get; set; }

    // ===== Helper không lưu xuống DB =====

    /// <summary>Số lượng có dấu: dương khi nhập, âm khi xuất.</summary>
    [NotMapped]
    public decimal Quantity => QuantityIn - QuantityOut;

    /// <summary>Loại movement suy ra từ <see cref="TransactionType"/>.</summary>
    [NotMapped]
    public MovementType MovementType
    {
        get => TransactionType?.ToUpperInvariant() switch
        {
            "OUT" or "SALE" or "ISSUE" => MovementType.Out,
            "TRANSFER" or "TR" => MovementType.Transfer,
            "ADJUST" or "ADJUSTMENT" or "ADJ" => MovementType.Adjustment,
            _ => MovementType.In
        };
        set => TransactionType = value switch
        {
            MovementType.Out => "OUT",
            MovementType.Transfer => "TRANSFER",
            MovementType.Adjustment => "ADJUST",
            _ => "IN"
        };
    }

    /// <summary>Tổng giá trị = số lượng × đơn giá (không có cột trong DB).</summary>
    [NotMapped]
    public decimal TotalCost => Quantity * (UnitCost ?? 0);

    /// <summary>Số chứng từ tham chiếu (không có cột riêng, dùng ReferenceId).</summary>
    [NotMapped]
    public string? ReferenceNumber => ReferenceId?.ToString();
}
