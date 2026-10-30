using System.Diagnostics;
using ClosedXML.Excel;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Application.Services;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Infrastructure.Services;

// ===================================================================
// XUẤT EXCEL CHO BÁO CÁO VẬN HÀNH
// -------------------------------------------------------------------
// SYMPTOM (triệu chứng):
//   Dispatcher phải in báo cáo cuối ngày (POD theo chuyến, công nợ
//   khách, tiền mặt theo ca) để nộp cho kế toán. Trước đây
//   không có endpoint nào xuất được những báo cáo này -> phải chép tay
//   từ màn hình, mỗi ca ~30-45 phút, sai số.
//
// ROOT CAUSE (nguyên nhân gốc):
//   `ExportService` chỉ hỗ trợ PARTNERS / PRODUCTS / ORDERS /
//   INVENTORY / REVENUE — lấy dữ liệu từ các entity gốc.
//   vai tro tạo 6 báo cáo vận hành MỚI (doanh thu theo tuyến, tỷ lệ
//   giao thành công, công nợ theo tuổi, hàng rớt, tiền mặt theo ca,
//   tồn kho cảnh báo) + dữ liệu POD/EOD. Không có "adapter" nào đưa
//   các báo cáo đó ra Excel.
//
// FIX (cách sửa):
//   `OperationsExportService` đặt cạnh `ExportService`, dùng CHUNG
//   `IReportService` / `IEodReconciliationService` / `IDispatcherService`
//   để lấy dữ liệu đã chuẩn hoá, rồi ghi ra .xlsx bằng ClosedXML.
//   Không query DB lần 2 -> báo cáo Excel luôn khớp với báo cáo
//   trên màn hình (cùng 1 nguồn sự thật).
//
// PREVENTION (ngăn tái diễn):
//   - Tất cả báo cáo đi qua `IReportService` -> 1 nguồn logic.
//   - Test `OperationsExportTests` assert file .xlsx tồn tại, có sheet
//     đúng tên, và số dòng = số bản ghi.
//   - Endpoint `/api/exports/operations/{type}` trả về file để tải trực tiếp.
// ===================================================================

public class OperationsExportService
{
    private readonly IReportService _reports;
    private readonly IEodReconciliationService _eod;
    private readonly IDispatcherService _dispatcher;
    private readonly ILogger<OperationsExportService> _logger;
    private readonly string _exportFolder;

    public OperationsExportService(
        IReportService reports,
        IEodReconciliationService eod,
        IDispatcherService dispatcher,
        ILogger<OperationsExportService> logger)
    {
        _reports = reports;
        _eod = eod;
        _dispatcher = dispatcher;
        _logger = logger;
        _exportFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ERP_FMCG", "Exports");
        Directory.CreateDirectory(_exportFolder);
    }

    /// <summary>
    /// Xuất 1 báo cáo vận hành ra file .xlsx.
    /// </summary>
    /// <param name="exportType">
    /// REVENUE_BY_ROUTE | SUCCESS_RATE | DEBT_AGING | RETURNED_GOODS |
    /// CASH_BY_SHIFT | LOW_STOCK | POD_BY_TRIP | EOD_RECONCILIATION
    /// </param>
    public async Task<ExportResultDto> ExportAsync(
        string exportType,
        ExportRequestDto request,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var type = (exportType ?? string.Empty).Trim().ToUpperInvariant();

        var fileName = $"Operations_{type}_{(request.FromDate?.ToString("yyyyMMdd") ?? "ALL")}" +
                       $"_to_{(request.ToDate?.ToString("yyyyMMdd") ?? "ALL")}" +
                       $"_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(_exportFolder, fileName);

        int rowCount = 0;

        using var wb = new XLWorkbook();

        switch (type)
        {
            case "REVENUE_BY_ROUTE":
                rowCount = await WriteRevenueByRouteAsync(wb, request, ct);
                break;
            case "SUCCESS_RATE":
                rowCount = await WriteSuccessRateAsync(wb, request, ct);
                break;
            case "DEBT_AGING":
                rowCount = await WriteDebtAgingAsync(wb, ct);
                break;
            case "RETURNED_GOODS":
                rowCount = await WriteReturnedGoodsAsync(wb, request, ct);
                break;
            case "CASH_BY_SHIFT":
                rowCount = await WriteCashByShiftAsync(wb, request, ct);
                break;
            case "LOW_STOCK":
                rowCount = await WriteLowStockAsync(wb, ct);
                break;
            case "POD_BY_TRIP":
                rowCount = await WritePodByTripAsync(wb, request, ct);
                break;
            case "EOD_RECONCILIATION":
                rowCount = await WriteEodAsync(wb, request, ct);
                break;
            default:
                throw new NotSupportedException(
                    $"Export type '{exportType}' khong ho tro. " +
                    "Ho tro: REVENUE_BY_ROUTE, SUCCESS_RATE, DEBT_AGING, " +
                    "RETURNED_GOODS, CASH_BY_SHIFT, LOW_STOCK, POD_BY_TRIP, " +
                    "EOD_RECONCILIATION.");
        }

        wb.SaveAs(filePath);
        sw.Stop();

        _logger.LogInformation(
            "Operations export: type={Type} file={File} rows={Rows} duration={Ms}ms",
            type, fileName, rowCount, sw.ElapsedMilliseconds);

        return new ExportResultDto
        {
            ExportId = Guid.NewGuid(),
            FileName = fileName,
            Format = "XLSX",
            FilePath = filePath,
            FileSize = new FileInfo(filePath).Length,
            RowCount = rowCount,
            CreatedAt = DateTime.UtcNow,
            Duration = sw.Elapsed,
            DownloadUrl = $"/api/exports/operations/download/{fileName}",
        };
    }

    // -------------------------------------------------------------------
    // Helpers ghi sheet
    // -------------------------------------------------------------------
    private static void StyleHeader(IXLWorksheet ws, int colCount)
    {
        var range = ws.Range(1, 1, 1, colCount);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E40AF");
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.SheetView.FreezeRows(1);
    }

    private static void ApplyNumberFormat(IXLWorksheet ws, int col, int fromRow, string fmt)
    {
        if (fromRow < 2) return;
        ws.Range(fromRow, col, ws.LastRowUsed()?.RowNumber() ?? fromRow, col)
          .Style.NumberFormat.Format = fmt;
    }

    // -------------------------------------------------------------------
    // 1. Doanh thu theo tuyến
    // -------------------------------------------------------------------
    private async Task<int> WriteRevenueByRouteAsync(
        XLWorkbook wb, ExportRequestDto request, CancellationToken ct)
    {
        var data = await _reports.GetRevenueByRouteAsync(request.FromDate, request.ToDate, ct);
        var ws = wb.Worksheets.Add("Doanh thu theo tuyen");

        string[] headers = { "Ma tuyen", "Ten tuyen", "So don", "Tong doanh thu",
                             "Giao thanh cong", "Giao that bai", "Da thu" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

        int r = 2;
        foreach (var d in data)
        {
            ws.Cell(r, 1).Value = d.RouteCode ?? "";
            ws.Cell(r, 2).Value = d.RouteName ?? "";
            ws.Cell(r, 3).Value = d.TotalOrders;
            ws.Cell(r, 4).Value = d.TotalAmount;
            ws.Cell(r, 5).Value = d.DeliveredCount;
            ws.Cell(r, 6).Value = d.FailedCount;
            ws.Cell(r, 7).Value = d.CollectedAmount;
            r++;
        }

        StyleHeader(ws, headers.Length);
        ApplyNumberFormat(ws, 4, 2, "#,##0");
        ApplyNumberFormat(ws, 7, 2, "#,##0");
        ws.Columns().AdjustToContents();
        return Math.Max(0, r - 2);
    }

    // -------------------------------------------------------------------
    // 2. Tỷ lệ giao thành công
    // -------------------------------------------------------------------
    private async Task<int> WriteSuccessRateAsync(
        XLWorkbook wb, ExportRequestDto request, CancellationToken ct)
    {
        var dimension = string.IsNullOrWhiteSpace(request.Status)
            ? "DATE" : request.Status.Trim().ToUpperInvariant();
        var data = await _reports.GetSuccessRateAsync(dimension, request.FromDate, request.ToDate, ct);
        var ws = wb.Worksheets.Add("Ty le giao thanh cong");

        string[] headers = { "Chieu doc", "Khoa", "Tong don", "Giao thanh cong",
                             "Giao that bai", "Ty le (%)" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

        int r = 2;
        foreach (var d in data)
        {
            ws.Cell(r, 1).Value = d.Dimension;
            ws.Cell(r, 2).Value = d.Key;
            ws.Cell(r, 3).Value = d.TotalOrders;
            ws.Cell(r, 4).Value = d.DeliveredCount;
            ws.Cell(r, 5).Value = d.FailedCount;
            ws.Cell(r, 6).Value = d.SuccessRate / 100m;
            r++;
        }

        StyleHeader(ws, headers.Length);
        ApplyNumberFormat(ws, 6, 2, "0.00%");
        ws.Columns().AdjustToContents();
        return Math.Max(0, r - 2);
    }

    // -------------------------------------------------------------------
    // 3. Công nợ theo tuổi
    // -------------------------------------------------------------------
    private async Task<int> WriteDebtAgingAsync(XLWorkbook wb, CancellationToken ct)
    {
        var data = await _reports.GetDebtAgingAsync(ct);
        var ws = wb.Worksheets.Add("Cong no theo tuoi");

        string[] headers = { "Nhom tuoi (ngay)", "So khach hang",
                             "Tong cong no hien tai", "Tong cong no cu" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

        int r = 2;
        foreach (var d in data)
        {
            ws.Cell(r, 1).Value = d.Bucket;
            ws.Cell(r, 2).Value = d.PartnerCount;
            ws.Cell(r, 3).Value = d.TotalDebt;
            ws.Cell(r, 4).Value = d.TotalOldDebt;
            r++;
        }

        StyleHeader(ws, headers.Length);
        ApplyNumberFormat(ws, 3, 2, "#,##0");
        ApplyNumberFormat(ws, 4, 2, "#,##0");
        ws.Columns().AdjustToContents();
        return Math.Max(0, r - 2);
    }

    // -------------------------------------------------------------------
    // 4. Hàng rớt
    // -------------------------------------------------------------------
    private async Task<int> WriteReturnedGoodsAsync(
        XLWorkbook wb, ExportRequestDto request, CancellationToken ct)
    {
        var data = await _reports.GetReturnedGoodsAsync(request.FromDate, request.ToDate, ct);
        var ws = wb.Worksheets.Add("Hang rot");

        string[] headers = { "SKU", "Ten san pham", "Tong so luong rot",
                             "Tong gia tri", "So POD" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

        int r = 2;
        foreach (var d in data)
        {
            ws.Cell(r, 1).Value = d.Sku ?? "";
            ws.Cell(r, 2).Value = d.ProductName ?? "";
            ws.Cell(r, 3).Value = d.TotalReturnedQty;
            ws.Cell(r, 4).Value = d.TotalValue;
            ws.Cell(r, 5).Value = d.PodCount;
            r++;
        }

        StyleHeader(ws, headers.Length);
        ApplyNumberFormat(ws, 3, 2, "#,##0.00");
        ApplyNumberFormat(ws, 4, 2, "#,##0");
        ws.Columns().AdjustToContents();
        return Math.Max(0, r - 2);
    }

    // -------------------------------------------------------------------
    // 5. Tiền mặt theo ca
    // -------------------------------------------------------------------
    private async Task<int> WriteCashByShiftAsync(
        XLWorkbook wb, ExportRequestDto request, CancellationToken ct)
    {
        var data = await _reports.GetCashByShiftAsync(request.FromDate, request.ToDate, ct);
        var ws = wb.Worksheets.Add("Tien mat theo ca");

        string[] headers = { "Ngay", "So chuyen", "Tai xe", "Tien mat don moi",
                             "Tien mat cong no cu", "Tong tien mat", "So POD" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

        int r = 2;
        foreach (var d in data)
        {
            ws.Cell(r, 1).Value = d.ShiftDate;
            ws.Cell(r, 2).Value = d.TripNumber ?? "";
            ws.Cell(r, 3).Value = d.DriverName ?? "";
            ws.Cell(r, 4).Value = d.NewOrderCash;
            ws.Cell(r, 5).Value = d.OldDebtCash;
            ws.Cell(r, 6).Value = d.TotalCash;
            ws.Cell(r, 7).Value = d.PodCount;
            r++;
        }

        StyleHeader(ws, headers.Length);
        ApplyNumberFormat(ws, 4, 2, "#,##0");
        ApplyNumberFormat(ws, 5, 2, "#,##0");
        ApplyNumberFormat(ws, 6, 2, "#,##0");
        ws.Columns().AdjustToContents();
        return Math.Max(0, r - 2);
    }

    // -------------------------------------------------------------------
    // 6. Tồn kho & cảnh báo
    // -------------------------------------------------------------------
    private async Task<int> WriteLowStockAsync(XLWorkbook wb, CancellationToken ct)
    {
        var data = await _reports.GetLowStockAsync(ct);
        var ws = wb.Worksheets.Add("Ton kho canh bao");

        string[] headers = { "Kho", "SKU", "Ten san pham", "Ton thuc te",
                             "Ton toi thieu", "Diem dat lai", "Muc canh bao" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

        int r = 2;
        foreach (var d in data)
        {
            ws.Cell(r, 1).Value = d.WarehouseName ?? "";
            ws.Cell(r, 2).Value = d.Sku ?? "";
            ws.Cell(r, 3).Value = d.ProductName ?? "";
            ws.Cell(r, 4).Value = d.QuantityOnHand;
            ws.Cell(r, 5).Value = d.MinStock;
            ws.Cell(r, 6).Value = d.ReorderPoint;
            ws.Cell(r, 7).Value = d.AlertLevel;
            r++;
        }

        StyleHeader(ws, headers.Length);
        ApplyNumberFormat(ws, 4, 2, "#,##0.00");
        ApplyNumberFormat(ws, 5, 2, "#,##0.00");
        ApplyNumberFormat(ws, 6, 2, "#,##0.00");
        ws.Columns().AdjustToContents();
        return Math.Max(0, r - 2);
    }

    // -------------------------------------------------------------------
    // 7. POD theo chuyến
    // -------------------------------------------------------------------
    private async Task<int> WritePodByTripAsync(
        XLWorkbook wb, ExportRequestDto request, CancellationToken ct)
    {
        var trips = await _dispatcher.GetAllTripsAsync(
            request.FromDate, request.Status, null, ct);

        var ws = wb.Worksheets.Add("POD theo chuyen");

        string[] headers = { "Ngay", "So chuyen", "Tai xe", "Kho", "Trang thai",
                             "Tien mat don moi", "Tien mat cong no cu",
                             "Chuyen khoan", "Tien mat thu", "Chuyen khoan thu",
                             "Ghi no don moi", "Ghi no cong no cu", "Gioi han GPS",
                             "Time lock" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

        int r = 2;
        foreach (var t in trips)
        {
            // Chi lay POD cua chuyen (dung 1 nguon su that: DispatcherService).
            var full = await _dispatcher.GetTripFullAsync(
                Guid.Parse(t.TripId), ct);
            foreach (var pod in full.Pods)
            {
                ws.Cell(r, 1).Value = t.TripDate ?? "";
                ws.Cell(r, 2).Value = t.TripNumber ?? "";
                ws.Cell(r, 3).Value = t.DriverName ?? "";
                ws.Cell(r, 4).Value = t.WarehouseName ?? "";
                ws.Cell(r, 5).Value = pod.DeliveryStatus ?? "";
                ws.Cell(r, 6).Value = pod.NewOrderCash;
                ws.Cell(r, 7).Value = pod.OldDebtCash;
                ws.Cell(r, 8).Value = pod.NewOrderCredit;
                ws.Cell(r, 9).Value = pod.NewOrderTransfer;
                ws.Cell(r, 10).Value = pod.OldDebtTransfer;
                ws.Cell(r, 11).Value = pod.NewOrderCredit;
                ws.Cell(r, 12).Value = pod.OldDebtCredit;
                ws.Cell(r, 13).Value = pod.GpsGateResult ?? "";
                ws.Cell(r, 14).Value = pod.TimeGuardResult ?? "";
                r++;
            }
        }

        StyleHeader(ws, headers.Length);
        for (int c = 6; c <= 12; c++) ApplyNumberFormat(ws, c, 2, "#,##0");
        ws.Columns().AdjustToContents();
        return Math.Max(0, r - 2);
    }

    // -------------------------------------------------------------------
    // 8. Phiếu đối soát cuối ngày (EOD)
    // -------------------------------------------------------------------
    private async Task<int> WriteEodAsync(
        XLWorkbook wb, ExportRequestDto request, CancellationToken ct)
    {
        var list = await _eod.ListAsync(request.FromDate, null, request.Status, ct);

        // --- Sheet 1: phieu ---
        var ws = wb.Worksheets.Add("Phieu doi soat");
        string[] headers = { "Ngay", "So phieu", "Tai xe", "Tien mat ky vong",
                             "Tien mat thuc te", "Chenh lech tien mat",
                             "Ly do chenh lech tien mat", "Xu ly chenh lech tien mat",
                             "Muc doi chieu", "Giao thanh cong", "Giao that bai",
                             "Chi phi chuyen", "Trang thai", "Nguoi duyet" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

        int r = 2;
        foreach (var e in list)
        {
            ws.Cell(r, 1).Value = e.ReconciliationDate;
            ws.Cell(r, 2).Value = e.Id;
            ws.Cell(r, 3).Value = e.DriverName ?? "";
            ws.Cell(r, 4).Value = e.ExpectedCash;
            ws.Cell(r, 5).Value = e.ActualCash;
            ws.Cell(r, 6).Value = e.CashDifference;
            ws.Cell(r, 7).Value = e.CashVarianceReason ?? "";
            ws.Cell(r, 8).Value = e.CashVarianceAction ?? "";
            ws.Cell(r, 9).Value = e.ItemsDifference;
            ws.Cell(r, 10).Value = e.SuccessfulOrders;
            ws.Cell(r, 11).Value = e.FailedOrders;
            ws.Cell(r, 12).Value = e.TripExpenses;
            ws.Cell(r, 13).Value = e.Status;
            ws.Cell(r, 14).Value = e.ApprovedBy ?? "";
            r++;
        }
        StyleHeader(ws, headers.Length);
        ApplyNumberFormat(ws, 4, 2, "#,##0");
        ApplyNumberFormat(ws, 5, 2, "#,##0");
        ApplyNumberFormat(ws, 6, 2, "#,##0");
        ApplyNumberFormat(ws, 12, 2, "#,##0");
        ws.Columns().AdjustToContents();

        // --- Sheet 2: chi tiet dong ---
        var wsItems = wb.Worksheets.Add("Chi tiet doi soat");
        string[] itemHeaders = { "Ngay", "So phieu", "Khach hang", "Don hang",
                                 "Tien mat ky vong", "Tien mat thu",
                                 "Da thu cong no cu", "Chenh lech tien mat",
                                 "SL don xuat", "SL giao", "SL tra", "Chenh lech SL",
                                 "Trang thai" };
        for (int i = 0; i < itemHeaders.Length; i++) wsItems.Cell(1, i + 1).Value = itemHeaders[i];

        int ri = 2;
        foreach (var e in list)
        {
            foreach (var it in e.Items)
            {
                wsItems.Cell(ri, 1).Value = e.ReconciliationDate;
                wsItems.Cell(ri, 2).Value = e.Id;
                wsItems.Cell(ri, 3).Value = it.PartnerName ?? "";
                wsItems.Cell(ri, 4).Value = it.SalesOrderId ?? "";
                wsItems.Cell(ri, 5).Value = it.ExpectedCash;
                wsItems.Cell(ri, 6).Value = it.CollectedCash;
                wsItems.Cell(ri, 7).Value = it.OldDebtCollected;
                wsItems.Cell(ri, 8).Value = it.CashDifference;
                wsItems.Cell(ri, 9).Value = it.ExpectedQty;
                wsItems.Cell(ri, 10).Value = it.DeliveredQty;
                wsItems.Cell(ri, 11).Value = it.ReturnedQty;
                wsItems.Cell(ri, 12).Value = it.ItemsDifference;
                wsItems.Cell(ri, 13).Value = it.ReconciliationStatus;
                ri++;
            }
        }
        StyleHeader(wsItems, itemHeaders.Length);
        for (int c = 5; c <= 8; c++) ApplyNumberFormat(wsItems, c, 2, "#,##0");
        wsItems.Columns().AdjustToContents();

        return Math.Max(0, r - 2);
    }
}
