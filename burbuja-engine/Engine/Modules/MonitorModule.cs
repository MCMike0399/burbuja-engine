using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Diagnostics;
using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Engine.Modules;

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
[DiscoverableModule(
    Name = "Monitor Module",
    Version = "1.0.0",
    Priority = 50,
    Tags = new[] { "monitoring", "infrastructure", "observability", "diagnostics" },
    Enabled = true
)]
public class MonitorModule : BaseEngineModule, IHostedService
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
    
    private DateTime _moduleStartTime;
    private long _totalMetricsCollected;
    private long _totalHealthChecks;
    private long _totalErrors;
    
    public override string ModuleName => "Monitor Module";
    public override string Version => "1.0.0";

    /// <summary>
    /// Configure priority for the monitor module.
    /// Monitor is infrastructure support that should start after core services.
    /// </summary>
    protected override ModulePriority ConfigurePriority()
    {
        return ModulePriority.Create(PriorityLevel.Infrastructure)
            .WithSubPriority(50) // Lower priority within infrastructure (starts after DB, etc.)
            .CanParallelInitialize(true) // Can initialize in parallel with other non-critical services
            .WithContextAdjustment("Development", -10) // Higher priority in development for debugging
            .WithContextAdjustment("Testing", -15) // Even higher priority in testing
            .WithContextAdjustment("Production", 10) // Slightly lower priority in production
            .WithTags("monitoring", "infrastructure", "observability", "diagnostics")
            .WithMetadata("description", "Real-time system monitoring and metrics collection")
            .WithMetadata("category", "Observability")
            .Build();
    }

    /// <summary>
    /// Initialize the monitor module.
    /// </summary>
    protected override Task OnInitializeAsync(CancellationToken cancellationToken)
    {
        LogInfo("Initializing monitor module...");
        
        try
        {
            // Initialize metrics storage
            InitializeMetrics();
            
            // Register monitor services
            RegisterMonitoringServices();
            
            LogInfo("Monitor module initialized successfully");
            LogInfo($"Metrics collection interval: {_metricsInterval}");
            LogInfo($"Health check interval: {_healthCheckInterval}");
            LogInfo($"Maximum events stored: {_maxEvents}");
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogError($"Failed to initialize monitor module: {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>
    /// Start the monitor module and begin metrics collection.
    /// </summary>
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        LogInfo("Starting monitor module...");
        
        try
        {
            _moduleStartTime = DateTime.UtcNow;
            
            // Start metrics collection timer
            _metricsCollectionTimer = new Timer(
                CollectMetricsCallback,
                null,
                TimeSpan.Zero, // Start immediately
                _metricsInterval);
            
            // Start health check timer
            _healthCheckTimer = new Timer(
                PerformHealthChecksCallback,
                null,
                TimeSpan.FromSeconds(10), // Start after 10 seconds
                _healthCheckInterval);
            
            // Collect initial metrics
            await CollectInitialMetrics(cancellationToken);
            
            LogInfo("Monitor module started successfully");
            LogMonitorEvent("MonitorStarted", "Monitor module has started and is collecting metrics");
        }
        catch (Exception ex)
        {
            LogError($"Failed to start monitor module: {ex.Message}", ex);
            Interlocked.Increment(ref _totalErrors);
            throw;
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
            // Stop timers
            _metricsCollectionTimer?.Dispose();
            _healthCheckTimer?.Dispose();
            
            // Collect final metrics
            await CollectFinalMetrics(cancellationToken);
            
            LogInfo("Monitor module stopped successfully");
            LogMonitorEvent("MonitorStopped", "Monitor module has stopped");
        }
        catch (Exception ex)
        {
            LogWarning($"Error stopping monitor module: {ex.Message}");
            Interlocked.Increment(ref _totalErrors);
        }
    }

    /// <summary>
    /// Shutdown the monitor module.
    /// </summary>
    protected override async Task OnShutdownAsync(CancellationToken cancellationToken)
    {
        LogInfo("Shutting down monitor module...");
        
        try
        {
            // Dispose timers if not already done
            _metricsCollectionTimer?.Dispose();
            _healthCheckTimer?.Dispose();
            
            // Save final state if needed
            await SaveMonitoringState(cancellationToken);
            
            LogInfo("Monitor module shutdown complete");
        }
        catch (Exception ex)
        {
            LogWarning($"Error during monitor module shutdown: {ex.Message}");
        }
    }

    /// <summary>
    /// Get health information for the monitor module.
    /// </summary>
    protected override Task<ModuleHealth> OnGetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var health = ModuleHealth.Healthy(ModuleId, ModuleName, "Monitor is collecting metrics normally");
            
            // Add monitor-specific health details
            health.Details["metrics_count"] = _metrics.Count;
            health.Details["events_count"] = _recentEvents.Count;
            health.Details["total_metrics_collected"] = _totalMetricsCollected;
            health.Details["total_health_checks"] = _totalHealthChecks;
            health.Details["total_errors"] = _totalErrors;
            health.Details["uptime"] = DateTime.UtcNow - _moduleStartTime;
            health.Details["last_metrics_collection"] = _lastUpdated.GetValueOrDefault("LastMetricsCollection", DateTime.MinValue);
            health.Details["last_health_check"] = _lastUpdated.GetValueOrDefault("LastHealthCheck", DateTime.MinValue);
            
            // Check if metrics collection is working
            var lastMetricsTime = _lastUpdated.GetValueOrDefault("LastMetricsCollection", DateTime.MinValue);
            if (DateTime.UtcNow - lastMetricsTime > _metricsInterval.Add(TimeSpan.FromSeconds(30)))
            {
                health = ModuleHealth.Warning(ModuleId, ModuleName, "Metrics collection appears to be delayed");
            }
            
            return Task.FromResult(health);
        }
        catch (Exception ex)
        {
            LogError($"Monitor health check failed: {ex.Message}", ex);
            return Task.FromResult(ModuleHealth.Unhealthy(ModuleId, ModuleName, $"Health check exception: {ex.Message}"));
        }
    }

    /// <summary>
    /// Populate monitor-specific diagnostics.
    /// </summary>
    protected override Task OnPopulateDiagnosticsAsync(ModuleDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        try
        {
            // Performance metrics
            diagnostics.Performance["metrics_count"] = _metrics.Count;
            diagnostics.Performance["events_count"] = _recentEvents.Count;
            diagnostics.Performance["total_metrics_collected"] = _totalMetricsCollected;
            diagnostics.Performance["total_health_checks"] = _totalHealthChecks;
            diagnostics.Performance["total_errors"] = _totalErrors;
            diagnostics.Performance["uptime_seconds"] = (DateTime.UtcNow - _moduleStartTime).TotalSeconds;
            
            // Configuration
            diagnostics.Configuration["metrics_interval_seconds"] = _metricsInterval.TotalSeconds;
            diagnostics.Configuration["health_check_interval_seconds"] = _healthCheckInterval.TotalSeconds;
            diagnostics.Configuration["max_events"] = _maxEvents;
            
            // Current metrics snapshot
            diagnostics.Metadata["current_metrics"] = new Dictionary<string, object>(_metrics);
            diagnostics.Metadata["last_updated_times"] = new Dictionary<string, DateTime>(_lastUpdated);
            
            // Recent events (last 10)
            lock (_eventsLock)
            {
                diagnostics.Metadata["recent_events"] = _recentEvents.TakeLast(10).ToList();
            }
            
            diagnostics.Metadata["monitoring_capabilities"] = new[]
            {
                "Real-time metrics collection",
                "Health status monitoring",
                "Performance tracking",
                "Event logging",
                "Resource usage monitoring"
            };
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to populate monitor diagnostics: {ex.Message}");
            diagnostics.Metadata["diagnostics_error"] = ex.Message;
        }
        
        return Task.CompletedTask;
    }

    #region IHostedService Implementation

    public new Task StartAsync(CancellationToken cancellationToken)
    {
        // This is called by the hosting system
        // Our module lifecycle is managed by the engine, so we don't need to do anything here
        LogInfo("Monitor module registered with hosting system");
        return Task.CompletedTask;
    }

    public new Task StopAsync(CancellationToken cancellationToken)
    {
        // This is called by the hosting system
        LogInfo("Monitor module unregistered from hosting system");
        return Task.CompletedTask;
    }

    #endregion

    #region Public API Methods

    /// <summary>
    /// Get all current metrics.
    /// </summary>
    public Dictionary<string, object> GetMetrics()
    {
        return new Dictionary<string, object>(_metrics);
    }

    /// <summary>
    /// Get a specific metric value.
    /// </summary>
    public T? GetMetric<T>(string key)
    {
        if (_metrics.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// Get recent monitor events.
    /// </summary>
    public List<MonitorEvent> GetRecentEvents(int count = 50)
    {
        lock (_eventsLock)
        {
            return _recentEvents.TakeLast(count).ToList();
        }
    }

    /// <summary>
    /// Get engine-wide health summary.
    /// </summary>
    public async Task<EngineHealthSummary> GetEngineHealthSummary(CancellationToken cancellationToken = default)
    {
        try
        {
            if (Context?.Engine == null)
            {
                return new EngineHealthSummary { Status = "Unknown", Message = "Engine not available" };
            }

            var engineHealth = await Context.Engine.GetHealthAsync(cancellationToken);
            var engineDiagnostics = await Context.Engine.GetDiagnosticsAsync(cancellationToken);
            
            var summary = new EngineHealthSummary
            {
                EngineId = Context.Engine.EngineId,
                Status = engineHealth.Status.ToString(),
                Message = engineHealth.Message ?? "Unknown",
                LastChecked = DateTime.UtcNow,
                ModuleCount = engineHealth.ModuleHealth.Count,
                HealthyModules = engineHealth.ModuleHealth.Count(m => m.Value.Status == HealthStatus.Healthy),
                WarningModules = engineHealth.ModuleHealth.Count(m => m.Value.Status == HealthStatus.Warning),
                UnhealthyModules = engineHealth.ModuleHealth.Count(m => m.Value.Status == HealthStatus.Unhealthy),
                Uptime = engineDiagnostics.Uptime,
                Version = Context.Engine.Version
            };

            return summary;
        }
        catch (Exception ex)
        {
            LogError($"Failed to get engine health summary: {ex.Message}", ex);
            return new EngineHealthSummary 
            { 
                Status = "Error", 
                Message = $"Health check failed: {ex.Message}",
                LastChecked = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Get detailed module information.
    /// </summary>
    public async Task<List<ModuleInfo>> GetModuleInformation(CancellationToken cancellationToken = default)
    {
        try
        {
            if (Context?.Engine == null)
            {
                return new List<ModuleInfo>();
            }

            var moduleInfos = new List<ModuleInfo>();

            foreach (var module in Context.Engine.Modules)
            {
                try
                {
                    var health = await module.GetHealthAsync(cancellationToken);
                    var diagnostics = await module.GetDiagnosticsAsync(cancellationToken);

                    moduleInfos.Add(new ModuleInfo
                    {
                        ModuleId = module.ModuleId,
                        ModuleName = module.ModuleName,
                        Version = module.Version,
                        State = module.State.ToString(),
                        HealthStatus = health.Status.ToString(),
                        HealthMessage = health.Message ?? "No health message",
                        Priority = module.Priority,
                        Uptime = diagnostics.Uptime,
                        Dependencies = module.Dependencies.ToList(),
                        LastHealthCheck = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    moduleInfos.Add(new ModuleInfo
                    {
                        ModuleId = module.ModuleId,
                        ModuleName = module.ModuleName,
                        Version = module.Version,
                        State = module.State.ToString(),
                        HealthStatus = "Error",
                        HealthMessage = $"Failed to get health: {ex.Message}",
                        Priority = module.Priority,
                        LastHealthCheck = DateTime.UtcNow
                    });
                }
            }

            return moduleInfos.OrderBy(m => m.Priority).ToList();
        }
        catch (Exception ex)
        {
            LogError($"Failed to get module information: {ex.Message}", ex);
            return new List<ModuleInfo>();
        }
    }

    /// <summary>
    /// Get system resource usage.
    /// </summary>
    public SystemResourceUsage GetSystemResourceUsage()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            
            return new SystemResourceUsage
            {
                CpuUsagePercent = GetCpuUsage(),
                MemoryUsageMB = process.WorkingSet64 / (1024 * 1024),
                PrivateMemoryMB = process.PrivateMemorySize64 / (1024 * 1024),
                VirtualMemoryMB = process.VirtualMemorySize64 / (1024 * 1024),
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount,
                TotalProcessorTime = process.TotalProcessorTime,
                StartTime = process.StartTime,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogError($"Failed to get system resource usage: {ex.Message}", ex);
            return new SystemResourceUsage
            {
                LastUpdated = DateTime.UtcNow,
                CpuUsagePercent = -1 // Indicates error
            };
        }
    }

    #endregion

    #region Private Methods

    private void InitializeMetrics()
    {
        // Initialize baseline metrics
        _metrics["ModuleStartTime"] = DateTime.UtcNow;
        _metrics["TotalMetricsCollected"] = 0L;
        _metrics["TotalHealthChecks"] = 0L;
        _metrics["TotalErrors"] = 0L;
        
        _lastUpdated["Initialization"] = DateTime.UtcNow;
    }

    private void RegisterMonitoringServices()
    {
        // Services are registered through ConfigureServices method
        LogDebug("Monitor services configuration completed");
    }

    private async void CollectMetricsCallback(object? state)
    {
        try
        {
            await CollectMetrics();
            Interlocked.Increment(ref _totalMetricsCollected);
            _lastUpdated["LastMetricsCollection"] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            LogError($"Error during metrics collection: {ex.Message}", ex);
            Interlocked.Increment(ref _totalErrors);
        }
    }

    private async void PerformHealthChecksCallback(object? state)
    {
        try
        {
            await PerformHealthChecks();
            Interlocked.Increment(ref _totalHealthChecks);
            _lastUpdated["LastHealthCheck"] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            LogError($"Error during health checks: {ex.Message}", ex);
            Interlocked.Increment(ref _totalErrors);
        }
    }

    private async Task CollectMetrics()
    {
        try
        {
            // Collect system metrics
            var resourceUsage = GetSystemResourceUsage();
            _metrics["SystemResourceUsage"] = resourceUsage;
            
            // Collect engine metrics if available
            if (Context?.Engine != null)
            {
                var engineDiagnostics = await Context.Engine.GetDiagnosticsAsync();
                _metrics["EngineUptime"] = engineDiagnostics.Uptime ?? TimeSpan.Zero;
                _metrics["ModuleCount"] = engineDiagnostics.ModuleCount;
                _metrics["EngineState"] = Context.Engine.State.ToString();
                
                // Collect module metrics
                var moduleMetrics = new Dictionary<string, object>();
                foreach (var module in Context.Engine.Modules)
                {
                    try
                    {
                        var moduleDiagnostics = await module.GetDiagnosticsAsync();
                        moduleMetrics[module.ModuleName] = new
                        {
                            State = module.State.ToString(),
                            Uptime = moduleDiagnostics.Uptime,
                            Version = module.Version
                        };
                    }
                    catch (Exception ex)
                    {
                        moduleMetrics[module.ModuleName] = new { Error = ex.Message };
                    }
                }
                _metrics["ModuleMetrics"] = moduleMetrics;
            }
            
            // Update totals
            _metrics["TotalMetricsCollected"] = _totalMetricsCollected;
            _metrics["TotalHealthChecks"] = _totalHealthChecks;
            _metrics["TotalErrors"] = _totalErrors;
            
            LogDebug($"Collected metrics: {_metrics.Count} total metrics");
        }
        catch (Exception ex)
        {
            LogError($"Failed to collect metrics: {ex.Message}", ex);
            Interlocked.Increment(ref _totalErrors);
        }
    }

    private async Task PerformHealthChecks()
    {
        try
        {
            if (Context?.Engine == null) return;

            var engineHealth = await Context.Engine.GetHealthAsync();
            _metrics["EngineHealth"] = engineHealth;
            
            LogDebug($"Performed health check: Engine status = {engineHealth.Status}");
            
            // Log health issues
            if (engineHealth.Status != HealthStatus.Healthy)
            {
                LogMonitorEvent("HealthIssue", $"Engine health status: {engineHealth.Status} - {engineHealth.Message}");
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to perform health checks: {ex.Message}", ex);
            Interlocked.Increment(ref _totalErrors);
        }
    }

    private async Task CollectInitialMetrics(CancellationToken cancellationToken)
    {
        await CollectMetrics();
        LogMonitorEvent("InitialMetrics", "Initial metrics collection completed");
    }

    private async Task CollectFinalMetrics(CancellationToken cancellationToken)
    {
        await CollectMetrics();
        LogMonitorEvent("FinalMetrics", "Final metrics collection completed");
    }

    private Task SaveMonitoringState(CancellationToken cancellationToken)
    {
        // In a real implementation, you might want to persist monitoring state
        LogDebug("Monitoring state saved");
        return Task.CompletedTask;
    }

    private double GetCpuUsage()
    {
        try
        {
            // This is a simplified CPU usage calculation
            // In production, you'd want a more sophisticated approach
            var process = Process.GetCurrentProcess();
            return process.TotalProcessorTime.TotalMilliseconds / Environment.ProcessorCount / DateTime.UtcNow.Subtract(process.StartTime).TotalMilliseconds * 100;
        }
        catch
        {
            return -1; // Indicates error
        }
    }

    private void LogMonitorEvent(string eventType, string message)
    {
        var monitorEvent = new MonitorEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Message = message,
            Source = ModuleName
        };

        lock (_eventsLock)
        {
            _recentEvents.Add(monitorEvent);
            
            // Keep only recent events
            while (_recentEvents.Count > _maxEvents)
            {
                _recentEvents.RemoveAt(0);
            }
        }

        LogInfo($"Monitor Event [{eventType}]: {message}");
    }

    #endregion

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register the monitor module as a hosted service
        services.AddSingleton<MonitorModule>(this);
        services.AddHostedService<MonitorModule>(provider => this);
        
        LogDebug("Monitor module services registered");
    }
}

#region Data Models

/// <summary>
/// Represents a monitor event.
/// </summary>
public class MonitorEvent
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// Represents engine health summary.
/// </summary>
public class EngineHealthSummary
{
    public Guid EngineId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime LastChecked { get; set; }
    public int ModuleCount { get; set; }
    public int HealthyModules { get; set; }
    public int WarningModules { get; set; }
    public int UnhealthyModules { get; set; }
    public TimeSpan? Uptime { get; set; }
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// Represents module information.
/// </summary>
public class ModuleInfo
{
    public Guid ModuleId { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = string.Empty;
    public string HealthMessage { get; set; } = string.Empty;
    public int Priority { get; set; }
    public TimeSpan? Uptime { get; set; }
    public List<Guid> Dependencies { get; set; } = new();
    public DateTime LastHealthCheck { get; set; }
}

/// <summary>
/// Represents system resource usage.
/// </summary>
public class SystemResourceUsage
{
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageMB { get; set; }
    public long PrivateMemoryMB { get; set; }
    public long VirtualMemoryMB { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public TimeSpan TotalProcessorTime { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime LastUpdated { get; set; }
}

#endregion
