using ERP.FMC.Application.DTOs;
using ERP.FMC.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.FMC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ExportsController : ControllerBase
{
    private readonly ExportService _exportService;
    private readonly ILogger<ExportsController> _logger;

    // ★ PHASE 6B.5 — xuất Excel cho báo cáo vận hành (8 loại).
    private readonly Phase6ExportService _phase6Export;
    private readonly ILogger<ExportsController> _loggerPhase6;

    public ExportsController(
        ExportService exportService,
        Phase6ExportService phase6Export,
        ILogger<ExportsController> logger)
    {
        _exportService = exportService;
        _phase6Export = phase6Export;
        _logger = logger;
        _loggerPhase6 = logger;
    }

    /// <summary>
    /// Export partners/products/orders/inventory ra Excel.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ExportResultDto>> Export(
        [FromBody] ExportRequestDto request, CancellationToken ct)
    {
        try
        {
            var result = request.ExportType.ToUpper() switch
            {
                "PARTNERS" => await _exportService.ExportPartnersAsync(request, ct),
                "PRODUCTS" => await _exportService.ExportProductsAsync(request, ct),
                "ORDERS" => await _exportService.ExportOrdersAsync(request, ct),
                "INVENTORY" => await _exportService.ExportInventoryAsync(request, ct),
                "REVENUE" => await _exportService.ExportRevenueAsync(request, ct),
                _ => throw new NotSupportedException($"Export type '{request.ExportType}' not supported")
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed: {Type}", request.ExportType);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get available export templates.
    /// </summary>
    [HttpGet("templates")]
    public ActionResult<List<ExportTemplateDto>> GetTemplates()
    {
        return Ok(new List<ExportTemplateDto>
        {
            ExportTemplates.PartnersTemplate,
            ExportTemplates.ProductsTemplate,
            ExportTemplates.OrdersTemplate,
            ExportTemplates.InventoryTemplate,
            ExportTemplates.RevenueTemplate
        });
    }

    /// <summary>
    /// Download exported file.
    /// </summary>
    [HttpGet("{fileName}")]
    public ActionResult Download(string fileName)
    {
        var exportFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ERP_FMCG", "Exports");

        // ★ Sanitize fileName — chống path traversal ("../../etc/passwd").
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || !string.Equals(safeName, fileName, StringComparison.Ordinal))
            return BadRequest(new { error = "INVALID_FILE_NAME" });

        var filePath = Path.Combine(exportFolder, safeName);

        if (!System.IO.File.Exists(filePath))
            return NotFound(new { error = "File not found" });

        var contentType = fileName.EndsWith(".xlsx")
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "text/csv";

        var bytes = System.IO.File.ReadAllBytes(filePath);
        return File(bytes, contentType, fileName);
    }

    // ===================================================================
    // ★ PHASE 6B.5 — XUẤT EXCEL CHO BÁO CÁO VẬN HÀNH
    // -------------------------------------------------------------------
    //   POST /api/exports/phase6/{type}  — sinh file .xlsx
    //   GET  /api/exports/phase6/download/{fileName} — tai file
    //
    //   Type: REVENUE_BY_ROUTE | SUCCESS_RATE | DEBT_AGING | RETURNED_GOODS |
    //          CASH_BY_SHIFT | LOW_STOCK | POD_BY_TRIP | EOD_RECONCILIATION
    // ===================================================================

    /// <summary>Sinh báo cáo Phase 6 ra Excel (.xlsx).</summary>
    [HttpPost("phase6/{type}")]
    [Authorize(Roles = "Admin,Dispatcher,Manager,Accountant")]
    public async Task<ActionResult<ExportResultDto>> ExportPhase6(
        string type,
        [FromBody] ExportRequestDto request,
        CancellationToken ct)
    {
        try
        {
            var result = await _phase6Export.ExportAsync(type, request, ct);
            return Ok(result);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { error = "UNSUPPORTED_EXPORT_TYPE", message = ex.Message });
        }
        catch (Exception ex)
        {
            _loggerPhase6.LogError(ex, "Phase6 export failed: {Type}", type);
            return BadRequest(new { error = "EXPORT_FAILED", message = ex.Message });
        }
    }

    /// <summary>Tai file Excel báo cáo Phase 6.</summary>
    [HttpGet("phase6/download/{fileName}")]
    [Authorize(Roles = "Admin,Dispatcher,Manager,Accountant")]
    public ActionResult DownloadPhase6(string fileName)
    {
        var exportFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ERP_FMCG", "Exports");
        var filePath = Path.Combine(exportFolder, Path.GetFileName(fileName));

        if (!System.IO.File.Exists(filePath))
            return NotFound(new { error = "NOT_FOUND", message = "Khong tim thay file." });

        var bytes = System.IO.File.ReadAllBytes(filePath);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    /// <summary>Danh sách loại báo cáo Phase 6 có thể xuất.</summary>
    [HttpGet("phase6/types")]
    [Authorize(Roles = "Admin,Dispatcher,Manager,Accountant")]
    public ActionResult<object> GetPhase6Types()
    {
        return Ok(new
        {
            types = new[]
            {
                "REVENUE_BY_ROUTE",
                "SUCCESS_RATE",
                "DEBT_AGING",
                "RETURNED_GOODS",
                "CASH_BY_SHIFT",
                "LOW_STOCK",
                "POD_BY_TRIP",
                "EOD_RECONCILIATION",
            }
        });
    }
}
