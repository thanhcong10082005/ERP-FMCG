using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

// ===================================================================
// BẢNG ĐỐI SOÁT CUỐI NGÀY (EOD)
// -------------------------------------------------------------------
// Nguồn: migration Postgres `v13_3__eod_reconciliation.sql` (hệ thống
// NestJS đã có, MS SQL chưa). Driver App đã có màn hình `EODScreen`
// nhưng chưa có API đối soát thật.
//
// Nghiệp vụ:
//   Cuối ca, tài xế nộp tiền mặt. Dispatcher nhập `actual_cash`.
//   Server tự tính `expected_cash` = SUM(new_order_cash + old_debt_cash)
//   của các POD trong chuyến, rồi so sánh.
// ===================================================================

/// <summary>Phiếu đối soát cuối ngày (1 chuyến = 1 phiếu).</summary>
[Table("eod_reconciliations", Schema = "tenant")]
public class EodReconciliation : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? TripId { get; set; }

    [ForeignKey(nameof(TripId))]
    public DeliveryTrip? Trip { get; set; }

    public DateTime ReconciliationDate { get; set; } = DateTime.Today;

    public Guid? DriverId { get; set; }

    [ForeignKey(nameof(DriverId))]
    public UserAccount? Driver { get; set; }

    [MaxLength(200)]
    public string? DriverName { get; set; }

    public Guid? WarehouseId { get; set; }

    [ForeignKey(nameof(WarehouseId))]
    public Warehouse? Warehouse { get; set; }

    /// <summary>Tiền mặt kỳ vọng = SUM(new_order_cash + old_debt_cash) của POD trong chuyến.</summary>
    public decimal ExpectedCash { get; set; }

    /// <summary>Tiền mặt thực tế tài xế nộp (dispatcher nhập).</summary>
    public decimal ActualCash { get; set; }

    /// <summary>actual_cash − expected_cash (âm = thiếu).</summary>
    public decimal CashDifference { get; set; }

    [MaxLength(500)]
    public string? CashVarianceReason { get; set; }

    /// <summary>DRIVER_REIMBURSE | SUSPEND_DEBT | WRITE_OFF.</summary>
    [MaxLength(50)]
    public string? CashVarianceAction { get; set; }

    public int ExpectedItems { get; set; }

    public int DeliveredItems { get; set; }

    public int ReturnedItems { get; set; }

    public int ItemsDifference { get; set; }

    [MaxLength(500)]
    public string? ItemsVarianceReason { get; set; }

    /// <summary>DRIVER_COMPENSATE | RETURN_TO_STOCK | WRITE_OFF.</summary>
    [MaxLength(50)]
    public string? ItemsVarianceAction { get; set; }

    /// <summary>PENDING | APPROVED | DISPUTED | CANCELLED.</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "PENDING";

    public int TotalOrders { get; set; }

    public int SuccessfulOrders { get; set; }

    public int FailedOrders { get; set; }

    public decimal TripExpenses { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public ICollection<EodReconciliationItem> Items { get; set; } = new List<EodReconciliationItem>();
}

/// <summary>Dòng chi tiết đối soát (theo từng đơn hàng).</summary>
[Table("eod_reconciliation_items", Schema = "tenant")]
public class EodReconciliationItem : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReconciliationId { get; set; }

    [ForeignKey(nameof(ReconciliationId))]
    public EodReconciliation? Reconciliation { get; set; }

    public Guid? SalesOrderId { get; set; }

    [ForeignKey(nameof(SalesOrderId))]
    public SalesOrder? SalesOrder { get; set; }

    public Guid? PartnerId { get; set; }

    [ForeignKey(nameof(PartnerId))]
    public Partner? Partner { get; set; }

    [MaxLength(200)]
    public string? PartnerName { get; set; }

    public decimal ExpectedCash { get; set; }

    public decimal CollectedCash { get; set; }

    public decimal OldDebtCollected { get; set; }

    public decimal CashDifference { get; set; }

    [MaxLength(500)]
    public string? CashVarianceReason { get; set; }

    public decimal ExpectedQty { get; set; }

    public decimal DeliveredQty { get; set; }

    public decimal ReturnedQty { get; set; }

    public decimal ItemsDifference { get; set; }

    /// <summary>MATCHED | CASH_SHORT | CASH_OVER | ITEMS_SHORT | FULL_MATCH.</summary>
    [Required, MaxLength(20)]
    public string ReconciliationStatus { get; set; } = "MATCHED";
}
