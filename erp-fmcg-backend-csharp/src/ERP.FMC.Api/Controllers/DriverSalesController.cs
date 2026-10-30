using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ERP.FMC.Api.Controllers;

// ===================================================================
// CONTROLLER NGHIỆP VỤ GIAO HÀNG (Driver App)
//
// ★ Tất cả ID trong đường dẫn là GUID (string) — khớp quyết định D1.
//
//  E1  GET  /api/sales/trips                    danh sách chuyến giao
//  E2  GET  /api/sales/trips/{tripId}           chi tiết chuyến giao
//  E3  PUT  /api/sales/trips/{tripId}/start     bắt đầu chuyến giao
//  E4  PUT  /api/sales/trips/{tripId}/complete  hoàn thành + EOD report
//  E5  POST /api/sales/pod                      tạo POD (idempotent)
//  E6  GET  /api/sales/pod/{clientPodId}        tra POD theo client_pod_id
//  E7  GET  /api/driver/master/partners         master data khách hàng
//  E8  GET  /api/driver/master/products         master data sản phẩm
// ===================================================================

[ApiController]
[Route("api/sales")]
[Produces("application/json")]
[Authorize]
public class DriverSalesController : ControllerBase
{
    private readonly IDeliveryTripService _tripService;
    private readonly IDeliveryPodService _podService;

    public DriverSalesController(
        IDeliveryTripService tripService,
        IDeliveryPodService podService)
    {
        _tripService = tripService;
        _podService = podService;
    }

    /// <summary>Lấy ID tài xế hiện tại từ JWT (null nếu request không có token hợp lệ).</summary>
    private Guid? CurrentDriverId
    {
        get
        {
            var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst("sub")?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    // -------------------------------------------------------------------
    // E1 — Danh sách chuyến giao
    // -------------------------------------------------------------------
    /// <param name="driverId">Lọc theo tài xế (mặc định: tài xế đang đăng nhập).</param>
    /// <param name="driverIdAlias">Alias snake_case `driver_id` (Driver App gửi theo convention này).</param>
    /// <param name="date">Lọc theo ngày (yyyy-MM-dd).</param>
    /// <param name="status">Lọc theo trạng thái chuyến (PREPARING / DELIVERING / COMPLETED ...).</param>
    [HttpGet("trips")]
    public async Task<ActionResult<IReadOnlyList<DeliveryTripDto>>> GetTrips(
        [FromQuery] string? driverId,
        [FromQuery] string? driver_id,
        [FromQuery] DateTime? date,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        // hỗ trợ CẢ `driverId` (camelCase) và `driver_id`
        //   (snake_case). Driver App gửi `driver_id` + `status`; trước đây
        //   ASP.NET Core không bind được `driver_id` => app luôn gửi
        //   driverId=null => server lọc theo token => tài xế không thấy
        //   chuyến được gán nếu token không khớp.
        var rawDriver = !string.IsNullOrWhiteSpace(driverId) ? driverId : driver_id;

        Guid? filterDriver = null;
        if (!string.IsNullOrWhiteSpace(rawDriver) && Guid.TryParse(rawDriver, out var d))
            filterDriver = d;
        else
            filterDriver = CurrentDriverId;

        var trips = await _tripService.GetTripsAsync(filterDriver, date, status, ct);

        return Ok(trips);
    }

    // -------------------------------------------------------------------
    // E2 — Chi tiết chuyến giao
    // -------------------------------------------------------------------
    [HttpGet("trips/{tripId}")]
    public async Task<ActionResult<DeliveryTripDto>> GetTrip(string tripId, CancellationToken ct)
    {
        if (!Guid.TryParse(tripId, out var id))
            return BadRequest(new { error = "INVALID_ID", message = "tripId phải là GUID hợp lệ." });

        var trip = await _tripService.GetTripAsync(id, ct);
        return Ok(trip);
    }

    // -------------------------------------------------------------------
    // E3 — Bắt đầu chuyến giao
    // -------------------------------------------------------------------
    [HttpPut("trips/{tripId}/start")]
    public async Task<ActionResult<DeliveryTripDto>> StartTrip(string tripId, CancellationToken ct)
    {
        if (!Guid.TryParse(tripId, out var id))
            return BadRequest(new { error = "INVALID_ID", message = "tripId phải là GUID hợp lệ." });

        var trip = await _tripService.StartTripAsync(id, CurrentDriverId, ct);
        return Ok(trip);
    }

    // -------------------------------------------------------------------
    // E4 — Hoàn thành chuyến giao
    // -------------------------------------------------------------------
    [HttpPut("trips/{tripId}/complete")]
    public async Task<ActionResult<EodReportDto>> CompleteTrip(string tripId, CancellationToken ct)
    {
        if (!Guid.TryParse(tripId, out var id))
            return BadRequest(new { error = "INVALID_ID", message = "tripId phải là GUID hợp lệ." });

        var report = await _tripService.CompleteTripAsync(id, CurrentDriverId, ct);
        return Ok(report);
    }

    // -------------------------------------------------------------------
    // E5 — Tạo POD (IDEMPOTENT theo client_pod_id)
    // -------------------------------------------------------------------
    /// <summary>
    /// Ghi phiếu giao hàng.
    ///
    /// ★ IDEMPOTENCY: `client_pod_id` là UUID do client sinh MỘT LẦN và gửi
    ///   kèm mọi lần retry. Nếu POD đã tồn tại, server trả về POD cũ với
    ///   `created = false` (HTTP 200) — KHÔNG tạo bản ghi trùng.
    ///
    ///   Đây là lớp bảo vệ thứ 2 (lớp 1 là client không enqueue sau khi
    ///   POST online thành công). Lớp 2 đảm bảo an toàn tuyệt đối dù
    ///   client còn bug.
    /// </summary>
    [HttpPost("pod")]
    public async Task<ActionResult<PodResponseDto>> CreatePod(
        [FromBody] CreatePodRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { error = "VALIDATION_ERROR", message = "Dữ liệu POD không hợp lệ." });

        var result = await _podService.CreatePodAsync(request, CurrentDriverId, ct);

        // 200 cho cả 2 trường hợp (tạo mới / idempotent hit) để client
        // không phải phân biệt lỗi.
        return Ok(result);
    }

    // -------------------------------------------------------------------
    // E6 — Tra POD theo client_pod_id
    // -------------------------------------------------------------------
    [HttpGet("pod/{clientPodId}")]
    public async Task<ActionResult<PodResponseDto>> GetPod(string clientPodId, CancellationToken ct)
    {
        if (!Guid.TryParse(clientPodId, out var id))
            return BadRequest(new { error = "INVALID_ID", message = "clientPodId phải là GUID hợp lệ." });

        var pod = await _podService.GetByClientPodIdAsync(id, ct);
        if (pod is null)
            return NotFound(new { error = "NOT_FOUND", message = "Không tìm thấy POD." });

        return Ok(pod);
    }
}

// ===================================================================
// Master data cho Driver App (offline sync)
// ===================================================================

[ApiController]
[Route("api/driver/master")]
[Produces("application/json")]
[Authorize]
public class DriverMasterController : ControllerBase
{
    private readonly IDeliveryTripService _tripService;

    public DriverMasterController(IDeliveryTripService tripService) => _tripService = tripService;

    /// <summary>
    /// E7 — Danh sách khách hàng cho offline cache.
    /// Trả về đúng shape `DriverPartnerDto` (snake_case, id string).
    /// </summary>
    [HttpGet("partners")]
    public async Task<ActionResult<IReadOnlyList<DriverPartnerDto>>> GetPartners(CancellationToken ct)
        => Ok(await _tripService.GetMasterPartnersAsync(ct));

    /// <summary>E8 — Danh sách sản phẩm cho offline cache.</summary>
    [HttpGet("products")]
    public async Task<ActionResult<IReadOnlyList<DriverProductDto>>> GetProducts(CancellationToken ct)
        => Ok(await _tripService.GetMasterProductsAsync(ct));
}
