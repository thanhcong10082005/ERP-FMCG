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

    // ===================================================================
    // ★ PHASE 6C.8 — HEALTH CHECK SÂU (deep health check)
    // -------------------------------------------------------------------
    // Health check cơ bản chỉ kiểm tra "DB có kết nối được không".
    // Nó KHÔNG phát hiện được các lỗi đã xảy ra thật:
    //   - Bảng `delivery_*` chưa được migrate ⇒ API giao hàng sẽ 500
    //   - `audit_logs` không ghi được ⇒ mất dấu vết kiểm toán
    //   - `inventory_balances` lệch với `stock_ledger` ⇒ tồn kho sai
    //
    // Endpoint này kiểm tra SÂU: tồn tại bảng, ghi được audit, khớp tồn kho.
    // Trả 503 nếu có bất kỳ check nghiêm trọng nào FAIL.
    // ===================================================================
    [HttpGet("deep")]
    public async Task<ActionResult> DeepCheck()
    {
        var checks = new List<object>();
        var criticalFailures = 0;

        // ---- 1. Kết nối DB ----
        var dbOk = await _db.Database.CanConnectAsync();
        checks.Add(new
        {
            name = "db_connection",
            status = dbOk ? "PASS" : "FAIL",
            detail = dbOk ? "Kết nối OK" : "Không kết nối được DB"
        });
        if (!dbOk)
            return StatusCode(503, new { status = "DOWN", checks });

        // ---- 2. Bảng delivery_* tồn tại ----
        //   Nếu thiếu ⇒ API giao hàng (POST /api/sales/pod) sẽ lỗi 500.
        var requiredTables = new[]
        {
            "delivery_trips", "delivery_trip_orders", "delivery_pod",
            "pod_items", "pod_attachments", "eod_reconciliations",
            "eod_reconciliation_items"
        };

        foreach (var table in requiredTables)
        {
            var exists = await TableExistsAsync(table);
            if (!exists) criticalFailures++;
            checks.Add(new
            {
                name = $"table_tenant.{table}",
                status = exists ? "PASS" : "FAIL",
                detail = exists ? "Tồn tại" : "THIẾU BẢNG — cần chạy migration Phase 6"
            });
        }

        // ---- 3. audit_logs ghi được ----
        //   Ghi thử 1 dòng rồi xoá ⇒ xác nhận constraint + quyền ghi.
        var auditOk = true;
        string auditDetail = "Ghi được";
        try
        {
            var probe = new ERP.FMC.Domain.Entities.AuditLog
            {
                TableName = "_health_check",
                RecordId = Guid.Empty,
                Action = "PROBE",
                Operation = "HEALTHCHECK",
                NewValues = "{}",
                Source = "HEALTHCHECK",
                RequestId = Guid.NewGuid().ToString(),
                Notes = "Phase 6C.8 deep health check probe (tự xoá)",
                CreatedAt = DateTime.UtcNow,
            };
            _db.AuditLogs.Add(probe);
            await _db.SaveChangesAsync();
            _db.AuditLogs.Remove(probe);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            auditOk = false;
            auditDetail = $"Không ghi được audit_logs: {ex.Message}";
            criticalFailures++;
        }
        checks.Add(new { name = "audit_logs_writable", status = auditOk ? "PASS" : "FAIL", detail = auditDetail });

        // ---- 4. Tồn kho khớp sổ kho ----
        //   inventory_balances.quantity_on_hand phải bằng
        //   SUM(quantity_in) - SUM(quantity_out) của stock_ledger.
        try
        {
            var mismatch = await _db.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS Value FROM (
                    SELECT b.id
                    FROM tenant.inventory_balances b
                    LEFT JOIN (
                        SELECT warehouse_id, product_id,
                               SUM(quantity_in) - SUM(quantity_out) AS qty
                        FROM tenant.stock_ledger
                        GROUP BY warehouse_id, product_id
                    ) l ON l.warehouse_id = b.warehouse_id AND l.product_id = b.product_id
                    WHERE ABS(b.quantity_on_hand - ISNULL(l.qty, 0)) > 0.01
                ) x
                """).ToListAsync();

            var mismatchCount = mismatch.FirstOrDefault();
            if (mismatchCount > 0) criticalFailures++;
            checks.Add(new
            {
                name = "inventory_matches_ledger",
                status = mismatchCount == 0 ? "PASS" : "FAIL",
                detail = mismatchCount == 0
                    ? "Tồn kho khớp sổ kho"
                    : $"{mismatchCount} bản ghi tồn kho KHÔNG khớp sổ kho"
            });
        }
        catch (Exception ex)
        {
            criticalFailures++;
            checks.Add(new
            {
                name = "inventory_matches_ledger",
                status = "FAIL",
                detail = $"Không kiểm tra được: {ex.Message}"
            });
        }

        // ---- 5. Số liệu nghiệp vụ (thông tin, không quyết định status) ----
        try
        {
            checks.Add(new
            {
                name = "business_stats",
                status = "INFO",
                detail = new
                {
                    delivery_pods = await _db.DeliveryPods.CountAsync(),
                    trips = await _db.DeliveryTrips.CountAsync(),
                    stock_ledger_rows = await _db.StockLedgers.CountAsync(),
                    inventory_balances = await _db.InventoryBalances.CountAsync(),
                    eod_reconciliations = await _db.EodReconciliations.CountAsync(),
                }
            });
        }
        catch
        {
            // Không quan trọng — bỏ qua.
        }

        var overall = criticalFailures == 0 ? "UP" : "DEGRADED";

        return criticalFailures == 0
            ? Ok(new { status = overall, checks, timestamp = DateTime.UtcNow })
            : StatusCode(503, new
            {
                status = overall,
                critical_failures = criticalFailures,
                checks,
                timestamp = DateTime.UtcNow
            });
    }

    /// <summary>Kiểm tra 1 bảng trong schema `tenant` có tồn tại không.</summary>
    private async Task<bool> TableExistsAsync(string tableName)
    {
        var rows = await _db.Database.SqlQuery<int>($"""
            SELECT CASE WHEN OBJECT_ID(N'tenant.{tableName}', N'U') IS NULL THEN 0 ELSE 1 END AS Value
            """).ToListAsync();
        return rows.FirstOrDefault() == 1;
    }
}
