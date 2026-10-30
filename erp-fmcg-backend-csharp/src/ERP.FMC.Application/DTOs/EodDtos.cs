using System.Text.Json.Serialization;

namespace ERP.FMC.Application.DTOs;

// ===================================================================
// WIZARD ĐỐI SOÁT CUỐI NGÀY (EOD)
// -------------------------------------------------------------------
// Nguồn: migration Postgres `v13_3__eod_reconciliation.sql` — hệ thống
// NestJS đã có nhưng MS SQL chưa. Driver App có màn hình `EODScreen`
// nhưng chưa có API đối soát thật.
//
// Nghiệp vụ: cuối ca, tài xế nộp tiền mặt thực tế, dispatcher đối chiếu
// với tiền mặt kỳ vọng (SUM POD trong chuyến) → phát hiện thiếu/thừa.
// ===================================================================

/// <summary>Yêu cầu tạo phiếu đối soát EOD từ chuyến.</summary>
public class CreateEodRequestDto
{
    /// <summary>Tiền mặt thực tế tài xế nộp (dispatcher nhập).</summary>
    [JsonPropertyName("actual_cash")]
    public decimal ActualCash { get; set; }

    /// <summary>Chi phí chuyến (nếu có).</summary>
    [JsonPropertyName("trip_expenses")]
    public decimal TripExpenses { get; set; }

    [JsonPropertyName("cash_variance_reason")]
    public string? CashVarianceReason { get; set; }

    /// <summary>DRIVER_REIMBURSE | SUSPEND_DEBT | WRITE_OFF.</summary>
    [JsonPropertyName("cash_variance_action")]
    public string? CashVarianceAction { get; set; }

    [JsonPropertyName("items_variance_reason")]
    public string? ItemsVarianceReason { get; set; }

    /// <summary>DRIVER_COMPENSATE | RETURN_TO_STOCK | WRITE_OFF.</summary>
    [JsonPropertyName("items_variance_action")]
    public string? ItemsVarianceAction { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>Chi tiết phiếu đối soát.</summary>
public class EodReconciliationDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("trip_id")]
    public string? TripId { get; set; }

    [JsonPropertyName("reconciliation_date")]
    public string ReconciliationDate { get; set; } = string.Empty;

    [JsonPropertyName("driver_id")]
    public string? DriverId { get; set; }

    [JsonPropertyName("driver_name")]
    public string? DriverName { get; set; }

    [JsonPropertyName("warehouse_id")]
    public string? WarehouseId { get; set; }

    [JsonPropertyName("expected_cash")]
    public decimal ExpectedCash { get; set; }

    [JsonPropertyName("actual_cash")]
    public decimal ActualCash { get; set; }

    /// <summary>actual_cash − expected_cash (âm = thiếu).</summary>
    [JsonPropertyName("cash_difference")]
    public decimal CashDifference { get; set; }

    [JsonPropertyName("cash_variance_reason")]
    public string? CashVarianceReason { get; set; }

    [JsonPropertyName("cash_variance_action")]
    public string? CashVarianceAction { get; set; }

    [JsonPropertyName("expected_items")]
    public int ExpectedItems { get; set; }

    [JsonPropertyName("delivered_items")]
    public int DeliveredItems { get; set; }

    [JsonPropertyName("returned_items")]
    public int ReturnedItems { get; set; }

    [JsonPropertyName("items_difference")]
    public int ItemsDifference { get; set; }

    [JsonPropertyName("items_variance_reason")]
    public string? ItemsVarianceReason { get; set; }

    [JsonPropertyName("items_variance_action")]
    public string? ItemsVarianceAction { get; set; }

    /// <summary>PENDING | APPROVED | DISPUTED | CANCELLED.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "PENDING";

    [JsonPropertyName("total_orders")]
    public int TotalOrders { get; set; }

    [JsonPropertyName("successful_orders")]
    public int SuccessfulOrders { get; set; }

    [JsonPropertyName("failed_orders")]
    public int FailedOrders { get; set; }

    [JsonPropertyName("trip_expenses")]
    public decimal TripExpenses { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("approved_by")]
    public string? ApprovedBy { get; set; }

    [JsonPropertyName("approved_at")]
    public string? ApprovedAt { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<EodReconciliationItemDto> Items { get; set; } = new();
}

/// <summary>Dòng chi tiết đối soát (theo từng đơn).</summary>
public class EodReconciliationItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("reconciliation_id")]
    public string ReconciliationId { get; set; } = string.Empty;

    [JsonPropertyName("sales_order_id")]
    public string? SalesOrderId { get; set; }

    [JsonPropertyName("partner_id")]
    public string? PartnerId { get; set; }

    [JsonPropertyName("partner_name")]
    public string? PartnerName { get; set; }

    [JsonPropertyName("expected_cash")]
    public decimal ExpectedCash { get; set; }

    [JsonPropertyName("collected_cash")]
    public decimal CollectedCash { get; set; }

    [JsonPropertyName("old_debt_collected")]
    public decimal OldDebtCollected { get; set; }

    [JsonPropertyName("cash_difference")]
    public decimal CashDifference { get; set; }

    [JsonPropertyName("cash_variance_reason")]
    public string? CashVarianceReason { get; set; }

    [JsonPropertyName("expected_qty")]
    public decimal ExpectedQty { get; set; }

    [JsonPropertyName("delivered_qty")]
    public decimal DeliveredQty { get; set; }

    [JsonPropertyName("returned_qty")]
    public decimal ReturnedQty { get; set; }

    [JsonPropertyName("items_difference")]
    public decimal ItemsDifference { get; set; }

    /// <summary>MATCHED | CASH_SHORT | CASH_OVER | ITEMS_SHORT | FULL_MATCH.</summary>
    [JsonPropertyName("reconciliation_status")]
    public string ReconciliationStatus { get; set; } = "MATCHED";
}
