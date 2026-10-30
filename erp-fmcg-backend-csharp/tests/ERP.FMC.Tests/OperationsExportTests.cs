using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Services;
using ERP.FMC.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ERP.FMC.Tests;

// ===================================================================
// TEST XUẤT EXCEL BÁO CÁO VẬN HÀNH
// -------------------------------------------------------------------
// Mục tiêu:
//   1. Mỗi loại báo cáo vận hành sinh được file .xlsx thật.
//   2. File có đúng tên sheet kỳ vọng.
//   3. Số dòng dữ liệu = số bản ghi mà `IReportService` trả về
//      (Excel và màn hình phải khớp — cùng 1 nguồn sự thật).
//   4. Loại báo cáo không hỗ trợ -> ném `NotSupportedException`
//      (không ghi file rác).
//
// Ghi chú về mock:
//   `IReportService` / `IEodReconciliationService` / `IDispatcherService`
//   được mock bằng Moq. Test KHONG cham DB -> chay nhanh, khong phu
//   thuoc moi truong.
// ===================================================================

public class OperationsExportTests
{
    private static OperationsExportService BuildService(
        IReadOnlyList<RevenueByRouteDto>? revenue = null,
        IReadOnlyList<DeliverySuccessRateDto>? success = null,
        IReadOnlyList<DebtAgingDto>? aging = null,
        IReadOnlyList<ReturnedGoodsDto>? returned = null,
        IReadOnlyList<CashByShiftDto>? cash = null,
        IReadOnlyList<LowStockDto>? lowStock = null,
        IReadOnlyList<EodReconciliationDto>? eod = null)
    {
        var reports = new Mock<IReportService>();
        reports.Setup(r => r.GetRevenueByRouteAsync(It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(revenue ?? Array.Empty<RevenueByRouteDto>());
        reports.Setup(r => r.GetSuccessRateAsync(It.IsAny<string>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(success ?? Array.Empty<DeliverySuccessRateDto>());
        reports.Setup(r => r.GetDebtAgingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(aging ?? Array.Empty<DebtAgingDto>());
        reports.Setup(r => r.GetReturnedGoodsAsync(It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returned ?? Array.Empty<ReturnedGoodsDto>());
        reports.Setup(r => r.GetCashByShiftAsync(It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cash ?? Array.Empty<CashByShiftDto>());
        reports.Setup(r => r.GetLowStockAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(lowStock ?? Array.Empty<LowStockDto>());

        var eodSvc = new Mock<IEodReconciliationService>();
        eodSvc.Setup(e => e.ListAsync(It.IsAny<DateTime?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(eod ?? Array.Empty<EodReconciliationDto>());

        var dispatcher = new Mock<IDispatcherService>();
        dispatcher.Setup(d => d.GetAllTripsAsync(It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DeliveryTripDto>());

        return new OperationsExportService(
            reports.Object, eodSvc.Object, dispatcher.Object,
            NullLogger<OperationsExportService>.Instance);
    }

    private static ExportRequestDto Request() => new()
    {
        FromDate = new DateTime(2026, 10, 1),
        ToDate = new DateTime(2026, 10, 30),
    };

    // -------------------------------------------------------------------
    // 1. Doanh thu theo tuyến -> file .xlsx + sheet + đúng số dòng
    // -------------------------------------------------------------------
    [Fact]
    public async Task Export_RevenueByRoute_TaoFileExcelDungSoDong()
    {
        var data = new List<RevenueByRouteDto>
        {
            new() { RouteCode = "R01", RouteName = "Tuyen 1", TotalOrders = 10,
                    TotalAmount = 1_000_000, DeliveredCount = 8, FailedCount = 2,
                    CollectedAmount = 500_000 },
            new() { RouteCode = "R02", RouteName = "Tuyen 2", TotalOrders = 5,
                    TotalAmount = 400_000, DeliveredCount = 5, FailedCount = 0,
                    CollectedAmount = 400_000 },
        };

        var svc = BuildService(revenue: data);
        var result = await svc.ExportAsync("REVENUE_BY_ROUTE", Request());

        Assert.True(File.Exists(result.FilePath));
        Assert.Equal("XLSX", result.Format);
        Assert.Equal(2, result.RowCount);
        Assert.True(result.FileSize > 0);

        // Sheet "Doanh thu theo tuyen" + header + 2 dong du lieu
        using var wb = new ClosedXML.Excel.XLWorkbook(result.FilePath);
        var ws = wb.Worksheets.First();
        Assert.Contains("tuyen", ws.Name.ToLowerInvariant());
        Assert.Equal(3, ws.LastRowUsed()!.RowNumber());

        File.Delete(result.FilePath);
    }

    // -------------------------------------------------------------------
    // 2. Công nợ theo tuổi
    // -------------------------------------------------------------------
    [Fact]
    public async Task Export_DebtAging_GhiDungBonNhom()
    {
        var data = new List<DebtAgingDto>
        {
            new() { Bucket = "0-30", PartnerCount = 100, TotalDebt = 10_000_000, TotalOldDebt = 0 },
            new() { Bucket = "31-60", PartnerCount = 50, TotalDebt = 5_000_000, TotalOldDebt = 1_000_000 },
            new() { Bucket = "61-90", PartnerCount = 20, TotalDebt = 2_000_000, TotalOldDebt = 500_000 },
            new() { Bucket = ">90", PartnerCount = 5, TotalDebt = 1_000_000, TotalOldDebt = 800_000 },
        };

        var svc = BuildService(aging: data);
        var result = await svc.ExportAsync("DEBT_AGING", Request());

        Assert.Equal(4, result.RowCount);
        using var wb = new ClosedXML.Excel.XLWorkbook(result.FilePath);
        var ws = wb.Worksheets.First();
        Assert.Equal(5, ws.LastRowUsed()!.RowNumber());

        File.Delete(result.FilePath);
    }

    // -------------------------------------------------------------------
    // 3. Tiền mặt theo ca
    // -------------------------------------------------------------------
    [Fact]
    public async Task Export_CashByShift_TongTienMatDung()
    {
        var data = new List<CashByShiftDto>
        {
            new() { DriverName = "Nguyen Van A", TripNumber = "TRIP-1",
                    ShiftDate = "2026-10-30", NewOrderCash = 100_000,
                    OldDebtCash = 50_000, TotalCash = 150_000, PodCount = 3 },
        };

        var svc = BuildService(cash: data);
        var result = await svc.ExportAsync("CASH_BY_SHIFT", Request());

        Assert.Equal(1, result.RowCount);

        using var wb = new ClosedXML.Excel.XLWorkbook(result.FilePath);
        var ws = wb.Worksheets.First();
        // Cot F = Tong tien mat -> 150000
        Assert.Equal(150_000d, ws.Cell(2, 6).GetDouble(), 2);

        File.Delete(result.FilePath);
    }

    // -------------------------------------------------------------------
    // 4. Tồn kho cảnh báo
    // -------------------------------------------------------------------
    [Fact]
    public async Task Export_LowStock_GhiSoLuong()
    {
        var data = new List<LowStockDto>
        {
            new() { Sku = "10000013", ProductName = "Aquafina", QuantityOnHand = 3,
                    MinStock = 10, ReorderPoint = 20, AlertLevel = "LOW" },
            new() { Sku = "10000042", ProductName = "Pepsi", QuantityOnHand = 25,
                    MinStock = 10, ReorderPoint = 20, AlertLevel = "REORDER" },
        };

        var svc = BuildService(lowStock: data);
        var result = await svc.ExportAsync("LOW_STOCK", Request());

        Assert.Equal(2, result.RowCount);
        File.Delete(result.FilePath);
    }

    // -------------------------------------------------------------------
    // 5. Loại báo cáo không hỗ trợ -> NotSupportedException
    // -------------------------------------------------------------------
    [Fact]
    public async Task Export_LoaiKhongHoTro_ThrowNotSupported()
    {
        var svc = BuildService();
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => svc.ExportAsync("KHONG_CO", Request()));
        Assert.Contains("REVENUE_BY_ROUTE", ex.Message);
    }

    // -------------------------------------------------------------------
    // 6. Tên file chứa loại báo cáo + khoảng thời gian
    // -------------------------------------------------------------------
    [Fact]
    public async Task Export_TenFileChuaLoaiBaoCao()
    {
        var svc = BuildService();
        var result = await svc.ExportAsync("REVENUE_BY_ROUTE", Request());

        Assert.Contains("REVENUE_BY_ROUTE", result.FileName);
        Assert.Contains("20261001", result.FileName);
        Assert.Contains("20261030", result.FileName);

        File.Delete(result.FilePath);
    }
}
