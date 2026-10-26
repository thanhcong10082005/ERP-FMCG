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
    Task<int> SaveChangesAsync();
    Task BeginTransactionAsync();
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
}
