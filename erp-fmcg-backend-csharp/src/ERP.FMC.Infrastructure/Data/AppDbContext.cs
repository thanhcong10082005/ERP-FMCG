using ERP.FMC.Domain.Common;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.FMC.Infrastructure.Data;

/// <summary>
/// EF Core DbContext cho ERP FMCG.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderItem> SalesOrderItems => Set<SalesOrderItem>();
    public DbSet<StockLedger> StockLedgers => Set<StockLedger>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportStaging> ImportStaging => Set<ImportStaging>();
    public DbSet<ImportError> ImportErrors => Set<ImportError>();
    public DbSet<ImportFieldMapping> ImportFieldMappings => Set<ImportFieldMapping>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // nghiệp vụ giao hàng
    public DbSet<DeliveryTrip> DeliveryTrips => Set<DeliveryTrip>();
    public DbSet<DeliveryTripOrder> DeliveryTripOrders => Set<DeliveryTripOrder>();
    public DbSet<DeliveryPod> DeliveryPods => Set<DeliveryPod>();
    public DbSet<PodItem> PodItems => Set<PodItem>();
    public DbSet<PodAttachment> PodAttachments => Set<PodAttachment>();

    // đối soát cuối ngày (EOD)
    public DbSet<EodReconciliation> EodReconciliations => Set<EodReconciliation>();
    public DbSet<EodReconciliationItem> EodReconciliationItems => Set<EodReconciliationItem>();

    /// <summary>
    /// Convention chung cho MỌI property kiểu decimal.
    ///
    /// ★ FIX — "No store type was specified for the decimal property ..." (69 warning).
    ///
    ///   SYMPTOM:
    ///     EF Core mặc định kiểu decimal là decimal(18,2) và cảnh báo mỗi property.
    ///     Trong DB, các cột tiền khai báo decimal(15,2), toạ độ decimal(10,7),
    ///     số lượng pod_items decimal(15,3)... Mặc định của EF KHÔNG khớp schema
    ///     ⇒ rủi ro sinh câu SQL với store type không tồn tại / sai làm tròn.
    ///
    ///   FIX:
    ///     Đặt mặc định (15,2) cho mọi property decimal — khớp đa số cột tiền.
    ///     Các cột đặc biệt được chỉ định tường minh trong SchemaMappings.cs
    ///     (HasPrecision) và sẽ ghi đè mặc định này:
    ///       - toạ độ GPS  : (10,7)  — delivery_pod, partners
    ///       - số lượng POD : (15,3)  — pod_items
    ///       - thuế suất   : (5,2)   — products.vat_rate
    ///       - % chiết khấu: (5,2)   — sales_order_items.discount_percent
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<decimal>().HavePrecision(15, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Database dùng snake_case (partner_code, created_at...) trong khi entity
        // dùng PascalCase (PartnerCode, CreatedAt...). Mapping tự động này là bắt buộc,
        // nếu thiếu thì mọi query sẽ lỗi "Invalid column name 'CreatedAt'".
        modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.GetColumnName() != null)
            .ToList()
            .ForEach(p => p.SetColumnName(ToSnakeCase(p.GetColumnName())));

        // Apply all configurations
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global query filter for soft-delete (nếu cần)
        // modelBuilder.ApplyGlobalFilters<ISoftDelete>(e => !e.IsDeleted);
    }

    /// <summary>
    /// Chuyển tên PascalCase sang snake_case: <c>PartnerCode</c> -> <c>partner_code</c>,
    /// <c>BaseUom</c> -> <c>base_uom</c>, <c>SysStartTime</c> -> <c>sys_start_time</c>.
    /// </summary>
    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (char.IsUpper(ch))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) ||
                              (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Auto-update timestamps
        var entries = ChangeTracker.Entries<BaseEntity>();
        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
