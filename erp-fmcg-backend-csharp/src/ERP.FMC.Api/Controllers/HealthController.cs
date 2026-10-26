using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.FMC.Infrastructure.Data;

namespace ERP.FMC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;

    public HealthController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Health check - verify DB connection + return basic stats.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> Check()
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync();
            if (!canConnect) return StatusCode(503, new { status = "DOWN", db = "unreachable" });

            var stats = new Dictionary<string, object>
            {
                ["status"] = "UP",
                ["db"] = "ok",
                ["server"] = Environment.MachineName,
                ["timestamp"] = DateTime.UtcNow
            };

            try
            {
                stats["partners"] = await _db.Partners.CountAsync();
                stats["products"] = await _db.Products.CountAsync();
                stats["salesOrders"] = await _db.SalesOrders.CountAsync();
                stats["salesOrderItems"] = await _db.SalesOrderItems.CountAsync();
                stats["warehouses"] = await _db.Warehouses.CountAsync();
                stats["users"] = await _db.UserAccounts.CountAsync();
            }
            catch (Exception ex)
            {
                stats["dbStatsError"] = ex.Message;
            }

            return Ok(stats);
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { status = "DOWN", error = ex.Message });
        }
    }
}
