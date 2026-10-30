using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ERP.FMC.Api.Controllers;

// ===================================================================
// ★ PHASE 6B — CONTROLLER DISPATCHER
// -------------------------------------------------------------------
// Các endpoint cho DISPATCHER (điều phối viên) và quản lý.
//
// ★ BẢO MẬT:
//   - `[Authorize(Roles = "...")]` — chỉ role quản trị.
//   - Driver App KHÔNG được gọi các endpoint này.
//   - Driver App dùng namespace `/api/sales/...` nhưng các endpoint
//     dispatcher nằm ở `/api/dispatcher/...` để tách bạch rõ ràng.
//
//  D1  POST   /api/dispatcher/trips                  tạo chuyến xe
//  D2  POST   /api/dispatcher/trips/{id}/orders      gắn đơn vào chuyến
//  D3  GET    /api/dispatcher/trips                  xem TẤT CẢ chuyến
//  D4  GET    /api/dispatcher/trips/{id}/full        trip + POD + GPS (bản đồ audit)
//  D5  GET    /api/dispatcher/pod/{id}               chi tiết 1 POD
//  D6  POST   /api/dispatcher/trips/{id}/eod         tạo phiếu đối soát cuối ngày
//  D7  GET    /api/dispatcher/eod/{id}               chi tiết phiếu đối soát
//  D8  POST   /api/dispatcher/eod/{id}/approve       duyệt phiếu
//  D9  GET    /api/dispatcher/eod                    danh sách phiếu
// ===================================================================

[ApiController]
[Route("api/dispatcher")]
[Produces("application/json")]
[Authorize(Roles = "Admin,Dispatcher,Manager")]
public class DispatcherController : ControllerBase
{
    private readonly IDispatcherService _dispatcher;
    private readonly IEodReconciliationService _eod;

    public DispatcherController(
        IDispatcherService dispatcher,
        IEodReconciliationService eod)
    {
        _dispatcher = dispatcher;
        _eod = eod;
    }

    private Guid? CurrentUserId
    {
        get
        {
            var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst("sub")?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    // -------------------------------------------------------------------
    // D1 — Tạo chuyến xe
    // -------------------------------------------------------------------
    [HttpPost("trips")]
    public async Task<ActionResult<DeliveryTripDto>> CreateTrip(
        [FromBody] CreateTripRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { error = "VALIDATION_ERROR", message = "Dữ liệu chuyến không hợp lệ." });

        var trip = await _dispatcher.CreateTripAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(GetTripFull), new { tripId = trip.TripId }, trip);
    }

    // -------------------------------------------------------------------
    // D2 — Gắn đơn vào chuyến
    // -------------------------------------------------------------------
    [HttpPost("trips/{tripId}/orders")]
    public async Task<ActionResult<DeliveryTripDto>> AttachOrders(
        string tripId, [FromBody] AttachOrdersRequestDto request, CancellationToken ct)
    {
        if (!Guid.TryParse(tripId, out var id))
            return BadRequest(new { error = "INVALID_ID", message = "tripId phải là GUID hợp lệ." });

        if (request.SalesOrderIds is null || request.SalesOrderIds.Count == 0)
            return BadRequest(new
            {
                error = "VALIDATION_ERROR",
                message = "sales_order_ids không được rỗng."
            });

        var trip = await _dispatcher.AttachOrdersAsync(id, request, ct);
        return Ok(trip);
    }

    // -------------------------------------------------------------------
    // D3 — Xem tất cả chuyến
    // -------------------------------------------------------------------
    [HttpGet("trips")]
    public async Task<ActionResult<IReadOnlyList<DeliveryTripDto>>> GetAllTrips(
        [FromQuery] DateTime? date,
        [FromQuery] string? status,
        [FromQuery] string? driver_id,
        CancellationToken ct)
    {
        Guid? driverId = null;
        if (!string.IsNullOrWhiteSpace(driver_id) && Guid.TryParse(driver_id, out var d))
            driverId = d;

        var trips = await _dispatcher.GetAllTripsAsync(date, status, driverId, ct);
        return Ok(trips);
    }

    // -------------------------------------------------------------------
    // D4 — ★ 6B.3 — Chi tiết chuyến đầy đủ (bản đồ audit)
    // -------------------------------------------------------------------
    [HttpGet("trips/{tripId}/full")]
    public async Task<ActionResult<TripFullDto>> GetTripFull(string tripId, CancellationToken ct)
    {
        if (!Guid.TryParse(tripId, out var id))
            return BadRequest(new { error = "INVALID_ID", message = "tripId phải là GUID hợp lệ." });

        return Ok(await _dispatcher.GetTripFullAsync(id, ct));
    }

    // -------------------------------------------------------------------
    // D5 — Chi tiết 1 POD
    // -------------------------------------------------------------------
    [HttpGet("pod/{podId}")]
    public async Task<ActionResult<PodDetailDto>> GetPod(string podId, CancellationToken ct)
    {
        if (!Guid.TryParse(podId, out var id))
            return BadRequest(new { error = "INVALID_ID", message = "podId phải là GUID hợp lệ." });

        var pod = await _dispatcher.GetPodDetailAsync(id, ct);
        if (pod is null)
            return NotFound(new { error = "NOT_FOUND", message = "Không tìm thấy POD." });

        return Ok(pod);
    }

    // -------------------------------------------------------------------
    // D6 — Tạo phiếu đối soát cuối ngày
    // -------------------------------------------------------------------
    [HttpPost("trips/{tripId}/eod")]
    public async Task<ActionResult<EodReconciliationDto>> CreateEod(
        string tripId, [FromBody] CreateEodRequestDto request, CancellationToken ct)
    {
        if (!Guid.TryParse(tripId, out var id))
            return BadRequest(new { error = "INVALID_ID", message = "tripId phải là GUID hợp lệ." });

        var recon = await _eod.CreateFromTripAsync(id, request, CurrentUserId, ct);
        return Ok(recon);
    }

    // -------------------------------------------------------------------
    // D7 — Chi tiết phiếu đối soát
    // -------------------------------------------------------------------
    [HttpGet("eod/{id}")]
    public async Task<ActionResult<EodReconciliationDto>> GetEod(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid))
            return BadRequest(new { error = "INVALID_ID", message = "id phải là GUID hợp lệ." });

        var recon = await _eod.GetByIdAsync(guid, ct);
        if (recon is null)
            return NotFound(new { error = "NOT_FOUND", message = "Không tìm thấy phiếu đối soát." });

        return Ok(recon);
    }

    // -------------------------------------------------------------------
    // D8 — Duyệt phiếu
    // -------------------------------------------------------------------
    [HttpPost("eod/{id}/approve")]
    public async Task<ActionResult<EodReconciliationDto>> ApproveEod(
        string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid))
            return BadRequest(new { error = "INVALID_ID", message = "id phải là GUID hợp lệ." });

        var recon = await _eod.ApproveAsync(guid, CurrentUserId, ct);
        return Ok(recon);
    }

    // -------------------------------------------------------------------
    // D9 — Danh sách phiếu đối soát
    // -------------------------------------------------------------------
    [HttpGet("eod")]
    public async Task<ActionResult<IReadOnlyList<EodReconciliationDto>>> ListEod(
        [FromQuery] DateTime? date,
        [FromQuery] string? status,
        [FromQuery] string? driver_id,
        CancellationToken ct)
    {
        Guid? driverId = null;
        if (!string.IsNullOrWhiteSpace(driver_id) && Guid.TryParse(driver_id, out var d))
            driverId = d;

        var list = await _eod.ListAsync(date, driverId, status, ct);
        return Ok(list);
    }
}
