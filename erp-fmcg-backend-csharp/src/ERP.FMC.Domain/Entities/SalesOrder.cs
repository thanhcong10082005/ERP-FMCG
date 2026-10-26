using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

/// <summary>
/// Đơn hàng bán.
/// </summary>
[Table("sales_orders", Schema = "tenant")]
public class SalesOrder : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string OrderNumber { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? ExternalRef { get; set; }

    public Guid CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public Partner? Customer { get; set; }

    public Guid WarehouseId { get; set; }

    [ForeignKey(nameof(WarehouseId))]
    public Warehouse? Warehouse { get; set; }

    public Guid? SalePersonId { get; set; }

    [ForeignKey(nameof(SalePersonId))]
    public UserAccount? SalePerson { get; set; }

    public DateTime OrderDate { get; set; }

    public DateTime? DeliveryDate { get; set; }

    public DateTime? ExpectedDelivery { get; set; }

    [MaxLength(500)]
    public string? DeliveryAddress { get; set; }

    [MaxLength(20)]
    public string? ContactPhone { get; set; }

    public decimal Subtotal { get; set; } = 0;

    public decimal DiscountAmount { get; set; } = 0;

    public decimal TaxAmount { get; set; } = 0;

    public decimal TotalAmount { get; set; } = 0;

    public decimal PaidAmount { get; set; } = 0;

    [MaxLength(20)]
    public string Status { get; set; } = "CONFIRMED";

    [MaxLength(20)]
    public string PaymentStatus { get; set; } = "UNPAID";

    [MaxLength(50)]
    public string? InvoiceNumber { get; set; }

    public DateTime? InvoiceDate { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime SysStartTime { get; set; }
    public DateTime SysEndTime { get; set; }

    public ICollection<SalesOrderItem> Items { get; set; } = new List<SalesOrderItem>();
}
