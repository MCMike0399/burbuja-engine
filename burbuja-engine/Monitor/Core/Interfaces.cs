using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Monitor.Core;

/// <summary>
/// Core monitoring service interface.
/// </summary>
public interface IMonitorService
{
    /// <summary>
    /// Get current engine health status.
    /// </summary>
    Task<EngineHealthStatus> GetEngineHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get real-time metrics for the engine.
    /// </summary>
    Task<SystemMetrics> GetSystemMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get health status for all modules.
    /// </summary>
    Task<IEnumerable<ModuleHealthInfo>> GetModuleHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent monitoring events.
    /// </summary>
    Task<IEnumerable<MonitorEvent>> GetRecentEventsAsync(int maxEvents = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get aggregated dashboard data.
    /// </summary>
    Task<MonitorDashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Real-time metrics provider interface.
/// </summary>
public interface IMetricsProvider
{
    /// <summary>
    /// Collect current system metrics.
    /// </summary>
    Task<SystemMetrics> CollectMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get metrics history.
    /// </summary>
    Task<IEnumerable<SystemMetrics>> GetMetricsHistoryAsync(TimeSpan duration, CancellationToken cancellationToken = default);
}

/// <summary>
/// Event logger for monitoring events.
/// </summary>
public interface IMonitorEventLogger
{
    /// <summary>
    /// Log a monitoring event.
    /// </summary>
    Task LogEventAsync(MonitorEvent monitorEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent events.
    /// </summary>
    Task<IEnumerable<MonitorEvent>> GetRecentEventsAsync(int maxEvents = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear old events.
    /// </summary>
    Task ClearOldEventsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}
