using System.Text.Json.Serialization;

namespace ERP.FMC.Application.DTOs;

// ===================================================================
// DTO BÁO CÁO VẬN HÀNH
// -------------------------------------------------------------------
// 6 báo cáo mà dispatcher/quản lý cần hằng ngày:
//   1. Doanh thu theo tuyến
//   2. Tỷ lệ giao thành công (theo ngày / tuyến / tài xế)
//   3. Công nợ theo tuổi (aging)
//   4. Hàng rớt theo SKU
//   5. Tiền mặt theo ca (tài xế + ngày)
//   6. Tồn kho & cảnh báo dưới mức tối thiểu
// ===================================================================

/// <summary>1. Doanh thu theo tuyến.</summary>
public class RevenueByRouteDto
{
    [JsonPropertyName("route_code")]
    public string? RouteCode { get; set; }

    [JsonPropertyName("route_name")]
    public string? RouteName { get; set; }

    [JsonPropertyName("total_orders")]
    public int TotalOrders { get; set; }

    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("delivered_count")]
    public int DeliveredCount { get; set; }

    [JsonPropertyName("failed_count")]
    public int FailedCount { get; set; }

    [JsonPropertyName("collected_amount")]
    public decimal CollectedAmount { get; set; }
}

/// <summary>2. Tỷ lệ giao thành công.</summary>
public class DeliverySuccessRateDto
{
    [JsonPropertyName("dimension")]
    public string Dimension { get; set; } = string.Empty;   // DATE | ROUTE | DRIVER

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("total_orders")]
    public int TotalOrders { get; set; }

    [JsonPropertyName("delivered_count")]
    public int DeliveredCount { get; set; }

    [JsonPropertyName("failed_count")]
    public int FailedCount { get; set; }

    /// <summary>Tỷ lệ % (0-100).</summary>
    [JsonPropertyName("success_rate")]
    public decimal SuccessRate { get; set; }
}

/// <summary>3. Công nợ theo tuổi (aging buckets).</summary>
public class DebtAgingDto
{
    [JsonPropertyName("bucket")]
    public string Bucket { get; set; } = string.Empty;  // 0-30 | 31-60 | 61-90 | >90

    [JsonPropertyName("partner_count")]
    public int PartnerCount { get; set; }

    [JsonPropertyName("total_debt")]
    public decimal TotalDebt { get; set; }

    [JsonPropertyName("total_old_debt")]
    public decimal TotalOldDebt { get; set; }
}

/// <summary>4. Hàng rớt theo SKU.</summary>
public class ReturnedGoodsDto
{
    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("total_returned_qty")]
    public decimal TotalReturnedQty { get; set; }

    [JsonPropertyName("total_value")]
    public decimal TotalValue { get; set; }

    [JsonPropertyName("pod_count")]
    public int PodCount { get; set; }
}

/// <summary>5. Tiền mặt theo ca.</summary>
public class CashByShiftDto
{
    [JsonPropertyName("driver_id")]
    public string? DriverId { get; set; }

    [JsonPropertyName("driver_name")]
    public string? DriverName { get; set; }

    [JsonPropertyName("trip_id")]
    public string? TripId { get; set; }

    [JsonPropertyName("trip_number")]
    public string? TripNumber { get; set; }

    [JsonPropertyName("shift_date")]
    public string ShiftDate { get; set; } = string.Empty;

    [JsonPropertyName("new_order_cash")]
    public decimal NewOrderCash { get; set; }

    [JsonPropertyName("old_debt_cash")]
    public decimal OldDebtCash { get; set; }

    [JsonPropertyName("total_cash")]
    public decimal TotalCash { get; set; }

    [JsonPropertyName("pod_count")]
    public int PodCount { get; set; }
}

/// <summary>6. Tồn kho & cảnh báo dưới mức tối thiểu.</summary>
public class LowStockDto
{
    [JsonPropertyName("warehouse_id")]
    public string WarehouseId { get; set; } = string.Empty;

    [JsonPropertyName("warehouse_name")]
    public string? WarehouseName { get; set; }

    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("quantity_on_hand")]
    public decimal QuantityOnHand { get; set; }

    [JsonPropertyName("min_stock")]
    public decimal MinStock { get; set; }

    [JsonPropertyName("reorder_point")]
    public decimal ReorderPoint { get; set; }

    /// <summary>LOW (dưới min) | REORDER (dưới điểm đặt lại).</summary>
    [JsonPropertyName("alert_level")]
    public string AlertLevel { get; set; } = "LOW";
}
