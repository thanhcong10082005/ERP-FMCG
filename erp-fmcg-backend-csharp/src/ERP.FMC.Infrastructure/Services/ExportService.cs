using System.Diagnostics;
using System.Globalization;
using ClosedXML.Excel;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Infrastructure.Services;

/// <summary>
/// Export Service - Excel export using ClosedXML.
/// </summary>
public class ExportService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ExportService> _logger;
    private readonly string _exportFolder;

    public ExportService(IUnitOfWork uow, ILogger<ExportService> logger)
    {
        _uow = uow;
        _logger = logger;
        _exportFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ERP_FMCG", "Exports");
        Directory.CreateDirectory(_exportFolder);
    }

    public async Task<ExportResultDto> ExportPartnersAsync(ExportRequestDto request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var fileName = $"Partners_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(_exportFolder, fileName);

        var all = await _uow.Partners.GetAllAsync();
        var data = request.IsActive.HasValue ? all.Where(p => p.IsActive == request.IsActive.Value).ToList() : all.ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Partners");

        ws.Cell(1, 1).Value = "Mã KH";
        ws.Cell(1, 2).Value = "Tên khách hàng";
        ws.Cell(1, 3).Value = "Loại";
        ws.Cell(1, 4).Value = "Điện thoại";
        ws.Cell(1, 5).Value = "Email";
        ws.Cell(1, 6).Value = "Địa chỉ";
        ws.Cell(1, 7).Value = "Hoạt động";

        for (int i = 0; i < data.Count; i++)
        {
            var p = data[i];
            ws.Cell(i + 2, 1).Value = p.PartnerCode ?? "";
            ws.Cell(i + 2, 2).Value = p.PartnerName ?? "";
            ws.Cell(i + 2, 3).Value = p.PartnerType ?? "";
            ws.Cell(i + 2, 4).Value = p.Phone ?? "";
            ws.Cell(i + 2, 5).Value = p.Email ?? "";
            ws.Cell(i + 2, 6).Value = p.Address ?? "";
            ws.Cell(i + 2, 7).Value = p.IsActive ? "Có" : "Không";
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
        sw.Stop();

        return new ExportResultDto
        {
            ExportId = Guid.NewGuid(),
            FileName = fileName,
            Format = "XLSX",
            FilePath = filePath,
            FileSize = new FileInfo(filePath).Length,
            RowCount = data.Count,
            CreatedAt = DateTime.UtcNow,
            Duration = sw.Elapsed,
            DownloadUrl = $"/api/exports/{fileName}"
        };
    }

    public async Task<ExportResultDto> ExportProductsAsync(ExportRequestDto request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var fileName = $"Products_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(_exportFolder, fileName);

        var all = await _uow.Products.GetAllAsync();
        var data = request.IsActive.HasValue ? all.Where(p => p.IsActive == request.IsActive.Value).ToList() : all.ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Products");

        ws.Cell(1, 1).Value = "SKU";
        ws.Cell(1, 2).Value = "Tên sản phẩm";
        ws.Cell(1, 3).Value = "Giá";
        ws.Cell(1, 4).Value = "ĐVT";
        ws.Cell(1, 5).Value = "VAT%";
        ws.Cell(1, 6).Value = "Hoạt động";

        for (int i = 0; i < data.Count; i++)
        {
            var p = data[i];
            ws.Cell(i + 2, 1).Value = p.Sku ?? "";
            ws.Cell(i + 2, 2).Value = p.ProductName ?? "";
            ws.Cell(i + 2, 3).Value = p.BasePrice;
            ws.Cell(i + 2, 4).Value = p.BaseUom ?? "";
            ws.Cell(i + 2, 5).Value = p.VatRate;
            ws.Cell(i + 2, 6).Value = p.IsActive ? "Có" : "Không";
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
        sw.Stop();

        return new ExportResultDto
        {
            ExportId = Guid.NewGuid(),
            FileName = fileName,
            Format = "XLSX",
            FilePath = filePath,
            FileSize = new FileInfo(filePath).Length,
            RowCount = data.Count,
            CreatedAt = DateTime.UtcNow,
            Duration = sw.Elapsed,
            DownloadUrl = $"/api/exports/{fileName}"
        };
    }

    public async Task<ExportResultDto> ExportOrdersAsync(ExportRequestDto request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var fileName = $"Orders_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(_exportFolder, fileName);

        var all = await _uow.SalesOrders.GetAllAsync();
        var data = all.ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Orders");

        ws.Cell(1, 1).Value = "Số đơn";
        ws.Cell(1, 2).Value = "Ngày đặt";
        ws.Cell(1, 3).Value = "Trạng thái";
        ws.Cell(1, 4).Value = "Tổng tiền";

        for (int i = 0; i < data.Count; i++)
        {
            var o = data[i];
            ws.Cell(i + 2, 1).Value = o.OrderNumber ?? "";
            ws.Cell(i + 2, 2).Value = o.OrderDate;
            ws.Cell(i + 2, 3).Value = o.Status ?? "";
            ws.Cell(i + 2, 4).Value = o.TotalAmount;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
        sw.Stop();

        return new ExportResultDto
        {
            ExportId = Guid.NewGuid(),
            FileName = fileName,
            Format = "XLSX",
            FilePath = filePath,
            FileSize = new FileInfo(filePath).Length,
            RowCount = data.Count,
            CreatedAt = DateTime.UtcNow,
            Duration = sw.Elapsed,
            DownloadUrl = $"/api/exports/{fileName}"
        };
    }

    public async Task<ExportResultDto> ExportInventoryAsync(ExportRequestDto request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var fileName = $"Inventory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(_exportFolder, fileName);

        var all = await _uow.InventoryBalances.GetAllAsync();
        var data = all.ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Inventory");

        ws.Cell(1, 1).Value = "Warehouse";
        ws.Cell(1, 2).Value = "Product";
        ws.Cell(1, 3).Value = "On Hand";
        ws.Cell(1, 4).Value = "Reserved";
        ws.Cell(1, 5).Value = "Available";

        for (int i = 0; i < data.Count; i++)
        {
            var ib = data[i];
            ws.Cell(i + 2, 1).Value = ib.WarehouseId.ToString();
            ws.Cell(i + 2, 2).Value = ib.ProductId.ToString();
            ws.Cell(i + 2, 3).Value = ib.OnHand;
            ws.Cell(i + 2, 4).Value = ib.Reserved;
            ws.Cell(i + 2, 5).Value = ib.Available;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
        sw.Stop();

        return new ExportResultDto
        {
            ExportId = Guid.NewGuid(),
            FileName = fileName,
            Format = "XLSX",
            FilePath = filePath,
            FileSize = new FileInfo(filePath).Length,
            RowCount = data.Count,
            CreatedAt = DateTime.UtcNow,
            Duration = sw.Elapsed,
            DownloadUrl = $"/api/exports/{fileName}"
        };
    }

    public async Task<ExportResultDto> ExportRevenueAsync(ExportRequestDto request, CancellationToken ct = default)
    {
        return await ExportOrdersAsync(request, ct);
    }
}
