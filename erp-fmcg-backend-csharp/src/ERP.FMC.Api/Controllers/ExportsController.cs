using ERP.FMC.Application.DTOs;
using ERP.FMC.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace ERP.FMC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ExportsController : ControllerBase
{
    private readonly ExportService _exportService;
    private readonly ILogger<ExportsController> _logger;

    public ExportsController(ExportService exportService, ILogger<ExportsController> logger)
    {
        _exportService = exportService;
        _logger = logger;
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
        var filePath = Path.Combine(exportFolder, fileName);

        if (!System.IO.File.Exists(filePath))
            return NotFound(new { error = "File not found" });

        var contentType = fileName.EndsWith(".xlsx")
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "text/csv";

        var bytes = System.IO.File.ReadAllBytes(filePath);
        return File(bytes, contentType, fileName);
    }
}
