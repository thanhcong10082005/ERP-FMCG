using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.FMC.Api.Controllers;

// ===================================================================
// CONTROLLER BÁO CÁO VẬN HÀNH
// -------------------------------------------------------------------
//   R1  GET /api/reports/revenue-by-route      doanh thu theo tuyến
//   R2  GET /api/reports/success-rate          tỷ lệ giao thành công
//   R3  GET /api/reports/debt-aging            công nợ theo tuổi
//   R4  GET /api/reports/returned-goods        hàng rớt theo SKU
//   R5  GET /api/reports/cash-by-shift         tiền mặt theo ca
//   R6  GET /api/reports/low-stock             tồn kho & cảnh báo
//   R7  GET /api/reports/dashboard             gộp tất cả cho dashboard
// ===================================================================

[ApiController]
[Route("api/reports")]
[Produces("application/json")]
[Authorize(Roles = "Admin,Dispatcher,Manager,Accountant")]
public class ReportController : ControllerBase
{
    private readonly IReportService _reports;

    public ReportController(IReportService reports) => _reports = reports;

    /// <summary>R1 — Doanh thu theo tuyến.</summary>
    [HttpGet("revenue-by-route")]
    public async Task<ActionResult<IReadOnlyList<RevenueByRouteDto>>> RevenueByRoute(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _reports.GetRevenueByRouteAsync(from, to, ct));

    /// <summary>R2 — Tỷ lệ giao thành công (dimension = DATE | ROUTE | DRIVER).</summary>
    [HttpGet("success-rate")]
    public async Task<ActionResult<IReadOnlyList<DeliverySuccessRateDto>>> SuccessRate(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string dimension = "DATE",
        CancellationToken ct = default)
        => Ok(await _reports.GetSuccessRateAsync(dimension, from, to, ct));

    /// <summary>R3 — Công nợ theo tuổi (0-30 / 31-60 / 61-90 / &gt;90 ngày).</summary>
    [HttpGet("debt-aging")]
    public async Task<ActionResult<IReadOnlyList<DebtAgingDto>>> DebtAging(CancellationToken ct)
        => Ok(await _reports.GetDebtAgingAsync(ct));

    /// <summary>R4 — Hàng rớt theo SKU.</summary>
    [HttpGet("returned-goods")]
    public async Task<ActionResult<IReadOnlyList<ReturnedGoodsDto>>> ReturnedGoods(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _reports.GetReturnedGoodsAsync(from, to, ct));

    /// <summary>R5 — Tiền mặt theo ca (tài xế + ngày).</summary>
    [HttpGet("cash-by-shift")]
    public async Task<ActionResult<IReadOnlyList<CashByShiftDto>>> CashByShift(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _reports.GetCashByShiftAsync(from, to, ct));

    /// <summary>R6 — Tồn kho dưới mức tối thiểu.</summary>
    [HttpGet("low-stock")]
    public async Task<ActionResult<IReadOnlyList<LowStockDto>>> LowStock(CancellationToken ct)
        => Ok(await _reports.GetLowStockAsync(ct));

    /// <summary>
    /// R7 — Dashboard gộp: gọi tuần tự 6 báo cáo.
    /// Dispatcher mở 1 màn hình → 1 request thay vì 6.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<object>> Dashboard(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        return Ok(new
        {
            revenue_by_route = await _reports.GetRevenueByRouteAsync(from, to, ct),
            success_rate = await _reports.GetSuccessRateAsync("DATE", from, to, ct),
            debt_aging = await _reports.GetDebtAgingAsync(ct),
            returned_goods = await _reports.GetReturnedGoodsAsync(from, to, ct),
            cash_by_shift = await _reports.GetCashByShiftAsync(from, to, ct),
            low_stock = await _reports.GetLowStockAsync(ct),
        });
    }
}
