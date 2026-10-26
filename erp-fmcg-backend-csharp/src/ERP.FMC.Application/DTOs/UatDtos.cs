namespace ERP.FMC.Application.DTOs;

// ============ UAT Test Case DTOs ============
public class TestCaseDto
{
    public string Id { get; set; } = string.Empty;  // TC01, TC02...
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = "NORMAL"; // CRITICAL, HIGH, NORMAL, LOW
    public string ExpectedResult { get; set; } = string.Empty;
    public TestCaseStatus Status { get; set; } = TestCaseStatus.PENDING;
    public string? ActualResult { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public DateTime? ExecutedAt { get; set; }
}

public enum TestCaseStatus
{
    PENDING,
    RUNNING,
    PASSED,
    FAILED,
    SKIPPED,
    ERROR
}

public class TestSuiteRunDto
{
    public string SuiteName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt - StartedAt : null;
    public int TotalCases { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int Errored { get; set; }
    public double PassRate => TotalCases > 0 ? (double)Passed / TotalCases * 100 : 0;
    public List<TestCaseDto> Cases { get; set; } = new();
}

public class TestSuiteSummaryDto
{
    public string SuiteName { get; set; } = string.Empty;
    public DateTime LastRunAt { get; set; }
    public int TotalRuns { get; set; }
    public double LastPassRate { get; set; }
    public bool IsReadyForGoLive { get; set; }
}

// ============ Smoke Test DTOs ============
public class SmokeTestDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public bool Pass { get; set; }
    public int StatusCode { get; set; }
    public long ResponseTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SmokeTestSuiteDto
{
    public DateTime ExecutedAt { get; set; }
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public bool AllPassed => Failed == 0;
    public TimeSpan Duration { get; set; }
    public List<SmokeTestDto> Tests { get; set; } = new();
}

// ============ Performance Benchmark DTOs ============
public class BenchmarkDto
{
    public string Operation { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public long TargetMs { get; set; }
    public long ActualMs { get; set; }
    public int RowCount { get; set; }
    public bool Pass => ActualMs <= TargetMs;
    public double Throughput { get; set; }  // rows/second
    public DateTime ExecutedAt { get; set; }
}

public class BenchmarkSuiteDto
{
    public DateTime ExecutedAt { get; set; }
    public int TotalBenchmarks { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public bool AllPassed => Failed == 0;
    public List<BenchmarkDto> Benchmarks { get; set; } = new();
}

// ============ Go-Live DTOs ============
public class GoLiveStatusDto
{
    public string Phase { get; set; } = string.Empty; // PREP, READY, LIVE, ROLLBACK
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsLockedNestJS { get; set; }
    public bool IsMigrated { get; set; }
    public bool IsTestedPassed { get; set; }
    public bool IsDnsSwitched { get; set; }
    public bool IsSmokeTestPassed { get; set; }
    public string? AnnouncementSentAt { get; set; }
    public List<string> Blockers { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class HealthCheckDto
{
    public DateTime CheckedAt { get; set; }
    public string Status { get; set; } = string.Empty; // HEALTHY, DEGRADED, DOWN
    public bool DatabaseConnected { get; set; }
    public long DatabaseResponseMs { get; set; }
    public long ApiResponseMs { get; set; }
    public int ActiveUsers { get; set; }
    public int RequestsLast5Min { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryUsageMb { get; set; }
    public Dictionary<string, object> Details { get; set; } = new();
}

public class MonitoringMetricDto
{
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Status { get; set; } = "OK"; // OK, WARNING, CRITICAL
}

public class SupportTicketDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // CRITICAL, HIGH, MEDIUM, LOW
    public string Status { get; set; } = string.Empty;   // OPEN, IN_PROGRESS, RESOLVED, CLOSED
    public string? UserEmail { get; set; }
    public string? Endpoint { get; set; }
    public string? ErrorTrace { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
}

public class HotfixDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AffectedEndpoint { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // IDENTIFIED, IN_PROGRESS, DEPLOYED, VERIFIED
    public string? FixCommit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeployedAt { get; set; }
}
