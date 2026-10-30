using ERP.FMC.Application.DTOs;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Application.Services;

// ===================================================================
// DỊCH VỤ NGHIỆP VỤ GIAO HÀNG
// ===================================================================

/// <summary>
/// Nghiệp vụ chuyến giao hàng cho Driver App.
/// </summary>
public interface IDeliveryTripService
{
    Task<IReadOnlyList<DeliveryTripDto>> GetTripsAsync(Guid? driverId, DateTime? date, string? status = null, CancellationToken ct = default);
    Task<DeliveryTripDto> GetTripAsync(Guid tripId, CancellationToken ct = default);
    Task<DeliveryTripDto> StartTripAsync(Guid tripId, Guid? driverId, CancellationToken ct = default);
    Task<EodReportDto> CompleteTripAsync(Guid tripId, Guid? driverId, CancellationToken ct = default);
    Task<IReadOnlyList<DriverPartnerDto>> GetMasterPartnersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DriverProductDto>> GetMasterProductsAsync(CancellationToken ct = default);
}

public class DeliveryTripService : IDeliveryTripService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DeliveryTripService> _logger;

    public DeliveryTripService(IUnitOfWork uow, ILogger<DeliveryTripService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    private static string Iso(DateTime dt) => dt.ToString("yyyy-MM-ddTHH:mm:ss");

    private static string IsoDate(DateTime dt) => dt.ToString("yyyy-MM-dd");

    // -------------------------------------------------------------------
    // E1 — Danh sách chuyến giao
    // -------------------------------------------------------------------
    public async Task<IReadOnlyList<DeliveryTripDto>> GetTripsAsync(
        Guid? driverId, DateTime? date, string? status = null, CancellationToken ct = default)
    {
        var all = await _uow.DeliveryTrips.GetAllAsync();

        var query = all.AsEnumerable();
        if (driverId.HasValue)
            query = query.Where(t => t.DriverId == driverId.Value);
        if (date.HasValue)
        {
            var d = date.Value.Date;
            query = query.Where(t => t.TripDate.Date == d);
        }

        // lọc theo trạng thái (Driver App gửi `status`).
        //   So sánh không phân biệt hoa/thường để chịu được cả
        //   'DELIVERING' lẫn 'delivering'.
        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim().ToUpperInvariant();
            query = query.Where(t => string.Equals(t.Status, wanted, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderByDescending(t => t.TripDate)
            .ThenBy(t => t.TripNumber)
            .Select(MapTrip)
            .ToList();
    }

    // -------------------------------------------------------------------
    // E2 — Chi tiết chuyến giao (kèm các điểm giao + hàng hóa)
    // -------------------------------------------------------------------
    public async Task<DeliveryTripDto> GetTripAsync(Guid tripId, CancellationToken ct = default)
    {
        var trip = await _uow.DeliveryTrips.GetByIdAsync(tripId)
            ?? throw new KeyNotFoundException($"Không tìm thấy chuyến giao {tripId}");

        var tripOrders = await _uow.DeliveryTripOrders.FindAsync(o => o.TripId == tripId);
        var soIds = tripOrders.Select(o => o.SalesOrderId).ToHashSet();

        var orders = await _uow.SalesOrders.FindAsync(o => soIds.Contains(o.Id));
        var items = await _uow.SalesOrderItems.FindAsync(i => soIds.Contains(i.SalesOrderId));
        var partners = await _uow.Partners.GetAllAsync();
        var warehouses = await _uow.Warehouses.GetAllAsync();
        var allProducts = await _uow.Products.GetAllAsync();

        var partnerMap = partners.ToDictionary(p => p.Id);
        var orderMap = orders.ToDictionary(o => o.Id);
        var warehouseMap = warehouses.ToDictionary(w => w.Id);
        // sales_order_items chỉ lưu product_sku (snapshot), không có tên.
        // => tên sản phẩm lấy từ bảng products.
        var productMap = allProducts.ToDictionary(p => p.Id);

        var dto = MapTrip(trip);

        if (warehouseMap.TryGetValue(trip.WarehouseId, out var wh))
            dto.WarehouseName = wh.WarehouseName;

        dto.Orders = tripOrders
            .OrderBy(o => o.StopOrder)
            .Select(to =>
            {
                var od = new TripOrderDto
                {
                    TripOrderId = to.Id.ToString(),
                    TripId = to.TripId.ToString(),
                    SalesOrderId = to.SalesOrderId.ToString(),
                    StopOrder = to.StopOrder,
                    Status = to.Status,
                    DeliveryStatus = to.Status == "PENDING" ? null : to.Status,
                    FailureReason = to.FailureReason,
                    FailureCount = to.FailureCount,
                    OldDebtAmount = to.OldDebtAmount,
                    Notes = to.Notes,
                };

                if (orderMap.TryGetValue(to.SalesOrderId, out var so))
                {
                    od.SalesOrderNumber = so.OrderNumber;
                    od.TotalAmount = so.TotalAmount;
                    od.PaidAmount = so.PaidAmount;
                    od.PaymentStatus = so.PaymentStatus;

                    if (partnerMap.TryGetValue(so.CustomerId, out var p))
                    {
                        od.PartnerId = p.Id.ToString();
                        od.PartnerName = p.PartnerName;
                        od.PartnerPhone = p.Phone;
                        od.PartnerAddress = p.Address;
                        od.PartnerLatitude = p.Latitude.HasValue ? (double)p.Latitude.Value : null;
                        od.PartnerLongitude = p.Longitude.HasValue ? (double)p.Longitude.Value : null;
                        od.Latitude = od.PartnerLatitude;
                        od.Longitude = od.PartnerLongitude;
                    }

                    od.Items = items
                        .Where(i => i.SalesOrderId == so.Id)
                        .OrderBy(i => i.LineNumber)
                        .Select(i => new DriverOrderItemDto
                        {
                            SalesOrderItemId = i.Id.ToString(),
                            SalesOrderId = i.SalesOrderId.ToString(),
                            ProductId = i.ProductId.ToString(),
                            Sku = i.ProductSku,
                            ProductName = productMap.TryGetValue(i.ProductId, out var prod)
                                ? prod.ProductName : i.ProductSku,
                            UomCode = i.UomCode,
                            Quantity = i.OrderedQty,
                            UnitPrice = i.UnitPrice,
                            DiscountAmount = i.DiscountAmount,
                            LineSubtotal = i.LineSubtotal,
                            LineTotal = i.LineTotal,
                            Notes = i.Notes,
                        })
                        .ToList();
                }

                return od;
            })
            .ToList();

        // Tổng công nợ hiện tại của các khách trong chuyến (DB-C7)
        if (dto.Orders != null)
        {
            var partnerIds = dto.Orders
                .Where(o => !string.IsNullOrEmpty(o.PartnerId))
                .Select(o => Guid.Parse(o.PartnerId))
                .ToHashSet();
            dto.TotalCurrentDebt = partners
                .Where(p => partnerIds.Contains(p.Id))
                .Sum(p => p.CurrentDebt);
        }

        return dto;
    }

    // -------------------------------------------------------------------
    // E3 — Bắt đầu chuyến giao
    // -------------------------------------------------------------------
    public async Task<DeliveryTripDto> StartTripAsync(Guid tripId, Guid? driverId, CancellationToken ct = default)
    {
        var trip = await _uow.DeliveryTrips.GetByIdAsync(tripId)
            ?? throw new KeyNotFoundException($"Không tìm thấy chuyến giao {tripId}");

        if (trip.Status is "COMPLETED" or "CANCELLED")
            throw new InvalidOperationException(
                $"Chuyến giao {trip.TripNumber} đã ở trạng thái {trip.Status}, không thể bắt đầu.");

        if (driverId.HasValue && trip.DriverId.HasValue && trip.DriverId.Value != driverId.Value)
            throw new UnauthorizedAccessException("Chuyến giao này đã được gán cho tài xế khác.");

        trip.Status = "DELIVERING";
        trip.DriverId ??= driverId;
        trip.StartedAt ??= DateTime.UtcNow;
        trip.UpdatedAt = DateTime.UtcNow;

        await _uow.SaveChangesAsync();
        _logger.LogInformation("Trip {TripNumber} started by driver {DriverId}", trip.TripNumber, driverId);

        return await GetTripAsync(tripId, ct);
    }

    // -------------------------------------------------------------------
    // E4 — Hoàn thành chuyến giao -> trả EOD report
    // -------------------------------------------------------------------
    public async Task<EodReportDto> CompleteTripAsync(Guid tripId, Guid? driverId, CancellationToken ct = default)
    {
        var trip = await _uow.DeliveryTrips.GetByIdAsync(tripId)
            ?? throw new KeyNotFoundException($"Không tìm thấy chuyến giao {tripId}");

        if (driverId.HasValue && trip.DriverId.HasValue && trip.DriverId.Value != driverId.Value)
            throw new UnauthorizedAccessException("Chuyến giao này đã được gán cho tài xế khác.");

        var tripOrders = await _uow.DeliveryTripOrders.FindAsync(o => o.TripId == tripId);
        var soIds = tripOrders.Select(o => o.SalesOrderId).ToHashSet();

        var orders = await _uow.SalesOrders.FindAsync(o => soIds.Contains(o.Id));
        var orderMap = orders.ToDictionary(o => o.Id);
        var partners = await _uow.Partners.GetAllAsync();
        var partnerMap = partners.ToDictionary(p => p.Id);

        // Tổng hợp từ các POD đã ghi nhận
        var pods = await _uow.DeliveryPods.GetAllAsync();
        var tripOrderIds = tripOrders.Select(o => o.Id).ToHashSet();
        var tripPods = pods.Where(p => tripOrderIds.Contains(p.TripOrderId)).ToList();

        var totalCash = tripPods.Sum(p => p.NewOrderCash + p.OldDebtCash);
        var totalTransfer = tripPods.Sum(p => p.NewOrderTransfer + p.OldDebtTransfer);
        var totalOldDebt = tripPods.Sum(p => p.OldDebtCash + p.OldDebtTransfer);
        var totalCollected = tripPods.Sum(p => p.TotalCollected);

        var delivered = tripOrders.Count(o => o.Status is "DELIVERED" or "PARTIAL");
        var failed = tripOrders.Count(o => o.Status == "FAILED");

        // ★ 'PARTIAL_DELIVERED' BẮT BUỘC: Driver App completeTrip() set giá trị
        //   này. Schema Postgres cũ thiếu -> insert bị reject.
        trip.Status = failed == 0 ? "COMPLETED" : "PARTIAL_DELIVERED";
        trip.DeliveredCount = delivered;
        trip.FailedCount = failed;
        trip.TotalCash = totalCash;
        trip.TotalTransfer = totalTransfer;
        trip.TotalOldDebt = totalOldDebt;
        trip.CompletedAt = DateTime.UtcNow;
        trip.UpdatedAt = DateTime.UtcNow;

        await _uow.SaveChangesAsync();
        _logger.LogInformation(
            "Trip {TripNumber} completed: delivered={Delivered}, failed={Failed}",
            trip.TripNumber, delivered, failed);

        var report = new EodReportDto
        {
            TripId = trip.Id.ToString(),
            TripNumber = trip.TripNumber,
            Status = trip.Status,
            TotalOrders = tripOrders.Count,
            DeliveredCount = delivered,
            FailedCount = failed,
            TotalCash = totalCash,
            TotalTransfer = totalTransfer,
            TotalOldDebt = totalOldDebt,
            TotalCollected = totalCollected,
            TotalReturnedQty = 0,
        };

        report.Items = tripOrders
            .OrderBy(o => o.StopOrder)
            .Select(to =>
            {
                var so = orderMap.GetValueOrDefault(to.SalesOrderId);
                var pod = tripPods.FirstOrDefault(p => p.TripOrderId == to.Id);
                return new EodReportItemDto
                {
                    TripOrderId = to.Id.ToString(),
                    SalesOrderNumber = so?.OrderNumber ?? string.Empty,
                    PartnerName = so != null && partnerMap.TryGetValue(so.CustomerId, out var p)
                        ? p.PartnerName : string.Empty,
                    Status = to.Status,
                    TotalAmount = so?.TotalAmount ?? 0,
                    TotalCollected = pod?.TotalCollected ?? 0,
                };
            })
            .ToList();

        return report;
    }

    // -------------------------------------------------------------------
    // E9/E10 — Master data cho offline
    // -------------------------------------------------------------------
    public async Task<IReadOnlyList<DriverPartnerDto>> GetMasterPartnersAsync(CancellationToken ct = default)
    {
        var all = await _uow.Partners.GetAllAsync();
        return all
            .Where(p => p.IsActive)
            .Select(p => new DriverPartnerDto
            {
                PartnerId = p.Id.ToString(),
                PartnerCode = p.PartnerCode,
                PartnerName = p.PartnerName,
                Phone = p.Phone,
                Email = p.Email,
                AddressLine = p.Address,
                Ward = p.Ward,
                District = p.District,
                Province = p.Province,
                Latitude = p.Latitude.HasValue ? (double)p.Latitude.Value : null,
                Longitude = p.Longitude.HasValue ? (double)p.Longitude.Value : null,
                PartnerType = p.PartnerType,
                CurrentDebt = p.CurrentDebt,
                OldDebt = p.OldDebt,
                OldDebtDate = p.OldDebtDate.HasValue ? IsoDate(p.OldDebtDate.Value) : null,
                RouteCode = p.RouteCode,
            })
            .ToList();
    }

    public async Task<IReadOnlyList<DriverProductDto>> GetMasterProductsAsync(CancellationToken ct = default)
    {
        var all = await _uow.Products.GetAllAsync();
        return all
            .Where(p => p.IsActive)
            .Select(p => new DriverProductDto
            {
                ProductId = p.Id.ToString(),
                Sku = p.Sku,
                ProductName = p.ProductName,
                Barcode = p.Barcode,
                CategoryId = p.CategoryId?.ToString(),
                UomCode = p.BaseUom,
                CostPrice = p.BasePrice,
                SellingPrice = p.BasePrice,
                MinStock = p.MinStock,
                MaxStock = p.MaxStock,
                ReorderPoint = p.ReorderPoint,
                Brand = p.Brand,
            })
            .ToList();
    }

    private static DeliveryTripDto MapTrip(DeliveryTrip t) => new()
    {
        TripId = t.Id.ToString(),
        TripNumber = t.TripNumber,
        TripDate = IsoDate(t.TripDate),
        WarehouseId = t.WarehouseId.ToString(),
        DriverId = t.DriverId?.ToString(),
        VehiclePlate = t.VehiclePlate,
        Status = t.Status,
        TotalOrders = t.TotalOrders,
        TotalAmount = t.TotalAmount,
        DeliveredCount = t.DeliveredCount,
        FailedCount = t.FailedCount,
        TotalCash = t.TotalCash,
        TotalTransfer = t.TotalTransfer,
        TotalOldDebt = t.TotalOldDebt,
        TotalCurrentDebt = t.TotalCurrentDebt,
        StartedAt = t.StartedAt.HasValue ? Iso(t.StartedAt.Value) : null,
        CompletedAt = t.CompletedAt.HasValue ? Iso(t.CompletedAt.Value) : null,
        CreatedAt = Iso(t.CreatedAt),
        UpdatedAt = Iso(t.UpdatedAt),
        Notes = t.Notes,
    };
}
