using System.Text.Json.Serialization;

namespace BurbujaEngine.Monitor.Core;

/// <summary>
/// Engine health status information.
/// </summary>
public class EngineHealthStatus
{
    public required string EngineId { get; init; }
    public required string State { get; init; }
    public required DateTime Timestamp { get; init; }
    public required TimeSpan Uptime { get; init; }
    public required bool IsHealthy { get; init; }
    public required int TotalModules { get; init; }
    public required int HealthyModules { get; init; }
    public required int WarningModules { get; init; }
    public required int UnhealthyModules { get; init; }
    public Dictionary<string, object> Details { get; init; } = new();
}

/// <summary>
/// System metrics information.
/// </summary>
public class SystemMetrics
{
    public required DateTime Timestamp { get; init; }
    public required double CpuUsagePercent { get; init; }
    public required long MemoryUsedBytes { get; init; }
    public required long MemoryTotalBytes { get; init; }
    public required double MemoryUsagePercent { get; init; }
    public required long ThreadCount { get; init; }
    public required long HandleCount { get; init; }
    public required TimeSpan ProcessUptime { get; init; }
    public Dictionary<string, object> AdditionalMetrics { get; init; } = new();
}

/// <summary>
/// Module health information.
/// </summary>
public class ModuleHealthInfo
{
    public required Guid ModuleId { get; init; }
    public required string ModuleName { get; init; }
    public required string FriendlyId { get; init; }
    public required string State { get; init; }
    public required string HealthStatus { get; init; }
    public required string HealthMessage { get; init; }
    public required DateTime LastUpdated { get; init; }
    public Dictionary<string, object> Details { get; init; } = new();
}

/// <summary>
/// Monitor event information.
/// </summary>
public class MonitorEvent
{
    public required Guid EventId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string EventType { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string Severity { get; init; }
    public Guid? RelatedModuleId { get; init; }
    public string? RelatedModuleName { get; init; }
    public Dictionary<string, object> Data { get; init; } = new();
}

/// <summary>
/// Aggregated dashboard data.
/// </summary>
public class MonitorDashboardData
{
    public required EngineHealthStatus EngineHealth { get; init; }
    public required SystemMetrics CurrentMetrics { get; init; }
    public required IEnumerable<ModuleHealthInfo> ModuleHealth { get; init; }
    public required IEnumerable<MonitorEvent> RecentEvents { get; init; }
    public required DateTime LastUpdated { get; init; }
}

/// <summary>
/// Monitor event severity levels.
/// </summary>
public static class MonitorEventSeverity
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Critical = "Critical";
}

/// <summary>
/// Monitor event categories.
/// </summary>
public static class MonitorEventCategory
{
    public const string Engine = "Engine";
    public const string Module = "Module";
    public const string System = "System";
    public const string Performance = "Performance";
    public const string Health = "Health";
    public const string Security = "Security";
}

/// <summary>
/// Monitor event types.
/// </summary>
public static class MonitorEventType
{
    public const string EngineStateChanged = "EngineStateChanged";
    public const string ModuleStateChanged = "ModuleStateChanged";
    public const string HealthCheck = "HealthCheck";
    public const string MetricsCollection = "MetricsCollection";
    public const string PerformanceAlert = "PerformanceAlert";
    public const string SystemAlert = "SystemAlert";
    public const string ModuleAlert = "ModuleAlert";
}
