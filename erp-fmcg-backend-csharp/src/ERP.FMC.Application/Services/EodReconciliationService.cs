using ERP.FMC.Application.DTOs;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Application.Services;

// ===================================================================
// ★ PHASE 6B.4 — WIZARD ĐỐI SOÁT CUỐI NGÀY (EOD)
// -------------------------------------------------------------------
// Luồng nghiệp vụ:
//   1. Dispatcher gọi POST /api/sales/trips/{id}/eod với `actual_cash`
//      (tiền mặt tài xế thực tế nộp).
//   2. Server TỰ tính `expected_cash` = SUM(new_order_cash + old_debt_cash)
//      của các POD trong chuyến → KHÔNG tin client, tránh gian lận.
//   3. `cash_difference` = actual − expected (âm = thiếu).
//   4. Tạo item cho từng đơn trong chuyến.
//   5. Dispatcher duyệt: POST /api/sales/eod/{id}/approve → status = APPROVED.
//
// ★ CHECK constraint ở DB (6B.4 acceptance):
//   cash_variance_action chỉ nhận 3 giá trị hợp lệ.
// ===================================================================

public interface IEodReconciliationService
{
    /// <summary>Tạo phiếu đối soát từ chuyến (tự tính expected_cash từ POD).</summary>
    Task<EodReconciliationDto> CreateFromTripAsync(
        Guid tripId, CreateEodRequestDto request, Guid? createdBy, CancellationToken ct = default);

    /// <summary>Chi tiết phiếu đối soát + items.</summary>
    Task<EodReconciliationDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Duyệt phiếu (status = APPROVED), ghi approved_by từ JWT.</summary>
    Task<EodReconciliationDto> ApproveAsync(Guid id, Guid? approvedBy, CancellationToken ct = default);

    /// <summary>Danh sách phiếu đối soát (lọc theo ngày / tài xế / trạng thái).</summary>
    Task<IReadOnlyList<EodReconciliationDto>> ListAsync(
        DateTime? date, Guid? driverId, string? status, CancellationToken ct = default);
}

public class EodReconciliationService : IEodReconciliationService
{
    /// <summary>★ 6B.4: chỉ 3 hành động xử lý chênh lệch tiền mặt hợp lệ.</summary>
    private static readonly string[] ValidCashActions =
        { "DRIVER_REIMBURSE", "SUSPEND_DEBT", "WRITE_OFF" };

    /// <summary>★ 6B.4: chỉ 3 hành động xử lý chênh lệch hàng hóa hợp lệ.</summary>
    private static readonly string[] ValidItemsActions =
        { "DRIVER_COMPENSATE", "RETURN_TO_STOCK", "WRITE_OFF" };

    private readonly IUnitOfWork _uow;
    private readonly ILogger<EodReconciliationService> _logger;

    public EodReconciliationService(IUnitOfWork uow, ILogger<EodReconciliationService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    private static string Iso(DateTime dt) => dt.ToString("yyyy-MM-ddTHH:mm:ss");
    private static string IsoDate(DateTime dt) => dt.ToString("yyyy-MM-dd");

    // -------------------------------------------------------------------
    // Tạo phiếu đối soát
    // -------------------------------------------------------------------
    public async Task<EodReconciliationDto> CreateFromTripAsync(
        Guid tripId, CreateEodRequestDto request, Guid? createdBy, CancellationToken ct = default)
    {
        // --- Validate hành động xử lý chênh lệch ---
        if (!string.IsNullOrWhiteSpace(request.CashVarianceAction)
            && !ValidCashActions.Contains(request.CashVarianceAction.Trim().ToUpperInvariant()))
            throw new ArgumentException(
                $"cash_variance_action không hợp lệ. Chỉ chấp nhận: {string.Join(", ", ValidCashActions)}.");

        if (!string.IsNullOrWhiteSpace(request.ItemsVarianceAction)
            && !ValidItemsActions.Contains(request.ItemsVarianceAction.Trim().ToUpperInvariant()))
            throw new ArgumentException(
                $"items_variance_action không hợp lệ. Chỉ chấp nhận: {string.Join(", ", ValidItemsActions)}.");

        var trip = await _uow.DeliveryTrips.GetByIdAsync(tripId)
            ?? throw new KeyNotFoundException($"Không tìm thấy chuyến giao {tripId}");

        // --- Idempotency: 1 chuyến chỉ có 1 phiếu đối soát đang PENDING ---
        var existing = await _uow.EodReconciliations.GetAllAsync();
        var dup = existing.FirstOrDefault(r =>
            r.TripId == tripId && r.Status is "PENDING" or "APPROVED");
        if (dup is not null)
        {
            _logger.LogInformation(
                "EOD idempotent: trip={TripId} da co phieu {ReconId}",
                tripId, dup.Id);
            return await BuildDtoAsync(dup);
        }

        // --- Gom POD của chuyến ---
        var tripOrders = await _uow.DeliveryTripOrders.FindAsync(o => o.TripId == tripId);
        var tripOrderIds = tripOrders.Select(o => o.Id).ToHashSet();
        var tripOrderById = tripOrders.ToDictionary(o => o.Id);

        var pods = (await _uow.DeliveryPods.GetAllAsync())
            .Where(p => tripOrderIds.Contains(p.TripOrderId))
            .ToList();

        if (pods.Count == 0)
            throw new InvalidOperationException(
                "Chuyến chưa có POD nào — không thể tạo phiếu đối soát.");

        // ★ expected_cash do SERVER tính từ POD — không tin client.
        var expectedCash = pods.Sum(p => p.NewOrderCash + p.OldDebtCash);
        var actualCash = request.ActualCash;
        var cashDifference = actualCash - expectedCash;

        var partners = await _uow.Partners.GetAllAsync();
        var partnerMap = partners.ToDictionary(p => p.Id);
        var drivers = await _uow.UserAccounts.GetAllAsync();
        var driverMap = drivers.ToDictionary(d => d.Id);
        string? driverName = null;
        if (trip.DriverId.HasValue
            && driverMap.TryGetValue(trip.DriverId.Value, out var dr))
        {
            driverName = dr.FullName;
        }

        var recon = new EodReconciliation
        {
            TripId = tripId,
            ReconciliationDate = DateTime.Today,
            DriverId = trip.DriverId,
            DriverName = driverName,
            WarehouseId = trip.WarehouseId,
            ExpectedCash = expectedCash,
            ActualCash = actualCash,
            CashDifference = cashDifference,
            CashVarianceReason = request.CashVarianceReason,
            CashVarianceAction = Normalize(request.CashVarianceAction, ValidCashActions),
            ExpectedItems = 0,
            DeliveredItems = 0,
            ReturnedItems = 0,
            ItemsDifference = 0,
            ItemsVarianceReason = request.ItemsVarianceReason,
            ItemsVarianceAction = Normalize(request.ItemsVarianceAction, ValidItemsActions),
            Status = "PENDING",
            TotalOrders = tripOrders.Count,
            SuccessfulOrders = pods.Count(p => p.DeliveryStatus is "DELIVERED" or "PARTIAL"),
            FailedOrders = pods.Count(p => p.DeliveryStatus == "FAILED"),
            TripExpenses = request.TripExpenses,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };

        await _uow.EodReconciliations.AddAsync(recon);

        // --- Items: theo từng đơn trong chuyến ---
        var podItems = await _uow.PodItems.GetAllAsync();
        var podItemsByPod = podItems
            .GroupBy(pi => pi.PodId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var pod in pods)
        {
            var tripOrder = tripOrderById.TryGetValue(pod.TripOrderId, out var to) ? to : null;
            var items = podItemsByPod.TryGetValue(pod.Id, out var pi) ? pi : new List<PodItem>();

            var expectedQty = items.Sum(i => i.OrderedQty);
            var deliveredQty = items.Sum(i => i.DeliveredQty);
            var returnedQty = items.Sum(i => i.ReturnedQty);

            var podNewCash = pod.NewOrderCash;
            var podOldCash = pod.OldDebtCash;
            var itemCashDiff = podNewCash + podOldCash;

            recon.Items.Add(new EodReconciliationItem
            {
                ReconciliationId = recon.Id,
                SalesOrderId = pod.SalesOrderId,
                PartnerId = pod.PartnerId,
                PartnerName = partnerMap.TryGetValue(pod.PartnerId, out var p)
                    ? p.PartnerName : null,
                ExpectedCash = podNewCash + podOldCash,
                CollectedCash = podNewCash + podOldCash,
                OldDebtCollected = podOldCash,
                CashDifference = itemCashDiff,
                ExpectedQty = expectedQty,
                DeliveredQty = deliveredQty,
                ReturnedQty = returnedQty,
                ItemsDifference = deliveredQty + returnedQty - expectedQty,
                ReconciliationStatus = DetermineStatus(itemCashDiff, deliveredQty + returnedQty - expectedQty),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });

            recon.ExpectedItems += (int)expectedQty;
            recon.DeliveredItems += (int)deliveredQty;
            recon.ReturnedItems += (int)returnedQty;
        }

        recon.ItemsDifference = recon.DeliveredItems + recon.ReturnedItems - recon.ExpectedItems;

        await _uow.SaveChangesAsync();

        _logger.LogInformation(
            "EOD created: recon={ReconId} trip={TripNumber} expected_cash={Expected} actual={Actual} diff={Diff}",
            recon.Id, trip.TripNumber, expectedCash, actualCash, cashDifference);

        return await BuildDtoAsync(recon);
    }

    // -------------------------------------------------------------------
    // Chi tiết
    // -------------------------------------------------------------------
    public async Task<EodReconciliationDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var recon = await _uow.EodReconciliations.GetByIdAsync(id);
        if (recon is null) return null;
        return await BuildDtoAsync(recon);
    }

    // -------------------------------------------------------------------
    // Duyệt
    // -------------------------------------------------------------------
    public async Task<EodReconciliationDto> ApproveAsync(
        Guid id, Guid? approvedBy, CancellationToken ct = default)
    {
        var recon = await _uow.EodReconciliations.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy phiếu đối soát {id}");

        if (recon.Status == "APPROVED")
            return await BuildDtoAsync(recon);

        recon.Status = "APPROVED";
        recon.ApprovedBy = approvedBy;
        recon.ApprovedAt = DateTime.UtcNow;
        recon.UpdatedAt = DateTime.UtcNow;

        await _uow.SaveChangesAsync();

        _logger.LogInformation(
            "EOD approved: recon={ReconId} by={UserId}", recon.Id, approvedBy);

        return await BuildDtoAsync(recon);
    }

    // -------------------------------------------------------------------
    // Danh sách
    // -------------------------------------------------------------------
    public async Task<IReadOnlyList<EodReconciliationDto>> ListAsync(
        DateTime? date, Guid? driverId, string? status, CancellationToken ct = default)
    {
        var all = await _uow.EodReconciliations.GetAllAsync();

        var query = all.AsEnumerable();
        if (date.HasValue)
        {
            var d = date.Value.Date;
            query = query.Where(r => r.ReconciliationDate.Date == d);
        }
        if (driverId.HasValue)
            query = query.Where(r => r.DriverId == driverId.Value);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim().ToUpperInvariant();
            query = query.Where(r => string.Equals(r.Status, wanted, StringComparison.OrdinalIgnoreCase));
        }

        var list = query
            .OrderByDescending(r => r.ReconciliationDate)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();

        var result = new List<EodReconciliationDto>();
        foreach (var r in list)
            result.Add(await BuildDtoAsync(r));

        return result;
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private static string? Normalize(string? value, string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().ToUpperInvariant();
        return allowed.Contains(v) ? v : null;
    }

    /// <summary>
    /// Xác định trạng thái từng dòng đối soát.
    ///   - Chênh lệch tiền mặt âm  → CASH_SHORT
    ///   - Chênh lệch tiền mặt dương → CASH_OVER
    ///   - Chênh lệch hàng hóa ≠ 0  → ITEMS_SHORT
    ///   - Không chênh lệch gì      → FULL_MATCH
    /// </summary>
    private static string DetermineStatus(decimal cashDiff, decimal itemsDiff)
    {
        if (itemsDiff != 0) return "ITEMS_SHORT";
        if (cashDiff < 0) return "CASH_SHORT";
        if (cashDiff > 0) return "CASH_OVER";
        return "FULL_MATCH";
    }

    private async Task<EodReconciliationDto> BuildDtoAsync(EodReconciliation recon)
    {
        var items = (await _uow.EodReconciliationItems.GetAllAsync())
            .Where(i => i.ReconciliationId == recon.Id)
            .OrderBy(i => i.PartnerName)
            .ToList();

        return new EodReconciliationDto
        {
            Id = recon.Id.ToString(),
            TripId = recon.TripId?.ToString(),
            ReconciliationDate = IsoDate(recon.ReconciliationDate),
            DriverId = recon.DriverId?.ToString(),
            DriverName = recon.DriverName,
            WarehouseId = recon.WarehouseId?.ToString(),
            ExpectedCash = recon.ExpectedCash,
            ActualCash = recon.ActualCash,
            CashDifference = recon.CashDifference,
            CashVarianceReason = recon.CashVarianceReason,
            CashVarianceAction = recon.CashVarianceAction,
            ExpectedItems = recon.ExpectedItems,
            DeliveredItems = recon.DeliveredItems,
            ReturnedItems = recon.ReturnedItems,
            ItemsDifference = recon.ItemsDifference,
            ItemsVarianceReason = recon.ItemsVarianceReason,
            ItemsVarianceAction = recon.ItemsVarianceAction,
            Status = recon.Status,
            TotalOrders = recon.TotalOrders,
            SuccessfulOrders = recon.SuccessfulOrders,
            FailedOrders = recon.FailedOrders,
            TripExpenses = recon.TripExpenses,
            Notes = recon.Notes,
            ApprovedBy = recon.ApprovedBy?.ToString(),
            ApprovedAt = recon.ApprovedAt.HasValue ? Iso(recon.ApprovedAt.Value) : null,
            CreatedAt = Iso(recon.CreatedAt),
            Items = items.Select(i => new EodReconciliationItemDto
            {
                Id = i.Id.ToString(),
                ReconciliationId = i.ReconciliationId.ToString(),
                SalesOrderId = i.SalesOrderId?.ToString(),
                PartnerId = i.PartnerId?.ToString(),
                PartnerName = i.PartnerName,
                ExpectedCash = i.ExpectedCash,
                CollectedCash = i.CollectedCash,
                OldDebtCollected = i.OldDebtCollected,
                CashDifference = i.CashDifference,
                CashVarianceReason = i.CashVarianceReason,
                ExpectedQty = i.ExpectedQty,
                DeliveredQty = i.DeliveredQty,
                ReturnedQty = i.ReturnedQty,
                ItemsDifference = i.ItemsDifference,
                ReconciliationStatus = i.ReconciliationStatus,
            }).ToList(),
        };
    }
}
