using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ERP.FMC.Application.DTOs;

// ===================================================================
// DTO CHO DRIVER APP
// -------------------------------------------------------------------
// ★ HỢP ĐỒNG (contract) giữa Backend C# và Driver App (TypeScript).
//
// Quy tắc bắt buộc:
//   1. Tất cả ID là `string` (Guid) — KHÔNG phải number.
//      Lý do: JS number chỉ an toàn tới 2^53-1; Guid 128-bit vượt ngưỡng.
//      Driver App đã đổi `number` -> `string`.
//   2. Tên field snake_case (JsonPropertyName) — khớp TypeScript.
//   3. Status là `string` (không enum) — tránh JsonStringEnumConverter
//      đổi tên theo convention, gây lệch với Driver App.
//   4. Tiền tệ là `decimal` — JSON number. Tránh float.
// ===================================================================

// -------------------------------------------------------------------
// Trip
// -------------------------------------------------------------------

public class DeliveryTripDto
{
    [JsonPropertyName("trip_id")]
    public string TripId { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = "DEFAULT";

    [JsonPropertyName("trip_number")]
    public string TripNumber { get; set; } = string.Empty;

    [JsonPropertyName("trip_date")]
    public string TripDate { get; set; } = string.Empty;

    [JsonPropertyName("warehouse_id")]
    public string WarehouseId { get; set; } = string.Empty;

    [JsonPropertyName("warehouse_name")]
    public string? WarehouseName { get; set; }

    [JsonPropertyName("driver_id")]
    public string? DriverId { get; set; }

    [JsonPropertyName("driver_name")]
    public string? DriverName { get; set; }

    [JsonPropertyName("vehicle_plate")]
    public string? VehiclePlate { get; set; }

    /// <summary>PREPARING | LOADING | DELIVERING | COMPLETED | PARTIAL_DELIVERED | CANCELLED</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "PREPARING";

    [JsonPropertyName("total_orders")]
    public int TotalOrders { get; set; }

    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("delivered_count")]
    public int DeliveredCount { get; set; }

    [JsonPropertyName("failed_count")]
    public int FailedCount { get; set; }

    [JsonPropertyName("total_cash")]
    public decimal TotalCash { get; set; }

    [JsonPropertyName("total_transfer")]
    public decimal TotalTransfer { get; set; }

    [JsonPropertyName("total_old_debt")]
    public decimal TotalOldDebt { get; set; }

    /// <summary>★ DB-C7: tổng công nợ hiện tại của các khách trong chuyến.</summary>
    [JsonPropertyName("total_current_debt")]
    public decimal TotalCurrentDebt { get; set; }

    [JsonPropertyName("started_at")]
    public string? StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public string? CompletedAt { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("orders")]
    public List<TripOrderDto>? Orders { get; set; }
}

// -------------------------------------------------------------------
// Trip Order (điểm giao)
// -------------------------------------------------------------------

public class TripOrderDto
{
    [JsonPropertyName("trip_order_id")]
    public string TripOrderId { get; set; } = string.Empty;

    [JsonPropertyName("trip_id")]
    public string TripId { get; set; } = string.Empty;

    [JsonPropertyName("so_id")]
    public string SalesOrderId { get; set; } = string.Empty;

    [JsonPropertyName("so_number")]
    public string SalesOrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("stop_order")]
    public int StopOrder { get; set; }

    /// <summary>PENDING | DELIVERED | PARTIAL | FAILED | CANCELLED</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "PENDING";

    [JsonPropertyName("delivery_status")]
    public string? DeliveryStatus { get; set; }

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }

    [JsonPropertyName("failure_count")]
    public int FailureCount { get; set; }

    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("paid_amount")]
    public decimal PaidAmount { get; set; }

    [JsonPropertyName("payment_status")]
    public string? PaymentStatus { get; set; }

    [JsonPropertyName("partner_id")]
    public string PartnerId { get; set; } = string.Empty;

    [JsonPropertyName("partner_name")]
    public string PartnerName { get; set; } = string.Empty;

    [JsonPropertyName("partner_phone")]
    public string? PartnerPhone { get; set; }

    [JsonPropertyName("partner_address")]
    public string? PartnerAddress { get; set; }

    [JsonPropertyName("partner_latitude")]
    public double? PartnerLatitude { get; set; }

    [JsonPropertyName("partner_longitude")]
    public double? PartnerLongitude { get; set; }

    /// <summary>Alias cho partner_latitude (Driver App cũ dùng latitude).</summary>
    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    /// <summary>★ Công nợ cũ của khách — Driver App hiển thị để tài xế thu.</summary>
    [JsonPropertyName("old_debt_amount")]
    public decimal OldDebtAmount { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("items")]
    public List<DriverOrderItemDto>? Items { get; set; }
}

public class DriverOrderItemDto
{
    [JsonPropertyName("so_item_id")]
    public string SalesOrderItemId { get; set; } = string.Empty;

    [JsonPropertyName("so_id")]
    public string SalesOrderId { get; set; } = string.Empty;

    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("uom_code")]
    public string UomCode { get; set; } = "CAI";

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("delivered_qty")]
    public decimal? DeliveredQty { get; set; }

    [JsonPropertyName("unit_price")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("discount_amount")]
    public decimal DiscountAmount { get; set; }

    [JsonPropertyName("line_subtotal")]
    public decimal LineSubtotal { get; set; }

    [JsonPropertyName("line_total")]
    public decimal LineTotal { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

// -------------------------------------------------------------------
// POD — request
// -------------------------------------------------------------------

/// <summary>
/// Payload POST /api/sales/pod.
///
/// ★ client_pod_id BẮT BUỘC (idempotency key, D5).
///   Client sinh UUID v4 MỘT LẦN khi tài xế bấm "Giao hang", gửi kèm
///   mọi lần retry. Server trả về cùng POD đã tồn tại (HTTP 200) thay
///   vì tạo bản ghi trùng.
/// </summary>
public class CreatePodRequestDto
{
    /// <summary>UUID v4 do client sinh. Bắt buộc.</summary>
    [Required]
    [JsonPropertyName("client_pod_id")]
    public string ClientPodId { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("trip_order_id")]
    public string TripOrderId { get; set; } = string.Empty;

    [JsonPropertyName("so_id")]
    public string? SalesOrderId { get; set; }

    [JsonPropertyName("partner_id")]
    public string? PartnerId { get; set; }

    /// <summary>DELIVERED | PARTIAL | FAILED</summary>
    [Required]
    [JsonPropertyName("delivery_status")]
    public string DeliveryStatus { get; set; } = "DELIVERED";

    [JsonPropertyName("new_order_cash")]
    public decimal NewOrderCash { get; set; }

    [JsonPropertyName("new_order_transfer")]
    public decimal NewOrderTransfer { get; set; }

    [JsonPropertyName("new_order_credit")]
    public decimal NewOrderCredit { get; set; }

    [JsonPropertyName("old_debt_cash")]
    public decimal OldDebtCash { get; set; }

    [JsonPropertyName("old_debt_transfer")]
    public decimal OldDebtTransfer { get; set; }

    [JsonPropertyName("old_debt_credit")]
    public decimal OldDebtCredit { get; set; }

    [JsonPropertyName("total_collected")]
    public decimal TotalCollected { get; set; }

    [JsonPropertyName("recipient_name")]
    public string? RecipientName { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>★ DB-C4: DOOR_LOCKED | WRONG_ORDER | CUSTOMER_ABSENT | TRAFFIC | OTHER</summary>
    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }

    // ---- GPS ----
    [JsonPropertyName("delivery_latitude")]
    public double? DeliveryLatitude { get; set; }

    [JsonPropertyName("delivery_longitude")]
    public double? DeliveryLongitude { get; set; }

    [JsonPropertyName("delivery_gps_accuracy_m")]
    public double? DeliveryGpsAccuracyM { get; set; }

    [JsonPropertyName("distance_to_partner_m")]
    public int? DistanceToPartnerM { get; set; }

    [JsonPropertyName("gps_captured_at")]
    public string? GpsCapturedAt { get; set; }

    // ---- Media (★ D6: đường dẫn, KHÔNG base64) ----
    [JsonPropertyName("signature_url")]
    public string? SignatureUrl { get; set; }

    [JsonPropertyName("photos")]
    public List<string>? Photos { get; set; }

    [JsonPropertyName("delivered_items")]
    public List<DeliveredItemDto>? DeliveredItems { get; set; }
}

public class DeliveredItemDto
{
    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("ordered_qty")]
    public decimal OrderedQty { get; set; }

    [JsonPropertyName("delivered_qty")]
    public decimal DeliveredQty { get; set; }

    [JsonPropertyName("returned_qty")]
    public decimal ReturnedQty { get; set; }

    [JsonPropertyName("unit_price")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("line_total")]
    public decimal LineTotal { get; set; }
}

// -------------------------------------------------------------------
// POD — response
// -------------------------------------------------------------------

public class PodResponseDto
{
    [JsonPropertyName("pod_id")]
    public string PodId { get; set; } = string.Empty;

    [JsonPropertyName("client_pod_id")]
    public string ClientPodId { get; set; } = string.Empty;

    [JsonPropertyName("trip_order_id")]
    public string TripOrderId { get; set; } = string.Empty;

    [JsonPropertyName("so_id")]
    public string SalesOrderId { get; set; } = string.Empty;

    [JsonPropertyName("partner_id")]
    public string PartnerId { get; set; } = string.Empty;

    [JsonPropertyName("delivery_status")]
    public string DeliveryStatus { get; set; } = string.Empty;

    [JsonPropertyName("total_collected")]
    public decimal TotalCollected { get; set; }

    /// <summary>
    /// true = POD mới tạo; false = đã tồn tại (idempotent hit).
    /// Driver App dùng cờ này để biết có cần gọi lại không.
    /// </summary>
    [JsonPropertyName("created")]
    public bool Created { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

// -------------------------------------------------------------------
// EOD Report
// -------------------------------------------------------------------

public class EodReportDto
{
    [JsonPropertyName("trip_id")]
    public string TripId { get; set; } = string.Empty;

    [JsonPropertyName("trip_number")]
    public string TripNumber { get; set; } = string.Empty;

    /// <summary>COMPLETED | PARTIAL_DELIVERED</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "COMPLETED";

    [JsonPropertyName("total_orders")]
    public int TotalOrders { get; set; }

    [JsonPropertyName("delivered_count")]
    public int DeliveredCount { get; set; }

    [JsonPropertyName("failed_count")]
    public int FailedCount { get; set; }

    [JsonPropertyName("total_cash")]
    public decimal TotalCash { get; set; }

    [JsonPropertyName("total_transfer")]
    public decimal TotalTransfer { get; set; }

    [JsonPropertyName("total_old_debt")]
    public decimal TotalOldDebt { get; set; }

    [JsonPropertyName("total_collected")]
    public decimal TotalCollected { get; set; }

    [JsonPropertyName("total_returned_qty")]
    public decimal TotalReturnedQty { get; set; }

    [JsonPropertyName("items")]
    public List<EodReportItemDto>? Items { get; set; }
}

public class EodReportItemDto
{
    [JsonPropertyName("trip_order_id")]
    public string TripOrderId { get; set; } = string.Empty;

    [JsonPropertyName("so_number")]
    public string SalesOrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("partner_name")]
    public string PartnerName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("total_collected")]
    public decimal TotalCollected { get; set; }

    [JsonPropertyName("returned_qty")]
    public decimal? ReturnedQty { get; set; }
}

// -------------------------------------------------------------------
// Master data cho Driver App (offline)
// -------------------------------------------------------------------

public class DriverPartnerDto
{
    [JsonPropertyName("partner_id")]
    public string PartnerId { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = "DEFAULT";

    [JsonPropertyName("partner_code")]
    public string PartnerCode { get; set; } = string.Empty;

    [JsonPropertyName("partner_name")]
    public string PartnerName { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("address_line")]
    public string? AddressLine { get; set; }

    [JsonPropertyName("ward")]
    public string? Ward { get; set; }

    [JsonPropertyName("district")]
    public string? District { get; set; }

    [JsonPropertyName("province")]
    public string? Province { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("partner_type")]
    public string PartnerType { get; set; } = "STORE";

    [JsonPropertyName("current_debt")]
    public decimal CurrentDebt { get; set; }

    [JsonPropertyName("old_debt")]
    public decimal OldDebt { get; set; }

    [JsonPropertyName("old_debt_date")]
    public string? OldDebtDate { get; set; }

    [JsonPropertyName("route_code")]
    public string? RouteCode { get; set; }
}

public class DriverProductDto
{
    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = "DEFAULT";

    [JsonPropertyName("sku")]
    public string Sku { get; set; } = string.Empty;

    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("barcode")]
    public string? Barcode { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("uom_code")]
    public string UomCode { get; set; } = "CAI";

    [JsonPropertyName("cost_price")]
    public decimal CostPrice { get; set; }

    [JsonPropertyName("selling_price")]
    public decimal SellingPrice { get; set; }

    [JsonPropertyName("min_stock")]
    public decimal MinStock { get; set; }

    [JsonPropertyName("max_stock")]
    public decimal MaxStock { get; set; }

    [JsonPropertyName("reorder_point")]
    public decimal ReorderPoint { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }
}
