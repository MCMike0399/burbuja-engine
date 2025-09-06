using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Diagnostics;
using BurbujaEngine.Engine.Core;
using BurbujaEngine.Monitor.Core;

namespace BurbujaEngine.Monitor.Services;

/// <summary>
/// Monitor module for BurbujaEngine - Real-time System Monitoring Service.
/// 
/// MICROKERNEL PATTERN: Step 3 - Modularize Services (Monitoring Example)
/// 
/// This module demonstrates how system monitoring capabilities are implemented
/// as a user-space service in the microkernel architecture:
/// 
/// MONITORING SERVICE CHARACTERISTICS:
/// - Real-time metrics collection from all engine components
/// - Performance monitoring for modules, drivers, and microkernel
/// - Health status aggregation and alerting
/// - Resource usage tracking (CPU, memory, I/O)
/// - Event logging and audit trail
/// - Configuration monitoring and changes tracking
/// 
/// USER-SPACE SERVICE BENEFITS:
/// - Non-intrusive: Monitoring doesn't impact microkernel performance
/// - Fault isolation: Monitor failures don't affect core engine operations
/// - Extensible: Can add new metrics without touching microkernel
/// - Hot-swappable: Can update monitoring logic without engine restart
/// 
/// INFRASTRUCTURE MODULE PRIORITY:
/// - Medium Priority: Important for operations but not critical for startup
/// - Context-Aware: Higher priority in development/testing environments
/// - Dependency-Safe: Designed to work even if some dependencies fail
/// 
/// This exemplifies how the microkernel architecture enables building
/// comprehensive monitoring solutions as independent, user-space services.
/// </summary>
public class MonitorModule : BaseEngineModule, IHostedService, IMonitorService, IMetricsProvider, IMonitorEventLogger
{
    private readonly ConcurrentDictionary<string, object> _metrics = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastUpdated = new();
    private readonly List<MonitorEvent> _recentEvents = new();
    private readonly object _eventsLock = new();
    private readonly int _maxEvents = 1000;
    
    private Timer? _metricsCollectionTimer;
    private Timer? _healthCheckTimer;
    private readonly TimeSpan _metricsInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _healthCheckInterval = TimeSpan.FromSeconds(30);
    
    public override string ModuleName => "Monitor Module";
    public override string Version => "2.0.0";
    
    /// <summary>
    /// Configure priority for the monitor module.
    /// Monitor is important for operations but not critical for startup.
    /// </summary>
    protected override ModulePriority ConfigurePriority()
    {
        return ModulePriority.Create(PriorityLevel.Monitoring)
            .WithSubPriority(8) // High priority within application level
            .CanParallelInitialize(true) // Can initialize in parallel with other modules
            .WithContextAdjustment("Development", -5) // Higher priority in development
            .WithContextAdjustment("Testing", -5) // Higher priority in testing
            .WithTags("monitoring", "metrics", "health", "diagnostics")
            .Build();
    }

    /// <summary>
    /// Initialize the monitor module.
    /// MICROKERNEL PRINCIPLE: Graceful Initialization - Module should initialize even if dependencies are unavailable
    /// </summary>
    protected override Task OnInitializeAsync(CancellationToken cancellationToken)
    {
        LogInfo("Initializing monitor module...");
        
        try
        {
            // Initialize metrics collection
            InitializeMetrics();
            
            LogInfo("Monitor module initialized successfully");
            LogInfo($"Metrics collection interval: {_metricsInterval}");
            LogInfo($"Health check interval: {_healthCheckInterval}");
            LogInfo($"Maximum events stored: {_maxEvents}");
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogError($"Failed to initialize monitor module: {ex.Message}");
            // Don't throw - allow module to continue in degraded state
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Start the monitor module.
    /// MICROKERNEL PRINCIPLE: Independent Operation - Module should start and provide value independently
    /// </summary>
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        LogInfo("Starting monitor module...");
        
        try
        {
            // Start background services
            await StartBackgroundServicesAsync();
            
            // Log initial metrics
            await LogEventAsync(new MonitorEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                EventType = MonitorEventType.MetricsCollection,
                Category = MonitorEventCategory.System,
                Title = "Initial Metrics",
                Message = "Initial metrics collection completed",
                Severity = MonitorEventSeverity.Info
            });
            
            LogInfo("Monitor module started successfully");
            
            // Log monitor started event
            await LogEventAsync(new MonitorEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                EventType = MonitorEventType.EngineStateChanged,
                Category = MonitorEventCategory.Module,
                Title = "Monitor Started",
                Message = "Monitor module has started and is collecting metrics",
                Severity = MonitorEventSeverity.Info,
                RelatedModuleId = ModuleId,
                RelatedModuleName = ModuleName
            });
            
        }
        catch (Exception ex)
        {
            LogError($"Failed to start monitor module: {ex.Message}");
            // Don't throw - let module continue in degraded state
        }
    }

    /// <summary>
    /// Stop the monitor module.
    /// </summary>
    protected override async Task OnStopAsync(CancellationToken cancellationToken)
    {
        LogInfo("Stopping monitor module...");
        
        try
        {
            // Stop background services
            await StopBackgroundServicesAsync();
            
            LogInfo("Monitor module stopped successfully");
        }
        catch (Exception ex)
        {
            LogWarning($"Error stopping monitor module: {ex.Message}");
        }
    }

    /// <summary>
    /// Shutdown the monitor module.
    /// </summary>
    protected override Task OnShutdownAsync(CancellationToken cancellationToken)
    {
        LogInfo("Shutting down monitor module...");
        
        try
        {
            // Dispose timers
            _metricsCollectionTimer?.Dispose();
            _healthCheckTimer?.Dispose();
            
            LogInfo("Monitor module shutdown complete");
        }
        catch (Exception ex)
        {
            LogWarning($"Error during monitor module shutdown: {ex.Message}");
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get health information for the monitor module.
    /// </summary>
    protected override Task<ModuleHealth> OnGetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var health = ModuleHealth.Healthy(ModuleId, ModuleName, "Monitor module is operating normally");
            
            // Add monitoring-specific health details
            health.Details["metrics_collected"] = _metrics.Count;
            health.Details["events_logged"] = _recentEvents.Count;
            health.Details["last_metrics_collection"] = _lastUpdated.GetValueOrDefault("metrics", DateTime.MinValue);
            health.Details["last_health_check"] = _lastUpdated.GetValueOrDefault("health", DateTime.MinValue);
            health.Details["metrics_interval_seconds"] = _metricsInterval.TotalSeconds;
            health.Details["health_check_interval_seconds"] = _healthCheckInterval.TotalSeconds;
            
            return Task.FromResult(health);
        }
        catch (Exception ex)
        {
            LogError($"Monitor health check failed: {ex.Message}");
            return Task.FromResult(ModuleHealth.Warning(ModuleId, ModuleName, $"Monitor health check exception: {ex.Message}"));
        }
    }

    /// <summary>
    /// Populate monitor-specific diagnostics.
    /// </summary>
    protected override Task OnPopulateDiagnosticsAsync(ModuleDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        try
        {
            // Add monitoring performance metrics
            diagnostics.Performance["total_metrics"] = _metrics.Count;
            diagnostics.Performance["total_events"] = _recentEvents.Count;
            diagnostics.Performance["metrics_collection_enabled"] = _metricsCollectionTimer != null;
            diagnostics.Performance["health_check_enabled"] = _healthCheckTimer != null;
            
            // Add configuration
            diagnostics.Configuration["metrics_interval"] = _metricsInterval;
            diagnostics.Configuration["health_check_interval"] = _healthCheckInterval;
            diagnostics.Configuration["max_events"] = _maxEvents;
            
            // Add metadata
            diagnostics.Metadata["monitor_version"] = Version;
            diagnostics.Metadata["supports_real_time"] = true;
            diagnostics.Metadata["supports_history"] = false; // Not implemented yet
            diagnostics.Metadata["supports_alerting"] = false; // Not implemented yet
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to populate monitor diagnostics: {ex.Message}");
            diagnostics.Metadata["diagnostics_error"] = ex.Message;
        }
        
        return Task.CompletedTask;
    }

    #region IHostedService Implementation

    public new async Task StartAsync(CancellationToken cancellationToken)
    {
        // IHostedService implementation - delegates to base class StartAsync
        await base.StartAsync(cancellationToken);
    }

    public new async Task StopAsync(CancellationToken cancellationToken)
    {
        // IHostedService implementation - delegates to base class StopAsync
        await base.StopAsync(cancellationToken);
    }

    #endregion

    #region IMonitorService Implementation

    public async Task<EngineHealthStatus> GetEngineHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var engine = Context.ServiceProvider.GetService<IBurbujaEngine>();
            if (engine == null)
            {
                return new EngineHealthStatus
                {
                    EngineId = "unknown",
                    State = "Unknown",
                    Timestamp = DateTime.UtcNow,
                    Uptime = TimeSpan.Zero,
                    IsHealthy = false,
                    TotalModules = 0,
                    HealthyModules = 0,
                    WarningModules = 0,
                    UnhealthyModules = 1
                };
            }

            var moduleHealthTasks = engine.Modules.Select(async m =>
            {
                try
                {
                    return await m.GetHealthAsync(cancellationToken);
                }
                catch
                {
                    return ModuleHealth.Unhealthy(m.ModuleId, m.ModuleName, "Health check failed");
                }
            });

            var moduleHealthResults = await Task.WhenAll(moduleHealthTasks);
            
            var healthyCount = moduleHealthResults.Count(h => h.Status == HealthStatus.Healthy);
            var warningCount = moduleHealthResults.Count(h => h.Status == HealthStatus.Warning);
            var unhealthyCount = moduleHealthResults.Count(h => h.Status == HealthStatus.Unhealthy || h.Status == HealthStatus.Critical);

            return new EngineHealthStatus
            {
                EngineId = engine.EngineId.ToString(),
                State = engine.State.ToString(),
                Timestamp = DateTime.UtcNow,
                Uptime = engine.State == EngineState.Running ? TimeSpan.FromSeconds(10) : TimeSpan.Zero, // Placeholder - calculate from process start time
                IsHealthy = engine.State == EngineState.Running && healthyCount > 0,
                TotalModules = engine.Modules.Count(),
                HealthyModules = healthyCount,
                WarningModules = warningCount,
                UnhealthyModules = unhealthyCount
            };
        }
        catch (Exception ex)
        {
            LogError($"Failed to get engine health: {ex.Message}");
            throw;
        }
    }

    public async Task<SystemMetrics> GetSystemMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await CollectCurrentMetrics();
            
            var process = Process.GetCurrentProcess();
            var memoryUsed = process.WorkingSet64;
            var totalMemory = GC.GetTotalMemory(false);
            
            return new SystemMetrics
            {
                Timestamp = DateTime.UtcNow,
                CpuUsagePercent = GetCpuUsage(),
                MemoryUsedBytes = memoryUsed,
                MemoryTotalBytes = totalMemory,
                MemoryUsagePercent = totalMemory > 0 ? (double)memoryUsed / totalMemory * 100 : 0,
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount,
                ProcessUptime = DateTime.UtcNow - process.StartTime
            };
        }
        catch (Exception ex)
        {
            LogError($"Failed to get system metrics: {ex.Message}");
            throw;
        }
    }

    public async Task<IEnumerable<ModuleHealthInfo>> GetModuleHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var engine = Context.ServiceProvider.GetService<IBurbujaEngine>();
            if (engine == null) return Array.Empty<ModuleHealthInfo>();

            var moduleHealthTasks = engine.Modules.Select(async module =>
            {
                try
                {
                    var health = await module.GetHealthAsync(cancellationToken);
                    return new ModuleHealthInfo
                    {
                        ModuleId = module.ModuleId,
                        ModuleName = module.ModuleName,
                        FriendlyId = module.FriendlyId,
                        State = module.State.ToString(),
                        HealthStatus = health.Status.ToString(),
                        HealthMessage = health.Message ?? "No message",
                        LastUpdated = health.CheckedAt,
                        Details = health.Details
                    };
                }
                catch (Exception ex)
                {
                    return new ModuleHealthInfo
                    {
                        ModuleId = module.ModuleId,
                        ModuleName = module.ModuleName,
                        FriendlyId = module.FriendlyId,
                        State = module.State.ToString(),
                        HealthStatus = "Error",
                        HealthMessage = $"Health check failed: {ex.Message}",
                        LastUpdated = DateTime.UtcNow
                    };
                }
            });

            return await Task.WhenAll(moduleHealthTasks);
        }
        catch (Exception ex)
        {
            LogError($"Failed to get module health: {ex.Message}");
            throw;
        }
    }

    public Task<IEnumerable<MonitorEvent>> GetRecentEventsAsync(int maxEvents = 100, CancellationToken cancellationToken = default)
    {
        lock (_eventsLock)
        {
            var events = _recentEvents
                .OrderByDescending(e => e.Timestamp)
                .Take(maxEvents)
                .ToList();
            
            return Task.FromResult<IEnumerable<MonitorEvent>>(events);
        }
    }

    public async Task<MonitorDashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var engineHealth = await GetEngineHealthAsync(cancellationToken);
            var systemMetrics = await GetSystemMetricsAsync(cancellationToken);
            var moduleHealth = await GetModuleHealthAsync(cancellationToken);
            var recentEvents = await GetRecentEventsAsync(50, cancellationToken);

            return new MonitorDashboardData
            {
                EngineHealth = engineHealth,
                CurrentMetrics = systemMetrics,
                ModuleHealth = moduleHealth,
                RecentEvents = recentEvents,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogError($"Failed to get dashboard data: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region IMetricsProvider Implementation

    public async Task<SystemMetrics> CollectMetricsAsync(CancellationToken cancellationToken = default)
    {
        return await GetSystemMetricsAsync(cancellationToken);
    }

    public Task<IEnumerable<SystemMetrics>> GetMetricsHistoryAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        // TODO: Implement metrics history storage
        return Task.FromResult<IEnumerable<SystemMetrics>>(Array.Empty<SystemMetrics>());
    }

    #endregion

    #region IMonitorEventLogger Implementation

    public Task LogEventAsync(MonitorEvent monitorEvent, CancellationToken cancellationToken = default)
    {
        lock (_eventsLock)
        {
            _recentEvents.Add(monitorEvent);
            
            // Keep only the most recent events
            while (_recentEvents.Count > _maxEvents)
            {
                _recentEvents.RemoveAt(0);
            }
        }
        
        // Log the event to the logger as well
        var logLevel = monitorEvent.Severity switch
        {
            MonitorEventSeverity.Critical => LogLevel.Critical,
            MonitorEventSeverity.Error => LogLevel.Error,
            MonitorEventSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };
        
        LogWithLevel(logLevel, $"Monitor Event [{monitorEvent.Title}]: {monitorEvent.Message}");
        
        return Task.CompletedTask;
    }

    public Task ClearOldEventsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTime.UtcNow - maxAge;
        
        lock (_eventsLock)
        {
            _recentEvents.RemoveAll(e => e.Timestamp < cutoffTime);
        }
        
        return Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    private void InitializeMetrics()
    {
        // Initialize basic metrics
        _metrics["monitor_started"] = DateTime.UtcNow;
        _lastUpdated["initialization"] = DateTime.UtcNow;
    }

    private Task StartBackgroundServicesAsync()
    {
        // Start metrics collection timer
        _metricsCollectionTimer = new Timer(
            callback: async _ => await CollectCurrentMetrics(),
            state: null,
            dueTime: _metricsInterval,
            period: _metricsInterval);

        // Start health check timer
        _healthCheckTimer = new Timer(
            callback: async _ => await PerformHealthCheck(),
            state: null,
            dueTime: _healthCheckInterval,
            period: _healthCheckInterval);
            
        return Task.CompletedTask;
    }

    private Task StopBackgroundServicesAsync()
    {
        if (_metricsCollectionTimer != null)
        {
            _metricsCollectionTimer.Dispose();
            _metricsCollectionTimer = null;
        }

        if (_healthCheckTimer != null)
        {
            _healthCheckTimer.Dispose();
            _healthCheckTimer = null;
        }
        
        return Task.CompletedTask;
    }

    private Task CollectCurrentMetrics()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            
            // Update metrics
            _metrics["cpu_usage"] = GetCpuUsage();
            _metrics["memory_usage"] = process.WorkingSet64;
            _metrics["thread_count"] = process.Threads.Count;
            _metrics["handle_count"] = process.HandleCount;
            _metrics["gc_memory"] = GC.GetTotalMemory(false);
            _lastUpdated["metrics"] = DateTime.UtcNow;
            
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to collect metrics: {ex.Message}");
        }
        
        return Task.CompletedTask;
    }

    private async Task PerformHealthCheck()
    {
        try
        {
            var engineHealth = await GetEngineHealthAsync();
            
            // Log health check event
            await LogEventAsync(new MonitorEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                EventType = MonitorEventType.HealthCheck,
                Category = MonitorEventCategory.Health,
                Title = "Periodic Health Check",
                Message = $"Engine health: {engineHealth.State}, Modules: {engineHealth.HealthyModules}/{engineHealth.TotalModules} healthy",
                Severity = engineHealth.IsHealthy ? MonitorEventSeverity.Info : MonitorEventSeverity.Warning
            });
            
            _lastUpdated["health"] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to perform health check: {ex.Message}");
        }
    }

    private double GetCpuUsage()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            return process.TotalProcessorTime.TotalMilliseconds / Environment.ProcessorCount / Environment.TickCount * 100;
        }
        catch
        {
            return 0.0;
        }
    }

    private void LogWithLevel(LogLevel level, string message)
    {
        switch (level)
        {
            case LogLevel.Critical:
            case LogLevel.Error:
                LogError(message);
                break;
            case LogLevel.Warning:
                LogWarning(message);
                break;
            default:
                LogInfo(message);
                break;
        }
    }

    #endregion
}
