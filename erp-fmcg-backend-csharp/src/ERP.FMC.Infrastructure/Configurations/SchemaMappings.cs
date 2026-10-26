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
        builder.Ignore(p => p.CurrentDebt);
        builder.Ignore(p => p.CreatedBy);
        builder.Ignore(p => p.UpdatedBy);
    }
}

public class ProductSchemaConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        SchemaMap.MapTemporal(builder);

        builder.Ignore(p => p.Barcode);
        builder.Ignore(p => p.MinStock);
        builder.Ignore(p => p.MaxStock);
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
        builder.Property(b => b.Available).HasColumnName("quantity_available");
        builder.Property(b => b.LastCost).HasColumnName("average_cost");
        builder.Property(b => b.LastMovementAt).HasColumnName("last_transaction_at");

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
