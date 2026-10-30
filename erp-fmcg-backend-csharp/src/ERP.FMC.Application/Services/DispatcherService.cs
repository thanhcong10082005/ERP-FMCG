using ERP.FMC.Application.DTOs;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Application.Services;

// ===================================================================
// DỊCH VỤ QUẢN TRỊ CHUYẾN XE (DISPATCHER)
// -------------------------------------------------------------------
// API cho TÀI XẾ chỉ xem chuyến của mình. Dispatcher cần:
//   - Tạo chuyến, gán tài xế, gắn đơn hàng vào chuyến
//   - Xem TẤT CẢ chuyến (toàn hệ thống)
//   - Xem trip + POD + GPS (bản đồ audit)
//
// Khác biệt bảo mật: các endpoint này yêu cầu role quản trị,
// Driver App KHÔNG được gọi.
// ===================================================================

public interface IDispatcherService
{
    /// <summary>Tạo chuyến xe mới.</summary>
    Task<DeliveryTripDto> CreateTripAsync(CreateTripRequestDto request, Guid? createdBy, CancellationToken ct = default);

    /// <summary>Gắn danh sách đơn hàng vào chuyến.</summary>
    Task<DeliveryTripDto> AttachOrdersAsync(Guid tripId, AttachOrdersRequestDto request, CancellationToken ct = default);

    /// <summary>Xem tất cả chuyến (dispatcher).</summary>
    Task<IReadOnlyList<DeliveryTripDto>> GetAllTripsAsync(DateTime? date, string? status, Guid? driverId, CancellationToken ct = default);

    /// <summary>Chi tiết chuyến đầy đủ: trip + POD + GPS (bản đồ audit).</summary>
    Task<TripFullDto> GetTripFullAsync(Guid tripId, CancellationToken ct = default);

    /// <summary>Chi tiết 1 POD.</summary>
    Task<PodDetailDto?> GetPodDetailAsync(Guid podId, CancellationToken ct = default);
}

public class DispatcherService : IDispatcherService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DispatcherService> _logger;

    public DispatcherService(IUnitOfWork uow, ILogger<DispatcherService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    private static string Iso(DateTime dt) => dt.ToString("yyyy-MM-ddTHH:mm:ss");
    private static string IsoDate(DateTime dt) => dt.ToString("yyyy-MM-dd");

    // -------------------------------------------------------------------
    // Tạo chuyến xe
    // -------------------------------------------------------------------
    public async Task<DeliveryTripDto> CreateTripAsync(
        CreateTripRequestDto request, Guid? createdBy, CancellationToken ct = default)
    {
        if (!Guid.TryParse(request.WarehouseId, out var warehouseId))
            throw new ArgumentException("warehouse_id không hợp lệ.");

        var warehouse = await _uow.Warehouses.GetByIdAsync(warehouseId)
            ?? throw new KeyNotFoundException($"Không tìm thấy kho {warehouseId}");

        Guid? driverId = null;
        if (!string.IsNullOrWhiteSpace(request.DriverId))
        {
            if (!Guid.TryParse(request.DriverId, out var d))
                throw new ArgumentException("driver_id không hợp lệ.");

            var driver = await _uow.UserAccounts.GetByIdAsync(d)
                ?? throw new KeyNotFoundException($"Không tìm thấy tài xế {d}");
            driverId = d;
        }

        // Ngày chạy xe: mặc định = hôm nay.
        var tripDate = DateTime.Today;
        if (!string.IsNullOrWhiteSpace(request.TripDate))
        {
            if (!DateTime.TryParse(request.TripDate, out tripDate))
                throw new ArgumentException("trip_date không hợp lệ (yyyy-MM-dd).");
        }

        // Sinh trip_number: TRIP-yyyyMMdd-HHmm (unique theo phút + hậu tố).
        var existing = await _uow.DeliveryTrips.GetAllAsync();
        var baseNumber = $"TRIP-{tripDate:yyyyMMdd}-{DateTime.Now:HHmm}";
        var tripNumber = baseNumber;
        var seq = 1;
        while (existing.Any(t => string.Equals(t.TripNumber, tripNumber, StringComparison.OrdinalIgnoreCase)))
        {
            tripNumber = $"{baseNumber}-{seq++}";
        }

        var trip = new DeliveryTrip
        {
            TripNumber = tripNumber,
            TripDate = tripDate.Date,
            WarehouseId = warehouseId,
            DriverId = driverId,
            VehiclePlate = request.VehiclePlate,
            Status = "PREPARING",
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };

        await _uow.DeliveryTrips.AddAsync(trip);
        await _uow.SaveChangesAsync();

        _logger.LogInformation(
            "Dispatcher tao chuyen: {TripNumber} warehouse={WarehouseId} driver={DriverId}",
            trip.TripNumber, warehouseId, driverId);

        return await BuildTripDtoAsync(trip);
    }

    // -------------------------------------------------------------------
    // Gắn đơn hàng vào chuyến
    // -------------------------------------------------------------------
    public async Task<DeliveryTripDto> AttachOrdersAsync(
        Guid tripId, AttachOrdersRequestDto request, CancellationToken ct = default)
    {
        var trip = await _uow.DeliveryTrips.GetByIdAsync(tripId)
            ?? throw new KeyNotFoundException($"Không tìm thấy chuyến giao {tripId}");

        if (trip.Status is "DELIVERING" or "COMPLETED" or "PARTIAL_DELIVERED")
            throw new InvalidOperationException(
                $"Không thể gắn đơn vào chuyến đang ở trạng thái {trip.Status}.");

        var existingOrders = await _uow.DeliveryTripOrders.FindAsync(o => o.TripId == tripId);
        var existingSoIds = existingOrders.Select(o => o.SalesOrderId).ToHashSet();

        var partners = await _uow.Partners.GetAllAsync();
        var partnerMap = partners.ToDictionary(p => p.Id);

        var stopOrder = request.StopOrderStart ?? (existingOrders.Count + 1);
        var added = 0;
        var skipped = 0;

        foreach (var rawSoId in request.SalesOrderIds ?? new List<string>())
        {
            if (!Guid.TryParse(rawSoId, out var soId))
                throw new ArgumentException($"sales_order_id không hợp lệ: {rawSoId}");

            if (existingSoIds.Contains(soId))
            {
                skipped++;
                continue;
            }

            var salesOrder = await _uow.SalesOrders.GetByIdAsync(soId)
                ?? throw new KeyNotFoundException($"Không tìm thấy đơn hàng {soId}");

            var order = new DeliveryTripOrder
            {
                TripId = tripId,
                SalesOrderId = soId,
                StopOrder = stopOrder++,
                Status = "PENDING",
                // Snapshot công nợ cũ lúc gán vào chuyến (D8).
                OldDebtAmount = partnerMap.TryGetValue(salesOrder.CustomerId, out var p)
                    ? p.OldDebt : 0m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            await _uow.DeliveryTripOrders.AddAsync(order);
            existingSoIds.Add(soId);
            added++;
        }

        // Cập nhật tổng số đơn + tổng tiền trên chuyến.
        var allOrders = await _uow.DeliveryTripOrders.FindAsync(o => o.TripId == tripId);
        var soIds = allOrders.Select(o => o.SalesOrderId).ToHashSet();
        var orders = await _uow.SalesOrders.FindAsync(o => soIds.Contains(o.Id));

        trip.TotalOrders = allOrders.Count;
        trip.TotalAmount = orders.Sum(o => o.TotalAmount);
        trip.UpdatedAt = DateTime.UtcNow;

        await _uow.SaveChangesAsync();

        _logger.LogInformation(
            "Dispatcher gan don: trip={TripNumber} added={Added} skipped={Skipped}",
            trip.TripNumber, added, skipped);

        return await BuildTripDtoAsync(trip);
    }

    // -------------------------------------------------------------------
    // Xem tất cả chuyến
    // -------------------------------------------------------------------
    public async Task<IReadOnlyList<DeliveryTripDto>> GetAllTripsAsync(
        DateTime? date, string? status, Guid? driverId, CancellationToken ct = default)
    {
        var all = await _uow.DeliveryTrips.GetAllAsync();

        var query = all.AsEnumerable();
        if (date.HasValue)
        {
            var d = date.Value.Date;
            query = query.Where(t => t.TripDate.Date == d);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim().ToUpperInvariant();
            query = query.Where(t => string.Equals(t.Status, wanted, StringComparison.OrdinalIgnoreCase));
        }
        if (driverId.HasValue)
            query = query.Where(t => t.DriverId == driverId.Value);

        var list = query
            .OrderByDescending(t => t.TripDate)
            .ThenBy(t => t.TripNumber)
            .ToList();

        var warehouses = await _uow.Warehouses.GetAllAsync();
        var drivers = await _uow.UserAccounts.GetAllAsync();
        var whMap = warehouses.ToDictionary(w => w.Id);
        var drMap = drivers.ToDictionary(d => d.Id);

        return list.Select(t =>
        {
            var dto = new DeliveryTripDto
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
            if (whMap.TryGetValue(t.WarehouseId, out var wh))
                dto.WarehouseName = wh.WarehouseName;
            if (t.DriverId.HasValue && drMap.TryGetValue(t.DriverId.Value, out var dr))
                dto.DriverName = dr.FullName;
            return dto;
        }).ToList();
    }

    // -------------------------------------------------------------------
    // Chi tiết chuyến đầy đủ (trip + POD + GPS)
    // -------------------------------------------------------------------
    public async Task<TripFullDto> GetTripFullAsync(Guid tripId, CancellationToken ct = default)
    {
        var trip = await _uow.DeliveryTrips.GetByIdAsync(tripId)
            ?? throw new KeyNotFoundException($"Không tìm thấy chuyến giao {tripId}");

        var tripOrders = await _uow.DeliveryTripOrders.FindAsync(o => o.TripId == tripId);
        var tripOrderIds = tripOrders.Select(o => o.Id).ToHashSet();

        var pods = (await _uow.DeliveryPods.GetAllAsync())
            .Where(p => tripOrderIds.Contains(p.TripOrderId))
            .OrderBy(p => p.DeliveredAt ?? p.CreatedAt)
            .ToList();

        var partners = await _uow.Partners.GetAllAsync();
        var partnerMap = partners.ToDictionary(p => p.Id);

        var podDetails = pods.Select(p => MapPodDetail(p, partnerMap)).ToList();

        // Điểm bản đồ audit
        var mapPoints = podDetails
            .Where(p => p.DeliveryLatitude.HasValue && p.DeliveryLongitude.HasValue)
            .Select(p => new MapPointDto
            {
                PodId = p.PodId,
                PartnerName = p.PartnerName,
                Latitude = p.DeliveryLatitude,
                Longitude = p.DeliveryLongitude,
                GpsGateResult = p.GpsGateResult,
                DistanceMeters = p.GpsGateDistanceM ?? p.DistanceToPartnerM,
                DeliveryStatus = p.DeliveryStatus,
            })
            .ToList();

        var delivered = pods.Count(p => p.DeliveryStatus is "DELIVERED" or "PARTIAL");
        var failed = pods.Count(p => p.DeliveryStatus == "FAILED");

        return new TripFullDto
        {
            Trip = await BuildTripDtoAsync(trip),
            Pods = podDetails,
            MapPoints = mapPoints,
            Summary = new TripSummaryDto
            {
                TotalOrders = tripOrders.Count,
                DeliveredCount = delivered,
                FailedCount = failed,
                TotalAmount = trip.TotalAmount,
                TotalCash = pods.Sum(p => p.NewOrderCash + p.OldDebtCash),
                TotalTransfer = pods.Sum(p => p.NewOrderTransfer + p.OldDebtTransfer),
                TotalOldDebt = pods.Sum(p => p.OldDebtCash + p.OldDebtTransfer),
                SuccessRate = tripOrders.Count > 0
                    ? Math.Round(delivered * 100m / tripOrders.Count, 2)
                    : 0m,
                GpsAlerts = podDetails.Count(p =>
                    p.GpsGateResult is "FAIL" or "FAIL_NO_GPS"),
            },
        };
    }

    // -------------------------------------------------------------------
    // Chi tiết 1 POD
    // -------------------------------------------------------------------
    public async Task<PodDetailDto?> GetPodDetailAsync(Guid podId, CancellationToken ct = default)
    {
        var pod = await _uow.DeliveryPods.GetByIdAsync(podId);
        if (pod is null) return null;

        var partners = await _uow.Partners.GetAllAsync();
        return MapPodDetail(pod, partners.ToDictionary(p => p.Id));
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    private static PodDetailDto MapPodDetail(DeliveryPod p, Dictionary<Guid, Partner> partnerMap)
    {
        return new PodDetailDto
        {
            PodId = p.Id.ToString(),
            ClientPodId = p.ClientPodId.ToString(),
            TripOrderId = p.TripOrderId.ToString(),
            SalesOrderId = p.SalesOrderId.ToString(),
            PartnerId = p.PartnerId.ToString(),
            PartnerName = partnerMap.TryGetValue(p.PartnerId, out var pr) ? pr.PartnerName : null,
            DeliveryStatus = p.DeliveryStatus,
            TotalCollected = p.TotalCollected,
            // đủ 6 ô thanh toán
            NewOrderCash = p.NewOrderCash,
            NewOrderTransfer = p.NewOrderTransfer,
            NewOrderCredit = p.NewOrderCredit,
            OldDebtCash = p.OldDebtCash,
            OldDebtTransfer = p.OldDebtTransfer,
            OldDebtCredit = p.OldDebtCredit,
            FailureReason = p.FailureReason,
            DeliveryLatitude = p.DeliveryLatitude.HasValue ? (double)p.DeliveryLatitude.Value : null,
            DeliveryLongitude = p.DeliveryLongitude.HasValue ? (double)p.DeliveryLongitude.Value : null,
            GpsGateResult = p.GpsGateResult,
            GpsGateDistanceM = p.GpsGateDistanceM,
            DistanceToPartnerM = p.DistanceToPartnerM,
            TimeGuardResult = p.TimeGuardResult,
            ArrivedAt = p.ArrivedAt.HasValue ? Iso(p.ArrivedAt.Value) : null,
            DeliveredAt = p.DeliveredAt.HasValue ? Iso(p.DeliveredAt.Value) : null,
            GpsCapturedAt = p.GpsCapturedAt.HasValue ? Iso(p.GpsCapturedAt.Value) : null,
            RecipientName = p.RecipientName,
            Notes = p.Notes,
        };
    }

    private async Task<DeliveryTripDto> BuildTripDtoAsync(DeliveryTrip trip)
    {
        var tripOrders = await _uow.DeliveryTripOrders.FindAsync(o => o.TripId == trip.Id);
        var warehouses = await _uow.Warehouses.GetAllAsync();
        var drivers = await _uow.UserAccounts.GetAllAsync();

        var dto = new DeliveryTripDto
        {
            TripId = trip.Id.ToString(),
            TripNumber = trip.TripNumber,
            TripDate = IsoDate(trip.TripDate),
            WarehouseId = trip.WarehouseId.ToString(),
            DriverId = trip.DriverId?.ToString(),
            VehiclePlate = trip.VehiclePlate,
            Status = trip.Status,
            TotalOrders = trip.TotalOrders > 0 ? trip.TotalOrders : tripOrders.Count,
            TotalAmount = trip.TotalAmount,
            DeliveredCount = trip.DeliveredCount,
            FailedCount = trip.FailedCount,
            TotalCash = trip.TotalCash,
            TotalTransfer = trip.TotalTransfer,
            TotalOldDebt = trip.TotalOldDebt,
            TotalCurrentDebt = trip.TotalCurrentDebt,
            StartedAt = trip.StartedAt.HasValue ? Iso(trip.StartedAt.Value) : null,
            CompletedAt = trip.CompletedAt.HasValue ? Iso(trip.CompletedAt.Value) : null,
            CreatedAt = Iso(trip.CreatedAt),
            UpdatedAt = Iso(trip.UpdatedAt),
            Notes = trip.Notes,
        };

        var wh = warehouses.FirstOrDefault(w => w.Id == trip.WarehouseId);
        if (wh is not null) dto.WarehouseName = wh.WarehouseName;

        if (trip.DriverId.HasValue)
        {
            var dr = drivers.FirstOrDefault(d => d.Id == trip.DriverId.Value);
            if (dr is not null) dto.DriverName = dr.FullName;
        }

        return dto;
    }
}
