using ERP.FMC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.FMC.Infrastructure.Configurations;

/// <summary>
/// Ánh xạ tường minh giữa property của entity và cột thực tế trong database.
///
/// Toàn bộ cột đã được chuyển PascalCase -> snake_case tự động trong
/// <c>AppDbContext.OnModelCreating</c>. File này chỉ xử lý phần lệch convention:
///   - Cột có tên khác hẳn (average_cost, effective_date, quantity_in...)
///   - Cột Temporal do SQL Server quản lý (SysStartTime / SysEndTime giữ nguyên PascalCase)
///   - Property KHÔNG có cột tương ứng trong DB -> phải Ignore, nếu không mọi
///     query sẽ lỗi "Invalid column name ..."
/// </summary>
internal static class SchemaMap
{
    /// <summary>
    /// Cột system-versioned của SQL Server luôn có tên PascalCase
    /// (<c>SysStartTime</c>/<c>SysEndTime</c>) nên phải ghi đè lại tên cột
    /// (mặc định sẽ bị đổi thành <c>sys_start_time</c>).
    ///
    /// Đồng thời phải đánh dấu 2 cột này là GENERATED ALWAYS do SQL Server tự sinh:
    /// nếu không EF Core sẽ đưa chúng vào câu INSERT và báo lỗi
    /// "Cannot insert an explicit value into a GENERATED ALWAYS column".
    /// </summary>
    public static void MapTemporal<T>(EntityTypeBuilder<T> builder) where T : class
    {
        var meta = builder.Metadata;

        if (meta.FindProperty("SysStartTime") != null)
        {
            builder.Property("SysStartTime")
                .HasColumnName("SysStartTime")
                .ValueGeneratedOnAddOrUpdate();
        }

        if (meta.FindProperty("SysEndTime") != null)
        {
            builder.Property("SysEndTime")
                .HasColumnName("SysEndTime")
                .ValueGeneratedOnAddOrUpdate();
        }
    }

    /// <summary>Bảng KHÔNG có 2 cột Temporal -> bỏ qua property nếu entity có khai báo.</summary>
    public static void IgnoreTemporal<T>(EntityTypeBuilder<T> builder) where T : class
    {
        var meta = builder.Metadata;
        if (meta.FindProperty("SysStartTime") != null) builder.Ignore("SysStartTime");
        if (meta.FindProperty("SysEndTime") != null) builder.Ignore("SysEndTime");
    }
}

public class PartnerSchemaConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        SchemaMap.MapTemporal(builder);

        // Bảng partners không có các cột này.
        builder.Ignore(p => p.CreatedBy);
        builder.Ignore(p => p.UpdatedBy);
    }
}

public class ProductSchemaConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        SchemaMap.MapTemporal(builder);

        //  barcode / min_stock / max_stock / reorder_point
        //   ĐÃ có trong schema MS SQL -> bỏ Ignore (trước đây entity có
        //   nhưng mapping lại Ignore nên app không đọc được).
        builder.Ignore(p => p.CreatedBy);
        builder.Ignore(p => p.UpdatedBy);
    }
}

public class CategorySchemaConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Ignore(c => c.SortOrder);
        builder.Ignore(c => c.CreatedBy);
        builder.Ignore(c => c.UpdatedBy);
    }
}

public class RouteSchemaConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> builder)
    {
        SchemaMap.IgnoreTemporal(builder);

        builder.Ignore(r => r.District);
        builder.Ignore(r => r.SupervisorId);
        builder.Ignore(r => r.CreatedBy);
        builder.Ignore(r => r.UpdatedBy);
    }
}

public class WarehouseSchemaConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        SchemaMap.IgnoreTemporal(builder);

        builder.Ignore(w => w.ManagerId);
        builder.Ignore(w => w.CreatedBy);
        builder.Ignore(w => w.UpdatedBy);
    }
}

public class UserAccountSchemaConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        SchemaMap.MapTemporal(builder);

        builder.Ignore(u => u.PasswordHash);
        builder.Ignore(u => u.EmployeeCode);
        builder.Ignore(u => u.CreatedBy);
        builder.Ignore(u => u.UpdatedBy);
    }
}

public class SalesOrderSchemaConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        SchemaMap.MapTemporal(builder);

        // sales_orders có created_by nhưng KHÔNG có updated_by.
        builder.Ignore(o => o.UpdatedBy);
    }
}
public class SalesOrderItemSchemaConfiguration : IEntityTypeConfiguration<SalesOrderItem>
{
    public void Configure(EntityTypeBuilder<SalesOrderItem> builder)
    {
        // sales_order_items chỉ có created_at.
        builder.Ignore(i => i.UpdatedAt);
        builder.Ignore(i => i.CreatedBy);
        builder.Ignore(i => i.UpdatedBy);
    }
}

public class InventoryBalanceSchemaConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.Property(b => b.OnHand).HasColumnName("quantity_on_hand");
        builder.Property(b => b.Reserved).HasColumnName("quantity_reserved");
        builder.Property(b => b.LastCost).HasColumnName("average_cost");
        builder.Property(b => b.LastMovementAt).HasColumnName("last_transaction_at");

        // quantity_available và total_value là COMPUTED PERSISTED
        //   PERSISTED trong SQL Server -> EF không được ghi vào, chỉ đọc.
        builder.Property(b => b.Available).HasColumnName("quantity_available").ValueGeneratedOnAddOrUpdate();
        builder.Ignore(b => b.TotalValue);

        // inventory_balances chỉ có updated_at (không có created_at).
        builder.Ignore(b => b.CreatedAt);
        builder.Ignore(b => b.CreatedBy);
        builder.Ignore(b => b.UpdatedBy);
    }
}

public class PriceListSchemaConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        SchemaMap.MapTemporal(builder);

        builder.Property(p => p.EffectiveFrom).HasColumnName("effective_date");
        builder.Property(p => p.EffectiveTo).HasColumnName("expiry_date");

        builder.Ignore(p => p.ApplyTo);
        builder.Ignore(p => p.MaxQty);

        // price_lists chỉ có created_at.
        builder.Ignore(p => p.UpdatedAt);
        builder.Ignore(p => p.CreatedBy);
        builder.Ignore(p => p.UpdatedBy);
    }
}

public class StockLedgerSchemaConfiguration : IEntityTypeConfiguration<StockLedger>
{
    public void Configure(EntityTypeBuilder<StockLedger> builder)
    {
        builder.Property(l => l.TransactionType).HasColumnName("transaction_type");
        builder.Property(l => l.TransactionTime).HasColumnName("transaction_time");
        builder.Property(l => l.QuantityIn).HasColumnName("quantity_in");
        builder.Property(l => l.QuantityOut).HasColumnName("quantity_out");
        builder.Property(l => l.PartnerId).HasColumnName("partner_id");
    }
}

// ===================================================================
// Mapping 5 entity nghiệp vụ giao hàng
// ===================================================================

public class DeliveryTripSchemaConfiguration : IEntityTypeConfiguration<DeliveryTrip>
{
    public void Configure(EntityTypeBuilder<DeliveryTrip> builder)
    {
        SchemaMap.MapTemporal(builder);

        builder.Property(t => t.TripNumber).HasColumnName("trip_number");
        builder.Property(t => t.TripDate).HasColumnName("trip_date");
        builder.Property(t => t.WarehouseId).HasColumnName("warehouse_id");
        builder.Property(t => t.DriverId).HasColumnName("driver_id");
        builder.Property(t => t.VehiclePlate).HasColumnName("vehicle_plate");
        builder.Property(t => t.TotalOrders).HasColumnName("total_orders");
        builder.Property(t => t.TotalAmount).HasColumnName("total_amount");
        builder.Property(t => t.DeliveredCount).HasColumnName("delivered_count");
        builder.Property(t => t.FailedCount).HasColumnName("failed_count");
        builder.Property(t => t.TotalCash).HasColumnName("total_cash");
        builder.Property(t => t.TotalTransfer).HasColumnName("total_transfer");
        builder.Property(t => t.TotalOldDebt).HasColumnName("total_old_debt");
        builder.Property(t => t.TotalCurrentDebt).HasColumnName("total_current_debt");
        builder.Property(t => t.StartedAt).HasColumnName("started_at");
        builder.Property(t => t.CompletedAt).HasColumnName("completed_at");

        // delivery_trips KHÔNG có created_by / updated_by.
        builder.Ignore(t => t.CreatedBy);
        builder.Ignore(t => t.UpdatedBy);
    }
}

public class DeliveryTripOrderSchemaConfiguration : IEntityTypeConfiguration<DeliveryTripOrder>
{
    public void Configure(EntityTypeBuilder<DeliveryTripOrder> builder)
    {
        builder.Property(o => o.TripId).HasColumnName("trip_id");
        builder.Property(o => o.SalesOrderId).HasColumnName("sales_order_id");
        builder.Property(o => o.StopOrder).HasColumnName("stop_order");
        builder.Property(o => o.FailureReason).HasColumnName("failure_reason");
        builder.Property(o => o.FailureCount).HasColumnName("failure_count");
        builder.Property(o => o.OldDebtAmount).HasColumnName("old_debt_amount");

        // delivery_trip_orders chỉ có created_at + updated_at,
        // KHÔNG có created_by / updated_by.
        builder.Ignore(o => o.CreatedBy);
        builder.Ignore(o => o.UpdatedBy);
    }
}

public class DeliveryPodSchemaConfiguration : IEntityTypeConfiguration<DeliveryPod>
{
    public void Configure(EntityTypeBuilder<DeliveryPod> builder)
    {
        builder.Property(p => p.ClientPodId).HasColumnName("client_pod_id");
        builder.Property(p => p.TripOrderId).HasColumnName("trip_order_id");
        builder.Property(p => p.SalesOrderId).HasColumnName("sales_order_id");
        builder.Property(p => p.PartnerId).HasColumnName("partner_id");
        builder.Property(p => p.ArrivedAt).HasColumnName("arrived_at");
        builder.Property(p => p.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(p => p.DeliveryStatus).HasColumnName("delivery_status");
        builder.Property(p => p.NewOrderCash).HasColumnName("new_order_cash");
        builder.Property(p => p.NewOrderTransfer).HasColumnName("new_order_transfer");
        builder.Property(p => p.NewOrderCredit).HasColumnName("new_order_credit");
        builder.Property(p => p.OldDebtCash).HasColumnName("old_debt_cash");
        builder.Property(p => p.OldDebtTransfer).HasColumnName("old_debt_transfer");
        builder.Property(p => p.OldDebtCredit).HasColumnName("old_debt_credit");
        builder.Property(p => p.TotalCollected).HasColumnName("total_collected");
        builder.Property(p => p.RecipientName).HasColumnName("recipient_name");
        builder.Property(p => p.SignaturePath).HasColumnName("signature_path");
        builder.Property(p => p.FailureReason).HasColumnName("failure_reason");
        builder.Property(p => p.DeliveryLatitude).HasColumnName("delivery_latitude");
        builder.Property(p => p.DeliveryLongitude).HasColumnName("delivery_longitude");
        builder.Property(p => p.DeliveryGpsAccuracyM).HasColumnName("delivery_gps_accuracy_m");
        builder.Property(p => p.DistanceToPartnerM).HasColumnName("distance_to_partner_m");
        builder.Property(p => p.GpsCapturedAt).HasColumnName("gps_captured_at");
        builder.Property(p => p.GpsGateResult).HasColumnName("gps_gate_result");
        builder.Property(p => p.GpsGateDistanceM).HasColumnName("gps_gate_distance_m");
        builder.Property(p => p.TimeGuardResult).HasColumnName("time_guard_result");

        // delivery_pod KHÔNG có updated_by.
        builder.Ignore(p => p.UpdatedBy);
    }
}

public class PodItemSchemaConfiguration : IEntityTypeConfiguration<PodItem>
{
    public void Configure(EntityTypeBuilder<PodItem> builder)
    {
        builder.Property(i => i.PodId).HasColumnName("pod_id");
        builder.Property(i => i.SalesOrderItemId).HasColumnName("sales_order_item_id");
        builder.Property(i => i.ProductId).HasColumnName("product_id");
        builder.Property(i => i.ProductName).HasColumnName("product_name");
        builder.Property(i => i.UomCode).HasColumnName("uom_code");
        builder.Property(i => i.OrderedQty).HasColumnName("ordered_qty");
        builder.Property(i => i.DeliveredQty).HasColumnName("delivered_qty");
        builder.Property(i => i.ReturnedQty).HasColumnName("returned_qty");
        builder.Property(i => i.UnitPrice).HasColumnName("unit_price");
        builder.Property(i => i.LineTotal).HasColumnName("line_total");
    }
}

public class PodAttachmentSchemaConfiguration : IEntityTypeConfiguration<PodAttachment>
{
    public void Configure(EntityTypeBuilder<PodAttachment> builder)
    {
        builder.Property(a => a.PodId).HasColumnName("pod_id");
        builder.Property(a => a.Kind).HasColumnName("kind");
        builder.Property(a => a.FilePath).HasColumnName("file_path");
        builder.Property(a => a.ContentType).HasColumnName("content_type");
        builder.Property(a => a.SizeBytes).HasColumnName("size_bytes");
    }
}

// ===================================================================
// ★ PHASE 6B.4 — ĐỐI SOÁT CUỐI NGÀY (EOD)
// ===================================================================

public class EodReconciliationSchemaConfiguration : IEntityTypeConfiguration<EodReconciliation>
{
    public void Configure(EntityTypeBuilder<EodReconciliation> builder)
    {
        builder.Property(r => r.TripId).HasColumnName("trip_id");
        builder.Property(r => r.ReconciliationDate).HasColumnName("reconciliation_date");
        builder.Property(r => r.DriverId).HasColumnName("driver_id");
        builder.Property(r => r.DriverName).HasColumnName("driver_name");
        builder.Property(r => r.WarehouseId).HasColumnName("warehouse_id");
        builder.Property(r => r.ExpectedCash).HasColumnName("expected_cash");
        builder.Property(r => r.ActualCash).HasColumnName("actual_cash");
        builder.Property(r => r.CashDifference).HasColumnName("cash_difference");
        builder.Property(r => r.CashVarianceReason).HasColumnName("cash_variance_reason");
        builder.Property(r => r.CashVarianceAction).HasColumnName("cash_variance_action");
        builder.Property(r => r.ExpectedItems).HasColumnName("expected_items");
        builder.Property(r => r.DeliveredItems).HasColumnName("delivered_items");
        builder.Property(r => r.ReturnedItems).HasColumnName("returned_items");
        builder.Property(r => r.ItemsDifference).HasColumnName("items_difference");
        builder.Property(r => r.ItemsVarianceReason).HasColumnName("items_variance_reason");
        builder.Property(r => r.ItemsVarianceAction).HasColumnName("items_variance_action");
        builder.Property(r => r.Status).HasColumnName("status");
        builder.Property(r => r.TotalOrders).HasColumnName("total_orders");
        builder.Property(r => r.SuccessfulOrders).HasColumnName("successful_orders");
        builder.Property(r => r.FailedOrders).HasColumnName("failed_orders");
        builder.Property(r => r.TripExpenses).HasColumnName("trip_expenses");
        builder.Property(r => r.Notes).HasColumnName("notes");
        builder.Property(r => r.ApprovedBy).HasColumnName("approved_by");
        builder.Property(r => r.ApprovedAt).HasColumnName("approved_at");
    }
}

public class EodReconciliationItemSchemaConfiguration : IEntityTypeConfiguration<EodReconciliationItem>
{
    public void Configure(EntityTypeBuilder<EodReconciliationItem> builder)
    {
        builder.Property(i => i.ReconciliationId).HasColumnName("reconciliation_id");
        builder.Property(i => i.SalesOrderId).HasColumnName("sales_order_id");
        builder.Property(i => i.PartnerId).HasColumnName("partner_id");
        builder.Property(i => i.PartnerName).HasColumnName("partner_name");
        builder.Property(i => i.ExpectedCash).HasColumnName("expected_cash");
        builder.Property(i => i.CollectedCash).HasColumnName("collected_cash");
        builder.Property(i => i.OldDebtCollected).HasColumnName("old_debt_collected");
        builder.Property(i => i.CashDifference).HasColumnName("cash_difference");
        builder.Property(i => i.CashVarianceReason).HasColumnName("cash_variance_reason");
        builder.Property(i => i.ExpectedQty).HasColumnName("expected_qty");
        builder.Property(i => i.DeliveredQty).HasColumnName("delivered_qty");
        builder.Property(i => i.ReturnedQty).HasColumnName("returned_qty");
        builder.Property(i => i.ItemsDifference).HasColumnName("items_difference");
        builder.Property(i => i.ReconciliationStatus).HasColumnName("reconciliation_status");
    }
}
