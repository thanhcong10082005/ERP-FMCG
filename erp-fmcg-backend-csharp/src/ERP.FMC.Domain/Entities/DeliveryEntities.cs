using ERP.FMC.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.FMC.Domain.Entities;

// ===================================================================
// 5 ENTITY NGHIỆP VỤ GIAO HÀNG
// -------------------------------------------------------------------
// Ánh xạ 1-1 với DDL trong Pack/erp-fmcg-database/database/01_schema/
//   07_delivery_tables.sql
//
// Quyết định kiến trúc:
//   D1 - ID giữ là UNIQUEIDENTIFIER (Guid). Driver App đổi number -> string.
//   D5 - DeliveryPod.ClientPodId là UNIQUE (idempotency key) chống double-POD.
//   D6 - Media lưu file_path (không lưu base64 trong DB).
// ===================================================================

/// <summary>
/// Chuyến giao hàng.
/// </summary>
[Table("delivery_trips", Schema = "tenant")]
public class DeliveryTrip : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(30)]
    public string TripNumber { get; set; } = string.Empty;

    public DateTime TripDate { get; set; } = DateTime.Today;

    public Guid WarehouseId { get; set; }

    [ForeignKey(nameof(WarehouseId))]
    public Warehouse? Warehouse { get; set; }

    public Guid? DriverId { get; set; }

    [ForeignKey(nameof(DriverId))]
    public UserAccount? Driver { get; set; }

    [MaxLength(20)]
    public string? VehiclePlate { get; set; }

    /// <summary>
    /// PREPARING | LOADING | DELIVERING | COMPLETED | PARTIAL_DELIVERED | CANCELLED
    /// ★ 'PARTIAL_DELIVERED' là trạng thái BẮT BUỘC: Driver App completeTrip()
    ///   set giá trị này, schema Postgres cũ thiếu -> insert bị reject.
    /// </summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "PREPARING";

    public int TotalOrders { get; set; }

    public decimal TotalAmount { get; set; }

    public int DeliveredCount { get; set; }

    public int FailedCount { get; set; }

    public decimal TotalCash { get; set; }

    public decimal TotalTransfer { get; set; }

    public decimal TotalOldDebt { get; set; }

    /// <summary>
    /// ★ DB-C7: tổng công nợ hiện tại của các khách trong chuyến.
    ///   NestJS getTripPartnerOldDebts() trả trường này cho dispatcher.
    /// </summary>
    public decimal TotalCurrentDebt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    // Temporal Table columns
    public DateTime SysStartTime { get; set; }
    public DateTime SysEndTime { get; set; }

    public ICollection<DeliveryTripOrder> Orders { get; set; } = new List<DeliveryTripOrder>();
}

/// <summary>
/// Đơn hàng thuộc 1 chuyến giao (1 điểm giao).
/// </summary>
[Table("delivery_trip_orders", Schema = "tenant")]
public class DeliveryTripOrder : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TripId { get; set; }

    [ForeignKey(nameof(TripId))]
    public DeliveryTrip? Trip { get; set; }

    public Guid SalesOrderId { get; set; }

    [ForeignKey(nameof(SalesOrderId))]
    public SalesOrder? SalesOrder { get; set; }

    public int StopOrder { get; set; } = 1;

    /// <summary>PENDING | DELIVERED | PARTIAL | FAILED | CANCELLED</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "PENDING";

    /// <summary>
    /// ★ DB-C11: lý do giao hàng thất bại.
    ///   Driver App POST lên POD với 5 giá trị:
    ///   DOOR_LOCKED | WRONG_ORDER | CUSTOMER_ABSENT | TRAFFIC | OTHER
    /// </summary>
    [MaxLength(255)]
    public string? FailureReason { get; set; }

    public int FailureCount { get; set; }

    /// <summary>Snapshot công nợ cũ lúc gán vào chuyến (D8).</summary>
    public decimal OldDebtAmount { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public ICollection<DeliveryPod> Pods { get; set; } = new List<DeliveryPod>();
}

/// <summary>
/// Phiếu giao hàng (Proof of Delivery).
/// </summary>
[Table("delivery_pod", Schema = "tenant")]
public class DeliveryPod : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// ★ D5 — IDEMPOTENCY KEY.
    ///   UUID v4 do CLIENT sinh, gửi kèm mỗi lần POST /api/sales/pod.
    ///   UNIQUE index UQ_delivery_pod_client bảo đảm server chỉ tạo
    ///   1 POD duy nhất dù client gửi lại nhiều lần (retry / offline queue).
    ///
    ///   Bằng chứng lỗi double-POD trên production Supabase:
    ///   Supabase_export/delivery_pod_rows.csv — 6 nhóm POD trùng,
    ///   cách nhau 7 giây đến 1 ngày.
    /// </summary>
    public Guid ClientPodId { get; set; }

    public Guid TripOrderId { get; set; }

    [ForeignKey(nameof(TripOrderId))]
    public DeliveryTripOrder? TripOrder { get; set; }

    public Guid SalesOrderId { get; set; }

    [ForeignKey(nameof(SalesOrderId))]
    public SalesOrder? SalesOrder { get; set; }

    public Guid PartnerId { get; set; }

    [ForeignKey(nameof(PartnerId))]
    public Partner? Partner { get; set; }

    public DateTime? ArrivedAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    /// <summary>DELIVERED | PARTIAL | FAILED</summary>
    [Required, MaxLength(20)]
    public string DeliveryStatus { get; set; } = "DELIVERED";

    // ---- Ma trận thanh toán 6 ô ----
    // Nhóm "đơn mới": tiền mặt / chuyển khoản / ghi nợ đơn mới
    public decimal NewOrderCash { get; set; }

    public decimal NewOrderTransfer { get; set; }

    public decimal NewOrderCredit { get; set; }

    // Nhóm "công nợ cũ": tiền mặt / chuyển khoản / ghi nợ công nợ cũ
    public decimal OldDebtCash { get; set; }

    public decimal OldDebtTransfer { get; set; }

    public decimal OldDebtCredit { get; set; }

    public decimal TotalCollected { get; set; }

    [MaxLength(255)]
    public string? RecipientName { get; set; }

    /// <summary>
    /// ★ D6: chỉ lưu ĐƯỜNG DẪN file, KHÔNG lưu base64.
    ///   base64 2-5MB làm nghẽn DB và buffer RAM.
    /// </summary>
    [MaxLength(500)]
    public string? SignaturePath { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>★ DB-C4: lý do thất bại do Driver App gửi kèm.</summary>
    [MaxLength(255)]
    public string? FailureReason { get; set; }

    // ---- GPS kiểm toán (chuyển từ Postgres v10_4__delivery_pod_gps.sql) ----
    public decimal? DeliveryLatitude { get; set; }

    public decimal? DeliveryLongitude { get; set; }

    public decimal? DeliveryGpsAccuracyM { get; set; }

    public int? DistanceToPartnerM { get; set; }

    public DateTime? GpsCapturedAt { get; set; }

    // ---- Kết quả kiểm tra chống gian lận (server-side) ----
    [MaxLength(20)]
    public string? GpsGateResult { get; set; }   // PASS | FAIL | SKIPPED_NO_PARTNER_GPS

    public int? GpsGateDistanceM { get; set; }

    [MaxLength(20)]
    public string? TimeGuardResult { get; set; }  // PASS | FAIL

    public ICollection<PodItem> Items { get; set; } = new List<PodItem>();

    public ICollection<PodAttachment> Attachments { get; set; } = new List<PodAttachment>();
}

/// <summary>
/// Chi tiết hàng giao thực tế trong POD.
/// </summary>
[Table("pod_items", Schema = "tenant")]
public class PodItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PodId { get; set; }

    [ForeignKey(nameof(PodId))]
    public DeliveryPod? Pod { get; set; }

    public Guid? SalesOrderItemId { get; set; }

    [ForeignKey(nameof(SalesOrderItemId))]
    public SalesOrderItem? SalesOrderItem { get; set; }

    public Guid ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    /// <summary>Snapshot tên sản phẩm tại thời điểm giao (tránh sửa đổi danh mục).</summary>
    [MaxLength(500)]
    public string? ProductName { get; set; }

    [Required, MaxLength(20)]
    public string UomCode { get; set; } = "CAI";

    public decimal OrderedQty { get; set; }

    public decimal DeliveredQty { get; set; }

    /// <summary>Hàng rớt / mang về.</summary>
    public decimal ReturnedQty { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    [MaxLength(255)]
    public string? Notes { get; set; }
}

/// <summary>
/// Media đính kèm POD (chữ ký, ảnh hàng rớt, bằng chứng thất bại).
/// ★ D6: file lưu trên đĩa, DB chỉ lưu đường dẫn.
/// </summary>
[Table("pod_attachments", Schema = "tenant")]
public class PodAttachment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PodId { get; set; }

    [ForeignKey(nameof(PodId))]
    public DeliveryPod? Pod { get; set; }

    /// <summary>SIGNATURE | HANG_ROT | FAILED_EVIDENCE</summary>
    [Required, MaxLength(20)]
    public string Kind { get; set; } = "SIGNATURE";

    [Required, MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string ContentType { get; set; } = "application/octet-stream";

    public int SizeBytes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
