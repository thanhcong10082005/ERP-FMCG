using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ERP.FMC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class UatController : ControllerBase
{
    private readonly IUatTestService _uatService;

    public UatController(IUatTestService uatService) => _uatService = uatService;

    [HttpPost("run")]
    public async Task<ActionResult<TestSuiteRunDto>> RunAll(CancellationToken ct)
        => Ok(await _uatService.RunAllAsync(ct));

    [HttpGet("cases")]
    public ActionResult<List<TestCaseDto>> GetCases() => Ok(new List<TestCaseDto>
    {
        new() { Id = "TC01", Name = "Import 1000 partners", Category = "IMPORT", Severity = "CRITICAL" },
        new() { Id = "TC02", Name = "Import with errors", Category = "IMPORT", Severity = "HIGH" },
        new() { Id = "TC03", Name = "Invalid SKU validation", Category = "VALIDATION", Severity = "HIGH" },
        new() { Id = "TC04", Name = "Export inventory <5s", Category = "EXPORT", Severity = "HIGH" },
        new() { Id = "TC05", Name = "Create order updates inventory", Category = "BUSINESS", Severity = "CRITICAL" },
        new() { Id = "TC06", Name = "Edit order temporal history", Category = "TEMPORAL", Severity = "CRITICAL" },
        new() { Id = "TC07", Name = "Query temporal history", Category = "TEMPORAL", Severity = "HIGH" },
        new() { Id = "TC08", Name = "Import 10k orders <5s", Category = "PERFORMANCE", Severity = "CRITICAL" },
        new() { Id = "TC09", Name = "Export revenue <10s", Category = "EXPORT", Severity = "HIGH" },
        new() { Id = "TC10", Name = "Multi-UOM calculation", Category = "BUSINESS", Severity = "NORMAL" },
        new() { Id = "TC11", Name = "Partner CRUD", Category = "CRUD", Severity = "CRITICAL" },
        new() { Id = "TC12", Name = "Product CRUD", Category = "CRUD", Severity = "CRITICAL" },
        new() { Id = "TC13", Name = "SalesOrder CRUD", Category = "CRUD", Severity = "CRITICAL" },
        new() { Id = "TC14", Name = "Price List query", Category = "QUERY", Severity = "NORMAL" },
        new() { Id = "TC15", Name = "Audit Log write", Category = "AUDIT", Severity = "CRITICAL" },
        new() { Id = "TC16", Name = "Health Check", Category = "INFRA", Severity = "CRITICAL" },
        new() { Id = "TC17", Name = "BulkCopy 180k <1s", Category = "PERFORMANCE", Severity = "CRITICAL" },
        new() { Id = "TC18", Name = "Validation engine", Category = "VALIDATION", Severity = "HIGH" },
        new() { Id = "TC19", Name = "Batch resolve", Category = "STAGING", Severity = "HIGH" },
        new() { Id = "TC20", Name = "Rollback import", Category = "STAGING", Severity = "HIGH" }
    });
}

[ApiController]
[Route("api/smoke")]
[Produces("application/json")]
public class SmokeController : ControllerBase
{
    private readonly ISmokeTestService _smokeService;
    public SmokeController(ISmokeTestService smokeService) => _smokeService = smokeService;

    [HttpPost("run")]
    public async Task<ActionResult<SmokeTestSuiteDto>> Run(CancellationToken ct)
        => Ok(await _smokeService.RunAsync(ct));
}

[ApiController]
[Route("api/golive")]
[Produces("application/json")]
public class GoLiveController : ControllerBase
{
    private readonly IGoLiveService _goLiveService;
    private readonly ISmokeTestService _smokeService;

    public GoLiveController(IGoLiveService goLiveService, ISmokeTestService smokeService)
    {
        _goLiveService = goLiveService;
        _smokeService = smokeService;
    }

    [HttpGet("status")]
    public ActionResult<GoLiveStatusDto> GetStatus() => Ok(_goLiveService.GetStatus());

    [HttpPost("lock-nestjs")]
    public async Task<ActionResult> LockNestJs(CancellationToken ct)
    {
        var ok = await _goLiveService.LockNestJsAsync(ct);
        return ok ? Ok(new { locked = true }) : BadRequest();
    }

    [HttpPost("final-migration")]
    public async Task<ActionResult<MigrationResultDto>> FinalMigration(CancellationToken ct)
        => Ok(await _goLiveService.RunFinalMigrationAsync(ct));

    [HttpPost("smoke-test")]
    public async Task<ActionResult<SmokeTestSuiteDto>> SmokeTest(CancellationToken ct)
    {
        var result = await _smokeService.RunAsync(ct);
        await _goLiveService.RunSmokeTestAsync(result, ct);
        return Ok(result);
    }

    [HttpPost("switch-dns")]
    public async Task<ActionResult> SwitchDns(CancellationToken ct)
    {
        var ok = await _goLiveService.SwitchDnsAsync(ct);
        return ok ? Ok(new { switched = true, phase = "LIVE" }) : BadRequest();
    }

    [HttpPost("announce")]
    public async Task<ActionResult> Announce(CancellationToken ct)
    {
        var text = await _goLiveService.SendAnnouncementAsync(ct);
        return Ok(new { announcement = text });
    }

    [HttpGet("report")]
    public async Task<ActionResult<GoLiveReportDto>> Report(CancellationToken ct)
        => Ok(await _goLiveService.GenerateReportAsync(ct));

    [HttpGet("health")]
    public async Task<ActionResult<HealthCheckDto>> Health(CancellationToken ct)
        => Ok(await _goLiveService.HealthCheckAsync(ct));
}

[ApiController]
[Route("api/monitoring")]
[Produces("application/json")]
public class MonitoringController : ControllerBase
{
    private readonly IMonitoringService _monitoringService;
    public MonitoringController(IMonitoringService monitoringService) => _monitoringService = monitoringService;

    [HttpGet("metrics")]
    public async Task<ActionResult<Dictionary<string, object>>> GetMetrics(CancellationToken ct)
        => Ok(await _monitoringService.GetMetricsAsync(ct));

    [HttpGet("metrics/recent")]
    public ActionResult<List<MonitoringMetricDto>> GetRecent([FromQuery] int count = 100)
        => Ok(_monitoringService.GetRecentMetrics(count));

    [HttpPost("tickets")]
    public ActionResult<Guid> CreateTicket([FromBody] SupportTicketDto ticket)
    {
        var id = _monitoringService.CreateTicket(ticket.Title, ticket.Description, ticket.Severity,
            ticket.UserEmail, ticket.Endpoint, ticket.ErrorTrace);
        return Ok(new { ticketId = id });
    }

    [HttpGet("tickets")]
    public ActionResult<List<SupportTicketDto>> GetTickets([FromQuery] string? status = null, [FromQuery] string? severity = null)
        => Ok(_monitoringService.GetTickets(status, severity));

    [HttpPost("tickets/{id:guid}/resolve")]
    public ActionResult ResolveTicket(Guid id, [FromBody] ResolveTicketDto body)
    {
        var ok = _monitoringService.ResolveTicket(id, body.Notes);
        return ok ? NoContent() : NotFound();
    }
}

public class ResolveTicketDto
{
    public string Notes { get; set; } = string.Empty;
}
