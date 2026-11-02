using System.Linq.Expressions;

namespace ERP.FMC.Domain.Interfaces;

/// <summary>
/// Generic repository interface.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<T>> GetAllAsync();
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null);
    Task<IReadOnlyList<T>> GetPagedAsync(int skip, int take, Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null);
    Task<T> AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate);
}

/// <summary>
/// Unit of Work pattern.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    IRepository<Entities.Partner> Partners { get; }
    IRepository<Entities.Product> Products { get; }
    IRepository<Entities.Category> Categories { get; }
    IRepository<Entities.Route> Routes { get; }
    IRepository<Entities.UserAccount> UserAccounts { get; }
    IRepository<Entities.Warehouse> Warehouses { get; }
    IRepository<Entities.SalesOrder> SalesOrders { get; }
    IRepository<Entities.SalesOrderItem> SalesOrderItems { get; }
    IRepository<Entities.StockLedger> StockLedgers { get; }
    IRepository<Entities.InventoryBalance> InventoryBalances { get; }
    IRepository<Entities.PriceList> PriceLists { get; }
    IRepository<Entities.ImportJob> ImportJobs { get; }
    IRepository<Entities.ImportStaging> ImportStaging { get; }
    IRepository<Entities.ImportError> ImportErrors { get; }
    IRepository<Entities.ImportFieldMapping> ImportFieldMappings { get; }
    IRepository<Entities.AuditLog> AuditLogs { get; }
    // nghiệp vụ giao hàng
    IRepository<Entities.DeliveryTrip> DeliveryTrips { get; }
    IRepository<Entities.DeliveryTripOrder> DeliveryTripOrders { get; }
    IRepository<Entities.DeliveryPod> DeliveryPods { get; }
    IRepository<Entities.PodItem> PodItems { get; }
    IRepository<Entities.PodAttachment> PodAttachments { get; }
    // đối soát cuối ngày
    IRepository<Entities.EodReconciliation> EodReconciliations { get; }
    IRepository<Entities.EodReconciliationItem> EodReconciliationItems { get; }
    Task<int> SaveChangesAsync();

    /// <summary>
    /// Thực hiện một khối thao tác trong transaction, TƯƠNG THÍCH với
    /// <c>SqlServerRetryingExecutionStrategy</c>.
    ///
    /// VÌ SAO CẦN HÀM NÀY:
    ///   EF Core cấu hình <c>EnableRetryOnFailure</c> sẽ dùng execution strategy
    ///   KHÔNG cho phép user khởi tạo transaction thủ công. Mọi truy vấn/thao tác
    ///   chạy BÊN TRONG một transaction đã mở thủ công đều ném:
    ///     "The configured execution strategy 'SqlServerRetryingExecutionStrategy'
    ///      does not support user-initiated transactions."
    ///   ⇒ Mọi luồng ghi dữ liệu (tạo đơn hàng, commit import, ghi POD, đối soát EOD)
    ///     đều lỗi 500.
    ///
    /// CÁCH SỬA ĐÚNG: để EF quản lý cả transaction lẫn retry bằng cách gọi
    /// <c>CreateExecutionStrategy().ExecuteAsync(...)</c> bao quanh TOÀN BỘ
    /// khối (mở tx -&gt; thao tác -&gt; commit). Khi gặp lỗi tạm thời, EF tự
    /// rollback và chạy lại cả khối ⇒ vẫn ATOMIC.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct = default);

    Task BeginTransactionAsync();
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
}
