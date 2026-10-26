using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Application.Services;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ERP.FMC.Tests;

public class AuditServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly AuditService _service;
    private readonly List<AuditLog> _logs = new();

    public AuditServiceTests()
    {
        _uow.Setup(u => u.AuditLogs.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(l => _logs.Add(l))
            .ReturnsAsync((AuditLog l) => l);

        _uow.Setup(u => u.AuditLogs.GetAllAsync())
            .ReturnsAsync(_logs);

        _uow.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _service = new AuditService(_uow.Object, NullLogger<AuditService>.Instance);
    }

    [Fact]
    public async Task LogAsync_AddsAuditLog()
    {
        await _service.LogAsync(
            tableName: "partners",
            recordId: Guid.NewGuid(),
            action: "INSERT",
            newValues: new { PartnerCode = "CT001", PartnerName = "Test" });

        Assert.Single(_logs);
        Assert.Equal("partners", _logs[0].TableName);
        Assert.Equal("INSERT", _logs[0].Action);
        Assert.NotNull(_logs[0].NewValues);
        Assert.Contains("CT001", _logs[0].NewValues);
    }

    [Fact]
    public async Task GetLogsAsync_FiltersCorrectly()
    {
        // Add multiple logs
        var partnerId = Guid.NewGuid();
        await _service.LogAsync("partners", partnerId, "INSERT", newValues: new { x = 1 });
        await _service.LogAsync("products", Guid.NewGuid(), "INSERT", newValues: new { y = 2 });

        var filter = new AuditLogFilterDto
        {
            TableName = "partners",
            Page = 1,
            PageSize = 10
        };

        var result = await _service.GetLogsAsync(filter);

        Assert.Single(result.Items);
        Assert.Equal("partners", result.Items[0].TableName);
    }

    [Fact]
    public async Task GetRecordHistory_ReturnsLogsForRecord()
    {
        var recordId = Guid.NewGuid();
        await _service.LogAsync("partners", recordId, "INSERT", newValues: new { x = 1 });
        await _service.LogAsync("partners", recordId, "UPDATE",
            oldValues: new { x = 1 },
            newValues: new { x = 2 });
        await _service.LogAsync("partners", Guid.NewGuid(), "INSERT"); // different record

        var history = await _service.GetRecordHistoryAsync("partners", recordId);

        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.Equal(recordId, h.RecordId));
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsCounts()
    {
        await _service.LogAsync("partners", Guid.NewGuid(), "INSERT");
        await _service.LogAsync("partners", Guid.NewGuid(), "UPDATE");
        await _service.LogAsync("products", Guid.NewGuid(), "INSERT");

        var stats = await _service.GetStatsAsync();

        Assert.Equal(3, stats["TotalLogs"]);
        Assert.NotNull(stats["LogsByAction"]);
        Assert.NotNull(stats["LogsByTable"]);
    }
}

public class ImportServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly ImportService _service;

    public ImportServiceTests()
    {
        _service = new ImportService(_uow.Object, _auditService.Object, NullLogger<ImportService>.Instance);
    }

    [Fact]
    public async Task GetImportHistoryAsync_ReturnsRecentJobs()
    {
        var jobs = new List<ImportJob>
        {
            new() { Id = Guid.NewGuid(), JobCode = "JOB1", ImportType = "PARTNERS", Status = "COMPLETED", UploadedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), JobCode = "JOB2", ImportType = "PRODUCTS", Status = "VALIDATED", UploadedAt = DateTime.UtcNow.AddHours(-1) }
        };

        _uow.Setup(u => u.ImportJobs.GetAllAsync()).ReturnsAsync(jobs);

        var history = await _service.GetImportHistoryAsync(limit: 10);

        Assert.Equal(2, history.Count);
        Assert.Equal("JOB1", history[0].JobCode); // most recent first
    }

    [Fact]
    public async Task ReImportAsync_CreatesNewJobFromOriginal()
    {
        var originalId = Guid.NewGuid();
        var originalJob = new ImportJob
        {
            Id = originalId,
            JobCode = "ORIG-001",
            ImportType = "PARTNERS",
            Status = "COMPLETED",
            FileName = "test.csv",
            UploadedBy = Guid.NewGuid()
        };

        var stagingRows = new List<ImportStaging>
        {
            new()
            {
                Id = Guid.NewGuid(),
                JobId = originalId,
                RowNumber = 1,
                RawData = "{\"partner_code\":\"CT001\"}",
                ValidationStatus = "VALID"
            }
        };

        _uow.Setup(u => u.ImportJobs.GetByIdAsync(originalId)).ReturnsAsync(originalJob);
        _uow.Setup(u => u.ImportStaging.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ImportStaging, bool>>>()))
            .ReturnsAsync(stagingRows);

        var newJobId = await _service.ReImportAsync(new ReImportRequestDto
        {
            OriginalJobId = originalId,
            KeepResolvedErrors = true
        });

        Assert.NotEqual(originalId, newJobId);
    }
}
