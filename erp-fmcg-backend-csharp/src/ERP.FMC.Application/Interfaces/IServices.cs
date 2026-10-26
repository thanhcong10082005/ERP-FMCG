using ERP.FMC.Application.DTOs;

namespace ERP.FMC.Application.Interfaces;

public interface IPartnerService
{
    Task<PagedResult<PartnerDto>> GetPagedAsync(PartnerFilterDto filter, CancellationToken ct = default);
    Task<PartnerDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PartnerDto?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<PartnerDto> CreateAsync(CreatePartnerDto dto, CancellationToken ct = default);
    Task<PartnerDto?> UpdateAsync(Guid id, UpdatePartnerDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IProductService
{
    Task<PagedResult<ProductDto>> GetPagedAsync(ProductFilterDto filter, CancellationToken ct = default);
    Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProductDto?> GetBySkuAsync(string sku, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken ct = default);
    Task<ProductDto?> UpdateAsync(Guid id, UpdateProductDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct = default);
    Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(CategoryDto dto, CancellationToken ct = default);
    Task<CategoryDto?> UpdateAsync(Guid id, CategoryDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IRouteService
{
    Task<IReadOnlyList<RouteDto>> GetAllAsync(CancellationToken ct = default);
    Task<RouteDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RouteDto> CreateAsync(CreateRouteDto dto, CancellationToken ct = default);
    Task<RouteDto?> UpdateAsync(Guid id, CreateRouteDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IWarehouseService
{
    Task<IReadOnlyList<WarehouseDto>> GetAllAsync(CancellationToken ct = default);
    Task<WarehouseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WarehouseDto> CreateAsync(CreateWarehouseDto dto, CancellationToken ct = default);
    Task<WarehouseDto?> UpdateAsync(Guid id, CreateWarehouseDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface ISalesOrderService
{
    Task<PagedResult<SalesOrderDto>> GetPagedAsync(SalesOrderFilterDto filter, CancellationToken ct = default);
    Task<SalesOrderDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SalesOrderDto?> GetByNumberAsync(string orderNumber, CancellationToken ct = default);
    Task<SalesOrderDto> CreateAsync(CreateSalesOrderDto dto, CancellationToken ct = default);
    Task<SalesOrderDto?> UpdateAsync(Guid id, UpdateSalesOrderDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<SalesOrderDto?> AddItemAsync(Guid orderId, CreateSalesOrderItemDto dto, CancellationToken ct = default);
    Task<bool> RemoveItemAsync(Guid orderId, Guid itemId, CancellationToken ct = default);
    Task<bool> MarkAsConfirmedAsync(Guid id, CancellationToken ct = default);
    Task<bool> MarkAsDeliveredAsync(Guid id, CancellationToken ct = default);
}

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryBalanceDto>> GetBalancesAsync(Guid? warehouseId, Guid? productId, CancellationToken ct = default);
    Task<IReadOnlyList<StockLedgerDto>> GetLedgerAsync(Guid? warehouseId, Guid? productId, DateTime? from, DateTime? to, CancellationToken ct = default);
}

public interface IPriceListService
{
    Task<IReadOnlyList<PriceListDto>> GetAllAsync(CancellationToken ct = default);
    Task<PriceListDto?> GetByProductAsync(Guid productId, CancellationToken ct = default);
    Task<PriceListDto> CreateAsync(PriceListDto dto, CancellationToken ct = default);
    Task<PriceListDto?> UpdateAsync(Guid id, PriceListDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IImportService
{
    Task<Guid> CreateJobAsync(string importType, string fileName, Stream fileStream,
        string errorHandling = "SKIP", Guid? uploadedBy = null, CancellationToken ct = default);
    Task ProcessImportAsync(Guid jobId, CancellationToken ct = default);
    Task<int> CommitImportAsync(Guid jobId, CancellationToken ct = default);
    Task<bool> RollbackImportAsync(Guid jobId, CancellationToken ct = default);

    // Staging API
    Task<PagedResult<ImportJobDto>> GetJobsAsync(ImportJobFilterDto filter, CancellationToken ct = default);
    Task<ImportJobDto?> GetJobAsync(Guid jobId, CancellationToken ct = default);
    Task<ImportJobDto> UploadAndProcessAsync(string importType, string fileName, Stream fileStream,
        string errorHandling = "SKIP", CancellationToken ct = default);
    Task<List<ImportStagingPreviewDto>> PreviewAsync(Guid jobId, int take = 10, CancellationToken ct = default);
    Task<ImportErrorSummaryDto> GetErrorSummaryAsync(Guid jobId, CancellationToken ct = default);
    Task<int> ResolveErrorsAsync(Guid jobId, List<ErrorResolutionDto> resolutions, CancellationToken ct = default);

    // Phase 4: Enhanced staging
    Task<List<ImportHistoryDto>> GetImportHistoryAsync(string? importType = null, int limit = 50, CancellationToken ct = default);
    Task<BatchResolveResultDto> BatchResolveErrorsAsync(Guid jobId, BatchResolveRequestDto request, CancellationToken ct = default);
    Task<Guid> ReImportAsync(ReImportRequestDto request, CancellationToken ct = default);
    Task<UndoResultDto> UndoImportAsync(UndoRequestDto request, CancellationToken ct = default);
}

public interface IAuditService
{
    Task LogAsync(
        string tableName, Guid recordId, string action,
        object? oldValues = null, object? newValues = null,
        string? operation = null, Guid? userId = null, string? userName = null,
        string source = "API", string? requestId = null, string? ipAddress = null,
        string? notes = null, CancellationToken ct = default);

    Task<PagedResult<AuditLogDto>> GetLogsAsync(AuditLogFilterDto filter, CancellationToken ct = default);
    Task<List<AuditLogDto>> GetRecordHistoryAsync(string tableName, Guid recordId, CancellationToken ct = default);
    Task<HistoryComparisonDto?> CompareAsync(string tableName, Guid recordId, DateTime fromTime, DateTime toTime, CancellationToken ct = default);
    Task<Dictionary<string, object>> GetStatsAsync(CancellationToken ct = default);
}

public interface ITemporalHistoryService
{
    Task<TemporalEntityHistoryDto?> GetEntityHistoryAsync(string tableName, Guid recordId,
        DateTime? fromTime = null, DateTime? toTime = null, CancellationToken ct = default);
    Task<List<T>> GetAsOfAsync<T>(string tableName, DateTime asOf,
        string? whereClause = null, CancellationToken ct = default) where T : class;
    Task<HistoryComparisonDto> CompareVersionsAsync(string tableName, Guid recordId,
        DateTime fromTime, DateTime toTime, CancellationToken ct = default);
    Task<bool> RestoreToAsync(string tableName, Guid recordId, DateTime targetTime,
        Guid? userId = null, string? userName = null, CancellationToken ct = default);
    IReadOnlyList<string> GetTemporalTables();
}

// Phase 5 interfaces - implemented in Infrastructure layer
public interface IUatTestService
{
    Task<TestSuiteRunDto> RunAllAsync(CancellationToken ct = default);
}

public interface ISmokeTestService
{
    Task<SmokeTestSuiteDto> RunAsync(CancellationToken ct = default);
}

public interface IGoLiveService
{
    GoLiveStatusDto GetStatus();
    Task<bool> LockNestJsAsync(CancellationToken ct = default);
    Task<MigrationResultDto> RunFinalMigrationAsync(CancellationToken ct = default);
    Task<bool> RunSmokeTestAsync(SmokeTestSuiteDto smokeResults, CancellationToken ct = default);
    Task<bool> SwitchDnsAsync(CancellationToken ct = default);
    Task<string> SendAnnouncementAsync(CancellationToken ct = default);
    Task<GoLiveReportDto> GenerateReportAsync(CancellationToken ct = default);
    Task<HealthCheckDto> HealthCheckAsync(CancellationToken ct = default);
}

public interface IMonitoringService
{
    Task<Dictionary<string, object>> GetMetricsAsync(CancellationToken ct = default);
    void RecordMetric(string name, double value, string unit = "");
    List<MonitoringMetricDto> GetRecentMetrics(int count = 100);
    void LogError(string source, string message, Exception? ex = null);
    Guid CreateTicket(string title, string description, string severity,
        string? userEmail = null, string? endpoint = null, string? errorTrace = null);
    List<SupportTicketDto> GetTickets(string? status = null, string? severity = null);
    bool ResolveTicket(Guid ticketId, string resolutionNotes);
}

// DTOs for Phase 5 services
public class MigrationResultDto
{
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public int PartnerCount { get; set; }
    public int ProductCount { get; set; }
    public int OrderCount { get; set; }
    public int OrderItemCount { get; set; }
    public List<string> TemporalTablesEnabled { get; set; } = new();
    public bool AuditLogsReady { get; set; }
    public string? Error { get; set; }
}

public class GoLiveReportDto
{
    public DateTime GeneratedAt { get; set; }
    public string Phase { get; set; } = string.Empty;
    public DateTime? StartTime { get; set; }
    public Dictionary<string, object> Statistics { get; set; } = new();
    public int TotalErrors { get; set; }
    public int SuccessfulOperations { get; set; }
    public TimeSpan? TotalDowntime { get; set; }
}
