using System.Linq.Expressions;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.FMC.Infrastructure.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    protected readonly Data.AppDbContext Context;
    protected readonly DbSet<T> DbSet;

    public Repository(Data.AppDbContext context)
    {
        Context = context;
        DbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id) => await DbSet.FindAsync(id);

    public async Task<IReadOnlyList<T>> GetAllAsync() => await Dbset().ToListAsync();

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate) =>
        await Dbset().Where(predicate).ToListAsync();

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate) =>
        await Dbset().FirstOrDefaultAsync(predicate);

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null) =>
        predicate == null ? await DbSet.CountAsync() : await DbSet.CountAsync(predicate);

    public async Task<IReadOnlyList<T>> GetPagedAsync(int skip, int take,
        Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null)
    {
        IQueryable<T> query = DbSet;
        if (predicate != null) query = query.Where(predicate);
        if (orderBy != null) query = orderBy(query);
        return await query.Skip(skip).Take(take).ToListAsync();
    }

    public async Task<T> AddAsync(T entity)
    {
        await DbSet.AddAsync(entity);
        return entity;
    }

    public Task UpdateAsync(T entity)
    {
        DbSet.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity)
    {
        DbSet.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate) =>
        await DbSet.AnyAsync(predicate);

    private IQueryable<T> Dbset() => DbSet.AsNoTracking();
}

public class UnitOfWork : IUnitOfWork
{
    private readonly Data.AppDbContext _context;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(Data.AppDbContext context)
    {
        _context = context;
        Partners = new Repository<Partner>(_context);
        Products = new Repository<Product>(_context);
        Categories = new Repository<Category>(_context);
        Routes = new Repository<Route>(_context);
        UserAccounts = new Repository<UserAccount>(_context);
        Warehouses = new Repository<Warehouse>(_context);
        SalesOrders = new Repository<SalesOrder>(_context);
        SalesOrderItems = new Repository<SalesOrderItem>(_context);
        StockLedgers = new Repository<StockLedger>(_context);
        InventoryBalances = new Repository<InventoryBalance>(_context);
        PriceLists = new Repository<PriceList>(_context);
        ImportJobs = new Repository<ImportJob>(_context);
        ImportStaging = new Repository<ImportStaging>(_context);
        ImportErrors = new Repository<ImportError>(_context);
        ImportFieldMappings = new Repository<ImportFieldMapping>(_context);
        AuditLogs = new Repository<AuditLog>(_context);
        DeliveryTrips = new Repository<DeliveryTrip>(_context);
        DeliveryTripOrders = new Repository<DeliveryTripOrder>(_context);
        DeliveryPods = new Repository<DeliveryPod>(_context);
        PodItems = new Repository<PodItem>(_context);
        PodAttachments = new Repository<PodAttachment>(_context);
        // đối soát cuối ngày
        EodReconciliations = new Repository<EodReconciliation>(_context);
        EodReconciliationItems = new Repository<EodReconciliationItem>(_context);
    }

    public IRepository<Partner> Partners { get; }
    public IRepository<Product> Products { get; }
    public IRepository<Category> Categories { get; }
    public IRepository<Route> Routes { get; }
    public IRepository<UserAccount> UserAccounts { get; }
    public IRepository<Warehouse> Warehouses { get; }
    public IRepository<SalesOrder> SalesOrders { get; }
    public IRepository<SalesOrderItem> SalesOrderItems { get; }
    public IRepository<StockLedger> StockLedgers { get; }
    public IRepository<InventoryBalance> InventoryBalances { get; }
    public IRepository<PriceList> PriceLists { get; }
    public IRepository<ImportJob> ImportJobs { get; }
    public IRepository<AuditLog> AuditLogs { get; }
    public IRepository<ImportStaging> ImportStaging { get; }
    public IRepository<ImportError> ImportErrors { get; }
    public IRepository<ImportFieldMapping> ImportFieldMappings { get; }
    // nghiệp vụ giao hàng
    public IRepository<DeliveryTrip> DeliveryTrips { get; }
    public IRepository<DeliveryTripOrder> DeliveryTripOrders { get; }
    public IRepository<DeliveryPod> DeliveryPods { get; }
    public IRepository<PodItem> PodItems { get; }
    public IRepository<PodAttachment> PodAttachments { get; }
    // đối soát cuối ngày
    public IRepository<EodReconciliation> EodReconciliations { get; }
    public IRepository<EodReconciliationItem> EodReconciliationItems { get; }

    public Task<int> SaveChangesAsync() => _context.SaveChangesAsync();

    /// <inheritdoc />
    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        // EF Core 8: IExecutionStrategy.ExecuteAsync<TState, TResult> yêu cầu
        // TState + verifySucceeded. Dùng overload không kiểm tra (null) để mọi
        // lỗi đều được retry theo chính sách của strategy.
        return await strategy.ExecuteAsync<object?, T>(
            state: null,
            operation: async (_, _, token) =>
            {
                // Nếu đã có transaction mở sẵn (do code cũ gọi BeginTransactionAsync)
                // thì dùng transaction đó, không mở thêm.
                var ownsTransaction = _context.Database.CurrentTransaction == null;
                var tx = ownsTransaction
                    ? await _context.Database.BeginTransactionAsync(token)
                    : _context.Database.CurrentTransaction!;

                try
                {
                    var result = await operation();
                    await tx.CommitAsync(token);
                    return result;
                }
                catch
                {
                    try { await tx.RollbackAsync(token); } catch { /* lỗi rollback không che lỗi gốc */ }
                    throw;
                }
                finally
                {
                    if (ownsTransaction)
                    {
                        await tx.DisposeAsync();
                    }
                }
            },
            verifySucceeded: null,
            cancellationToken: ct);
    }

    /// <summary>
    /// Mở transaction theo cách tương thích với <c>SqlServerRetryingExecutionStrategy</c>.
    ///
    /// BUG ĐÃ SỬA: khi DbContext được cấu hình <c>EnableRetryOnFailure</c>, EF Core dùng
    /// execution strategy KHÔNG cho phép user khởi tạo transaction thủ công
    /// (<c>BeginTransactionAsync</c> ném InvalidOperationException:
    /// "The configured execution strategy ... does not support user-initiated transactions").
    /// Hệ quả: MỌI thao tác ghi dữ liệu bọc trong transaction (tạo đơn hàng, commit
    /// import, ghi POD...) đều lỗi 500 ⇒ nghiệp vụ giao hàng không dùng được.
    ///
    /// ƯU TIÊN: dùng <c>ExecuteInTransactionAsync</c> để bọc trọn khối (bắt buộc về
    /// mặt ngữ nghĩa retry). Hàm này giữ lại để các luồng cũ vẫn chạy được.
    /// </summary>
    public async Task BeginTransactionAsync()
    {
        // Nếu đã có transaction đang mở thì không mở thêm.
        if (_context.Database.CurrentTransaction != null) return;

        var strategy = _context.Database.CreateExecutionStrategy();
        _transaction = await strategy.ExecuteAsync(async () =>
        {
            // Nếu strategy retry, transaction cũ (đã lỗi) phải được dispose
            // trước khi mở cái mới.
            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
            return await _context.Database.BeginTransactionAsync();
        });
    }

    public async Task CommitTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
