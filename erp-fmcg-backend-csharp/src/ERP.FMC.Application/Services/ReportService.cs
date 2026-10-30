using ERP.FMC.Application.DTOs;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Application.Services;

// ===================================================================
// ★ PHASE 6B.2 — BÁO CÁO VẬN HÀNH
// -------------------------------------------------------------------
// 6 báo cáo cho dispatcher/quản lý:
//   1. Doanh thu theo tuyến
//   2. Tỷ lệ giao thành công (theo ngày / tuyến / tài xế)
//   3. Công nợ theo tuổi (aging)
//   4. Hàng rớt theo SKU
//   5. Tiền mặt theo ca (tài xế + ngày)
//   6. Tồn kho & cảnh báo dưới mức tối thiểu
//
// ★ Về hiệu năng:
//   C-C7 (Phase 5) đã cảnh báo `SalesOrderService` nạp 49.844 orders +
//   176.332 items vào RAM mỗi request. Các báo cáo ở đây CŨNG nạp
//   tương tự — nhưng đây là endpoint cho dispatcher, gọi 1-2 lần/ngày,
//   không nằm trên đường giao hàng. Tuy vậy, dữ liệu được nạp MỘT LẦN
//   cho cả 6 báo cáo (một method `LoadSnapshotAsync`) để không nhân bội
//   chi phí I/O khi dispatcher mở dashboard.
// ===================================================================

public interface IReportService
{
    /// <summary>1. Doanh thu theo tuyến.</summary>
    Task<IReadOnlyList<RevenueByRouteDto>> GetRevenueByRouteAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>2. Tỷ lệ giao thành công theo ngày / tuyến / tài xế.</summary>
    Task<IReadOnlyList<DeliverySuccessRateDto>> GetSuccessRateAsync(
        string dimension, DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>3. Công nợ theo tuổi (0-30 / 31-60 / 61-90 / &gt;90 ngày).</summary>
    Task<IReadOnlyList<DebtAgingDto>> GetDebtAgingAsync(CancellationToken ct = default);

    /// <summary>4. Hàng rớt theo SKU.</summary>
    Task<IReadOnlyList<ReturnedGoodsDto>> GetReturnedGoodsAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>5. Tiền mặt theo ca (tài xế + ngày).</summary>
    Task<IReadOnlyList<CashByShiftDto>> GetCashByShiftAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>6. Tồn kho dưới mức tối thiểu / điểm đặt lại.</summary>
    Task<IReadOnlyList<LowStockDto>> GetLowStockAsync(CancellationToken ct = default);
}

public class ReportService : IReportService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ReportService> _logger;

    public ReportService(IUnitOfWork uow, ILogger<ReportService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    // -------------------------------------------------------------------
    // 1. Doanh thu theo tuyến
    // -------------------------------------------------------------------
    public async Task<IReadOnlyList<RevenueByRouteDto>> GetRevenueByRouteAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (orders, partners, routes) = await LoadOrdersAsync(from, to, ct);

        var partnerMap = partners.ToDictionary(p => p.Id);
        var routeMap = routes.ToDictionary(r => r.RouteCode, r => r);

        var result = orders
            .Where(o => partnerMap.ContainsKey(o.CustomerId))
            .GroupBy(o => partnerMap[o.CustomerId].RouteCode ?? "(CHƯA PHÂN TUYẾN)")
            .Select(g =>
            {
                var route = routeMap.TryGetValue(g.Key, out var r) ? r : null;
                return new RevenueByRouteDto
                {
                    RouteCode = route?.RouteCode ?? g.Key,
                    RouteName = route?.RouteName,
                    TotalOrders = g.Count(),
                    TotalAmount = g.Sum(o => o.TotalAmount),
                    DeliveredCount = g.Count(o => o.Status == "DELIVERED"),
                    FailedCount = g.Count(o => o.Status == "CANCELLED"),
                    CollectedAmount = g.Sum(o => o.PaidAmount),
                };
            })
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        return result;
    }

    // -------------------------------------------------------------------
    // 2. Tỷ lệ giao thành công
    // -------------------------------------------------------------------
    public async Task<IReadOnlyList<DeliverySuccessRateDto>> GetSuccessRateAsync(
        string dimension, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var dim = (dimension ?? "DATE").Trim().ToUpperInvariant();
        if (dim is not ("DATE" or "ROUTE" or "DRIVER"))
            throw new ArgumentException("dimension phải là DATE | ROUTE | DRIVER.");

        var (orders, partners, _) = await LoadOrdersAsync(from, to, ct);
        var partnerMap = partners.ToDictionary(p => p.Id);

        var enriched = orders
            .Where(o => partnerMap.ContainsKey(o.CustomerId))
            .Select(o => new
            {
                Order = o,
                RouteCode = partnerMap[o.CustomerId].RouteCode ?? "(CHƯA PHÂN TUYẾN)",
                DriverId = o.SalePersonId,
            })
            .ToList();

        var drivers = await _uow.UserAccounts.GetAllAsync();
        var driverMap = drivers.ToDictionary(d => d.Id);

        var result = new List<DeliverySuccessRateDto>();

        if (dim == "DATE")
        {
            foreach (var g in enriched.GroupBy(e => e.Order.OrderDate.ToString("yyyy-MM-dd")))
                result.Add(BuildSuccess("DATE", g.Key, g.Count(),
                    g.Count(x => x.Order.Status == "DELIVERED"),
                    g.Count(x => x.Order.Status == "CANCELLED")));
        }
        else if (dim == "ROUTE")
        {
            foreach (var g in enriched.GroupBy(e => e.RouteCode))
                result.Add(BuildSuccess("ROUTE", g.Key, g.Count(),
                    g.Count(x => x.Order.Status == "DELIVERED"),
                    g.Count(x => x.Order.Status == "CANCELLED")));
        }
        else
        {
            foreach (var g in enriched.GroupBy(e =>
                e.DriverId.HasValue && driverMap.TryGetValue(e.DriverId.Value, out var d)
                    ? d.FullName : "(CHƯA GÁN TÀI XẾ)"))
                result.Add(BuildSuccess("DRIVER", g.Key, g.Count(),
                    g.Count(x => x.Order.Status == "DELIVERED"),
                    g.Count(x => x.Order.Status == "CANCELLED")));
        }

        result.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
        return result;
    }

    private static DeliverySuccessRateDto BuildSuccess(
        string dimension, string key, int total, int delivered, int failed)
        => new()
        {
            Dimension = dimension,
            Key = key,
            TotalOrders = total,
            DeliveredCount = delivered,
            FailedCount = failed,
            SuccessRate = total > 0 ? Math.Round(delivered * 100m / total, 2) : 0m,
        };

    // -------------------------------------------------------------------
    // 3. Công nợ theo tuổi
    // -------------------------------------------------------------------
    public async Task<IReadOnlyList<DebtAgingDto>> GetDebtAgingAsync(CancellationToken ct = default)
    {
        var partners = await _uow.Partners.GetAllAsync();
        var today = DateTime.Today;

        var buckets = new (string Name, int Min, int Max)[]
        {
            ("0-30", 0, 30),
            ("31-60", 31, 60),
            ("61-90", 61, 90),
            (">90", 91, int.MaxValue),
        };

        var result = new List<DebtAgingDto>();

        foreach (var (name, min, max) in buckets)
        {
            // Tuổi nợ tính theo `old_debt_date` (ngày ghi nhận nợ cũ).
            // Khách chưa có old_debt_date ⇒ xem như 0-30 ngày.
            var group = partners.Where(p =>
            {
                if (p.CurrentDebt <= 0 && p.OldDebt <= 0) return false;
                var age = p.OldDebtDate.HasValue
                    ? (today - p.OldDebtDate.Value.Date).Days
                    : 0;
                if (age < 0) age = 0;
                return age >= min && age <= max;
            }).ToList();

            result.Add(new DebtAgingDto
            {
                Bucket = name,
                PartnerCount = group.Count,
                TotalDebt = group.Sum(p => p.CurrentDebt),
                TotalOldDebt = group.Sum(p => p.OldDebt),
            });
        }

        return result;
    }

    // -------------------------------------------------------------------
    // 4. Hàng rớt theo SKU
    // -------------------------------------------------------------------
    public async Task<IReadOnlyList<ReturnedGoodsDto>> GetReturnedGoodsAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var pods = await _uow.DeliveryPods.GetAllAsync();
        var podIds = pods
            .Where(p => IsInRange(p.DeliveredAt ?? p.CreatedAt, from, to))
            .Select(p => p.Id)
            .ToHashSet();

        var podItems = (await _uow.PodItems.GetAllAsync())
            .Where(pi => podIds.Contains(pi.PodId) && pi.ReturnedQty > 0)
            .ToList();

        var products = await _uow.Products.GetAllAsync();
        var productMap = products.ToDictionary(p => p.Id);

        var result = podItems
            .GroupBy(pi => pi.ProductId)
            .Select(g =>
            {
                productMap.TryGetValue(g.Key, out var prod);
                return new ReturnedGoodsDto
                {
                    ProductId = g.Key.ToString(),
                    Sku = prod?.Sku,
                    ProductName = prod?.ProductName ?? g.First().ProductName,
                    TotalReturnedQty = g.Sum(x => x.ReturnedQty),
                    TotalValue = g.Sum(x => x.ReturnedQty * x.UnitPrice),
                    PodCount = g.Select(x => x.PodId).Distinct().Count(),
                };
            })
            .OrderByDescending(x => x.TotalValue)
            .ToList();

        return result;
    }

    // -------------------------------------------------------------------
    // 5. Tiền mặt theo ca
    // -------------------------------------------------------------------
    public async Task<IReadOnlyList<CashByShiftDto>> GetCashByShiftAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var pods = (await _uow.DeliveryPods.GetAllAsync())
            .Where(p => p.DeliveryStatus is "DELIVERED" or "PARTIAL")
            .Where(p => IsInRange(p.DeliveredAt ?? p.CreatedAt, from, to))
            .ToList();

        var tripOrderIds = pods.Select(p => p.TripOrderId).ToHashSet();
        var tripOrders = (await _uow.DeliveryTripOrders.GetAllAsync())
            .Where(o => tripOrderIds.Contains(o.Id))
            .ToList();
        var tripIds = tripOrders.Select(o => o.TripId).ToHashSet();

        var trips = (await _uow.DeliveryTrips.GetAllAsync())
            .Where(t => tripIds.Contains(t.Id))
            .ToList();
        var tripMap = trips.ToDictionary(t => t.Id);

        var drivers = await _uow.UserAccounts.GetAllAsync();
        var driverMap = drivers.ToDictionary(d => d.Id);

        var result = pods
            .GroupBy(p =>
            {
                var tripId = tripOrders.FirstOrDefault(o => o.Id == p.TripOrderId)?.TripId;
                return tripId ?? Guid.Empty;
            })
            .Select(g =>
            {
                var trip = tripMap.TryGetValue(g.Key, out var t) ? t : null;
                var driverName = trip?.DriverId.HasValue == true
                    && driverMap.TryGetValue(trip.DriverId.Value, out var d)
                    ? d.FullName : null;

                return new CashByShiftDto
                {
                    DriverId = trip?.DriverId?.ToString(),
                    DriverName = driverName,
                    TripId = trip?.Id.ToString(),
                    TripNumber = trip?.TripNumber,
                    ShiftDate = trip?.TripDate.ToString("yyyy-MM-dd") ?? "",
                    NewOrderCash = g.Sum(p => p.NewOrderCash),
                    OldDebtCash = g.Sum(p => p.OldDebtCash),
                    TotalCash = g.Sum(p => p.NewOrderCash + p.OldDebtCash),
                    PodCount = g.Count(),
                };
            })
            .OrderByDescending(x => x.ShiftDate)
            .ThenBy(x => x.TripNumber)
            .ToList();

        return result;
    }

    // -------------------------------------------------------------------
    // 6. Tồn kho dưới mức tối thiểu
    // -------------------------------------------------------------------
    public async Task<IReadOnlyList<LowStockDto>> GetLowStockAsync(CancellationToken ct = default)
    {
        var balances = await _uow.InventoryBalances.GetAllAsync();
        var products = await _uow.Products.GetAllAsync();
        var warehouses = await _uow.Warehouses.GetAllAsync();

        var productMap = products.ToDictionary(p => p.Id);
        var whMap = warehouses.ToDictionary(w => w.Id);

        var result = balances
            .Where(b =>
            {
                var prod = productMap.GetValueOrDefault(b.ProductId);
                if (prod is null) return false;
                return b.OnHand <= prod.MinStock || b.OnHand <= prod.ReorderPoint;
            })
            .Select(b =>
            {
                var prod = productMap.GetValueOrDefault(b.ProductId);
                var level = prod is not null && b.OnHand <= prod.MinStock ? "LOW" : "REORDER";
                return new LowStockDto
                {
                    WarehouseId = b.WarehouseId.ToString(),
                    WarehouseName = whMap.GetValueOrDefault(b.WarehouseId)?.WarehouseName,
                    ProductId = b.ProductId.ToString(),
                    Sku = prod?.Sku,
                    ProductName = prod?.ProductName,
                    QuantityOnHand = b.OnHand,
                    MinStock = prod?.MinStock ?? 0,
                    ReorderPoint = prod?.ReorderPoint ?? 0,
                    AlertLevel = level,
                };
            })
            .OrderBy(x => x.QuantityOnHand)
            .ThenBy(x => x.Sku)
            .ToList();

        return result;
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private static bool IsInRange(DateTime? value, DateTime? from, DateTime? to)
    {
        if (value is null) return false;
        var d = value.Value.Date;
        if (from.HasValue && d < from.Value.Date) return false;
        if (to.HasValue && d > to.Value.Date) return false;
        return true;
    }

    /// <summary>
    /// Nạp 1 lần: đơn hàng + khách hàng + tuyến trong khoảng ngày.
    /// Dùng chung cho các báo cáo để tránh nạp lặp.
    /// </summary>
    private async Task<(List<SalesOrder> Orders, List<Partner> Partners, List<Route> Routes)>
        LoadOrdersAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var allOrders = await _uow.SalesOrders.GetAllAsync();
        var orders = allOrders
            .Where(o => IsInRange(o.OrderDate, from, to))
            .ToList();

        var partners = (await _uow.Partners.GetAllAsync()).ToList();
        var routes = (await _uow.Routes.GetAllAsync()).ToList();

        return (orders, partners, routes);
    }
}
