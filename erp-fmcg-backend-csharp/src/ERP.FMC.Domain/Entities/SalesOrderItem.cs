using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Chi tiết đơn hàng bán.
/// </summary>
[Table("sales_order_items", Schema = "tenant")]
public class SalesOrderItem : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SalesOrderId { get; set; }

    [ForeignKey(nameof(SalesOrderId))]
    public SalesOrder? SalesOrder { get; set; }

    public int LineNumber { get; set; } = 1;

    public Guid ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    [Required, MaxLength(50)]
    public string ProductSku { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string UomCode { get; set; } = "CAI";

    public decimal OrderedQty { get; set; } = 0;

    public decimal BaseQty { get; set; } = 0;

    public decimal UnitPrice { get; set; } = 0;

    public decimal DiscountPercent { get; set; } = 0;

    public decimal DiscountAmount { get; set; } = 0;

    public decimal LineSubtotal { get; set; } = 0;

    public decimal LineTotal { get; set; } = 0;

    public decimal DeliveredQty { get; set; } = 0;

    public decimal ReturnedQty { get; set; } = 0;

    [MaxLength(500)]
    public string? Notes { get; set; }
}
