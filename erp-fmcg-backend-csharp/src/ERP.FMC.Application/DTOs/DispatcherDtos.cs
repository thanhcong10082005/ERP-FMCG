using System.Text.Json.Serialization;

namespace ERP.FMC.Application.DTOs;

// ===================================================================
// — DTO cho DISPATCHER
// -------------------------------------------------------------------
// Dispatcher (điều phối) cần nhìn được TOÀN BỘ chuyến xe, không chỉ
// chuyến của mình. Đây là lớp nghiệp vụ mới hoàn toàn.
// ===================================================================

/// <summary>Yêu cầu tạo chuyến xe (dispatcher).</summary>
public class CreateTripRequestDto
{
    /// <summary>Kho xuất phát (GUID).</summary>
    [JsonPropertyName("warehouse_id")]
    public string WarehouseId { get; set; } = string.Empty;

    /// <summary>Tài xế được gán (GUID) — có thể để trống để gán sau.</summary>
    [JsonPropertyName("driver_id")]
    public string? DriverId { get; set; }

    /// <summary>Ngày chạy xe (yyyy-MM-dd). Mặc định = hôm nay (giờ VN).</summary>
    [JsonPropertyName("trip_date")]
    public string? TripDate { get; set; }

    [JsonPropertyName("vehicle_plate")]
    public string? VehiclePlate { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>Yêu cầu gắn đơn hàng vào chuyến.</summary>
public class AttachOrdersRequestDto
{
    /// <summary>Danh sách sales_order_id (GUID) cần gắn.</summary>
    [JsonPropertyName("sales_order_ids")]
    public List<string> SalesOrderIds { get; set; } = new();

    /// <summary>Ghi đè stop_order nếu muốn đặt thứ tự giao thủ công.</summary>
    [JsonPropertyName("stop_order_start")]
    public int? StopOrderStart { get; set; }
}

/// <summary>Chi tiết chuyến đầy đủ cho dispatcher (trip + POD + GPS).</summary>
public class TripFullDto
{
    [JsonPropertyName("trip")]
    public DeliveryTripDto? Trip { get; set; }

    [JsonPropertyName("pods")]
    public List<PodDetailDto> Pods { get; set; } = new();

    /// <summary>Điểm giao để vẽ bản đồ audit.</summary>
    [JsonPropertyName("map_points")]
    public List<MapPointDto> MapPoints { get; set; } = new();

    /// <summary>Thống kê nhanh cho header màn hình dispatcher.</summary>
    [JsonPropertyName("summary")]
    public TripSummaryDto? Summary { get; set; }
}

/// <summary>Chi tiết 1 POD (cho dispatcher).</summary>
public class PodDetailDto
{
    [JsonPropertyName("pod_id")]
    public string PodId { get; set; } = string.Empty;

    [JsonPropertyName("client_pod_id")]
    public string ClientPodId { get; set; } = string.Empty;

    [JsonPropertyName("trip_order_id")]
    public string TripOrderId { get; set; } = string.Empty;

    [JsonPropertyName("sales_order_id")]
    public string SalesOrderId { get; set; } = string.Empty;

    [JsonPropertyName("partner_id")]
    public string PartnerId { get; set; } = string.Empty;

    [JsonPropertyName("partner_name")]
    public string? PartnerName { get; set; }

    [JsonPropertyName("delivery_status")]
    public string DeliveryStatus { get; set; } = string.Empty;

    [JsonPropertyName("total_collected")]
    public decimal TotalCollected { get; set; }

    // chi tiết 6 ô thanh toán.
    //   Trước đây, `PodDetailDto` chỉ có `total_collected` (tổng) —
    //   dispatcher/Kế toán KHÔNG biết bao nhiêu là tiền mặt đơn mới, bao
    //   nhiêu là thu nợ cũ, bao nhiêu là ghi nợ -> không đối chiếu được
    //   với bảng công nợ, không xuất Excel được.
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

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }

    /// <summary>GPS để vẽ bản đồ audit.</summary>
    [JsonPropertyName("delivery_latitude")]
    public double? DeliveryLatitude { get; set; }

    [JsonPropertyName("delivery_longitude")]
    public double? DeliveryLongitude { get; set; }

    [JsonPropertyName("gps_gate_result")]
    public string? GpsGateResult { get; set; }

    [JsonPropertyName("gps_gate_distance_m")]
    public int? GpsGateDistanceM { get; set; }

    [JsonPropertyName("distance_to_partner_m")]
    public int? DistanceToPartnerM { get; set; }

    [JsonPropertyName("time_guard_result")]
    public string? TimeGuardResult { get; set; }

    [JsonPropertyName("arrived_at")]
    public string? ArrivedAt { get; set; }

    [JsonPropertyName("delivered_at")]
    public string? DeliveredAt { get; set; }

    [JsonPropertyName("gps_captured_at")]
    public string? GpsCapturedAt { get; set; }

    [JsonPropertyName("recipient_name")]
    public string? RecipientName { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>Điểm trên bản đồ audit.</summary>
public class MapPointDto
{
    [JsonPropertyName("pod_id")]
    public string PodId { get; set; } = string.Empty;

    [JsonPropertyName("partner_name")]
    public string? PartnerName { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    /// <summary>PASS | FAIL | SKIPPED_NO_PARTNER_GPS | FAIL_NO_GPS.</summary>
    [JsonPropertyName("gps_gate_result")]
    public string? GpsGateResult { get; set; }

    [JsonPropertyName("distance_m")]
    public int? DistanceMeters { get; set; }

    [JsonPropertyName("delivery_status")]
    public string? DeliveryStatus { get; set; }
}

/// <summary>Tóm tắt chuyến cho dispatcher.</summary>
public class TripSummaryDto
{
    [JsonPropertyName("total_orders")]
    public int TotalOrders { get; set; }

    [JsonPropertyName("delivered_count")]
    public int DeliveredCount { get; set; }

    [JsonPropertyName("failed_count")]
    public int FailedCount { get; set; }

    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("total_cash")]
    public decimal TotalCash { get; set; }

    [JsonPropertyName("total_transfer")]
    public decimal TotalTransfer { get; set; }

    [JsonPropertyName("total_old_debt")]
    public decimal TotalOldDebt { get; set; }

    /// <summary>Tỷ lệ giao thành công (0-100).</summary>
    [JsonPropertyName("success_rate")]
    public decimal SuccessRate { get; set; }

    /// <summary>Số POD có cảnh báo GPS (cần dispatcher xem lại).</summary>
    [JsonPropertyName("gps_alerts")]
    public int GpsAlerts { get; set; }
}
