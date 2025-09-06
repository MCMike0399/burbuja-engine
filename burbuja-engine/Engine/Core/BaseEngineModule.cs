using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BurbujaEngine.Engine.Core;

/// <summary>
/// Base implementation for engine modules with integrated unified priority support.
/// Provides common functionality for all engine modules including semantic priority management.
/// </summary>
public abstract class BaseEngineModule : IEngineModule, IModulePriorityModule, IDisposable
{
    private readonly object _stateLock = new();
    private ModuleState _state = ModuleState.Created;
    private readonly List<LogEntry> _recentLogs = new();
    private readonly int _maxLogEntries = 100;
    private ModulePriority? _modulePriority;
    
    protected ILogger Logger { get; private set; } = default!;
    protected IModuleContext Context { get; private set; } = default!;
    protected DateTime CreatedAt { get; } = DateTime.UtcNow;
    protected DateTime? InitializedAt { get; private set; }
    protected DateTime? StartedAt { get; private set; }
    protected CancellationTokenSource? ModuleCancellationTokenSource { get; private set; }
    
    public abstract string ModuleName { get; }
    public virtual string Version => "1.0.0";
    public virtual IReadOnlyList<Guid> Dependencies => Array.Empty<Guid>();
    
    /// <summary>
    /// Module ID with automatic generation. Each module instance gets a unique identifier.
    /// </summary>
    public virtual Guid ModuleId { get; } = Guid.NewGuid();
    
    /// <summary>
    /// Friendly identifier for debugging purposes. Combines module name with short ID.
    /// </summary>
    public string FriendlyId => $"{ModuleName}_{ModuleId.ToString("N")[..8]}";
    
    /// <summary>
    /// Priority configuration for this module.
    /// </summary>
    public virtual ModulePriority ModulePriority
    {
        get
        {
            if (_modulePriority == null)
            {
                _modulePriority = ConfigurePriority();
            }
            return _modulePriority;
        }
    }
    
    /// <summary>
    /// Legacy priority property for backward compatibility.
    /// Uses the module priority system to calculate effective priority.
    /// </summary>
    public virtual int Priority => ModulePriority.GetEffectivePriority(GetExecutionContext());
    
    /// <summary>
    /// Semantic priority level for this module.
    /// Override GetDefaultPriority() to customize this for derived classes.
    /// </summary>
    public virtual PriorityLevel ModulePriorityLevel => ModulePriority.Level;
    
    /// <summary>
    /// Configure the priority for this module.
    /// Override this method to provide custom priority configuration.
    /// </summary>
    protected virtual ModulePriority ConfigurePriority()
    {
        return ModulePriority.Simple(GetDefaultPriority());
    }
    
    /// <summary>
    /// Get the default priority for this module.
    /// Override this to set the base priority level.
    /// </summary>
    protected virtual PriorityLevel GetDefaultPriority() => PriorityLevel.Core;
    
    /// <summary>
    /// Get the current execution context for priority calculations.
    /// This can be environment, configuration, or runtime context.
    /// </summary>
    protected virtual string? GetExecutionContext()
    {
        // Try to get context from various sources
        try
        {
            if (Context?.Configuration != null)
            {
                // Check for explicit context configuration
                if (Context.Configuration.TryGetValue("ExecutionContext", out var contextObj))
                {
                    return contextObj?.ToString();
                }
                
                // Check for environment
                if (Context.Configuration.TryGetValue("Environment", out var envObj))
                {
                    return envObj?.ToString();
                }
            }
            
            // Fall back to environment variable
            return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        }
        catch
        {
            return "Production"; // Safe fallback
        }
    }
    
    public ModuleState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
        private set
        {
            ModuleState oldState;
            lock (_stateLock)
            {
                oldState = _state;
                _state = value;
            }
            
            if (oldState != value)
            {
                OnStateChanged(oldState, value);
                StateChanged?.Invoke(this, new ModuleStateChangedEventArgs(ModuleId, ModuleName, oldState, value));
            }
        }
    }
    
    public event EventHandler<ModuleStateChangedEventArgs>? StateChanged;
    
    /// <summary>
    /// Initialize the module with the given context.
    /// </summary>
    public async Task<ModuleResult> InitializeAsync(IModuleContext context, CancellationToken cancellationToken = default)
    {
        if (State != ModuleState.Created)
        {
            return ModuleResult.Failed($"Module {ModuleId} is in state {State}, expected Created");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = ModuleState.Initializing;
        
        try
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Logger = context.LoggerFactory.CreateLogger(GetType());
            ModuleCancellationTokenSource = new CancellationTokenSource();
            
            LogInfo($"Initializing module {FriendlyId}");
            
            // Call template method for specific initialization
            await OnInitializeAsync(cancellationToken);
            
            InitializedAt = DateTime.UtcNow;
            State = ModuleState.Initialized;
            
            LogInfo($"Module {FriendlyId} initialized successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return ModuleResult.Successful($"Module {ModuleName} initialized", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = ModuleState.Error;
            LogError($"Failed to initialize module {FriendlyId}: {ex.Message}", ex);
            return ModuleResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Start the module and begin its operations.
    /// </summary>
    public async Task<ModuleResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (State != ModuleState.Initialized)
        {
            return ModuleResult.Failed($"Module {ModuleId} is in state {State}, expected Initialized");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = ModuleState.Starting;
        
        try
        {
            LogInfo($"Starting module {FriendlyId}");
            
            // Call template method for specific startup logic
            await OnStartAsync(cancellationToken);
            
            StartedAt = DateTime.UtcNow;
            State = ModuleState.Running;
            
            LogInfo($"Module {FriendlyId} started successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return ModuleResult.Successful($"Module {ModuleName} started", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = ModuleState.Error;
            LogError($"Failed to start module {FriendlyId}: {ex.Message}", ex);
            return ModuleResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Stop the module and cleanup resources.
    /// </summary>
    public async Task<ModuleResult> StopAsync(CancellationToken cancellationToken = default)
    {
        if (State != ModuleState.Running)
        {
            return ModuleResult.Successful($"Module {ModuleName} is not running (state: {State})");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = ModuleState.Stopping;
        
        try
        {
            LogInfo($"Stopping module {FriendlyId}");
            
            // Cancel module operations
            ModuleCancellationTokenSource?.Cancel();
            
            // Call template method for specific stop logic
            await OnStopAsync(cancellationToken);
            
            State = ModuleState.Stopped;
            
            LogInfo($"Module {FriendlyId} stopped successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return ModuleResult.Successful($"Module {ModuleName} stopped", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = ModuleState.Error;
            LogError($"Failed to stop module {FriendlyId}: {ex.Message}", ex);
            return ModuleResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Shutdown the module gracefully.
    /// </summary>
    public async Task<ModuleResult> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (State == ModuleState.Shutdown || State == ModuleState.Disposed)
        {
            return ModuleResult.Successful($"Module {ModuleName} already shut down");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = ModuleState.ShuttingDown;
        
        try
        {
            LogInfo($"Shutting down module {FriendlyId}");
            
            // Stop first if running
            if (State == ModuleState.Running)
            {
                await StopAsync(cancellationToken);
            }
            
            // Call template method for specific shutdown logic
            await OnShutdownAsync(cancellationToken);
            
            State = ModuleState.Shutdown;
            
            LogInfo($"Module {FriendlyId} shut down successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return ModuleResult.Successful($"Module {ModuleName} shut down", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = ModuleState.Error;
            LogError($"Failed to shut down module {FriendlyId}: {ex.Message}", ex);
            return ModuleResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Get health information about the module.
    /// </summary>
    public virtual async Task<ModuleHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var health = await OnGetHealthAsync(cancellationToken);
            stopwatch.Stop();
            
            return health with { ResponseTime = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            LogError($"Health check failed for module {FriendlyId}: {ex.Message}", ex);
            return ModuleHealth.Unhealthy(ModuleId, ModuleName, $"Health check failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get diagnostic information about the module.
    /// </summary>
    public virtual async Task<ModuleDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var uptime = StartedAt.HasValue ? (TimeSpan?)(DateTime.UtcNow - StartedAt.Value) : null;
            
            var diagnostics = new ModuleDiagnostics
            {
                ModuleId = ModuleId,
                ModuleName = ModuleName,
                Version = Version,
                State = State,
                CreatedAt = CreatedAt,
                InitializedAt = InitializedAt,
                StartedAt = StartedAt,
                Uptime = uptime,
                Dependencies = Dependencies.ToList(),
                RecentLogs = GetRecentLogs()
            };
            
            // Allow derived classes to add specific diagnostics
            await OnPopulateDiagnosticsAsync(diagnostics, cancellationToken);
            
            return diagnostics;
        }
        catch (Exception ex)
        {
            LogError($"Failed to get diagnostics for module {FriendlyId}: {ex.Message}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// Configure services that this module provides.
    /// </summary>
    public virtual void ConfigureServices(IServiceCollection services)
    {
        // Default implementation does nothing
        // Derived classes can override to register services
    }
    
    /// <summary>
    /// Template method for module-specific initialization.
    /// </summary>
    protected virtual Task OnInitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    /// Template method for module-specific startup.
    /// </summary>
    protected virtual Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    /// Template method for module-specific stop logic.
    /// </summary>
    protected virtual Task OnStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    /// Template method for module-specific shutdown logic.
    /// </summary>
    protected virtual Task OnShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    /// Template method for module-specific health checks.
    /// </summary>
    protected virtual Task<ModuleHealth> OnGetHealthAsync(CancellationToken cancellationToken)
    {
        var status = State switch
        {
            ModuleState.Running => HealthStatus.Healthy,
            ModuleState.Initialized or ModuleState.Stopped => HealthStatus.Warning,
            ModuleState.Error => HealthStatus.Unhealthy,
            _ => HealthStatus.Unknown
        };
        
        var message = State switch
        {
            ModuleState.Running => "Module is running normally",
            ModuleState.Initialized => "Module is initialized but not started",
            ModuleState.Stopped => "Module is stopped",
            ModuleState.Error => "Module is in error state",
            _ => $"Module is in {State} state"
        };
        
        var health = new ModuleHealth
        {
            ModuleId = ModuleId,
            ModuleName = ModuleName,
            Status = status,
            Message = message
        };
        
        return Task.FromResult(health);
    }
    
    /// <summary>
    /// Template method for populating module-specific diagnostics.
    /// </summary>
    protected virtual Task OnPopulateDiagnosticsAsync(ModuleDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        // Add priority information to diagnostics
        var context = GetExecutionContext();
        var analysis = ModulePriority.Analyze(context);
        
        diagnostics.Metadata["priority_config"] = new
        {
            base_priority = analysis.Level.ToString(),
            base_priority_value = analysis.Level.ToNumericValue(),
            sub_priority = analysis.SubPriority,
            effective_priority = analysis.EffectivePriority,
            execution_context = analysis.Context,
            context_adjustment = analysis.ContextAdjustment,
            weight = analysis.Weight,
            can_parallel_initialize = analysis.CanParallelInitialize,
            context_adjustments = ModulePriority.ContextAdjustments,
            tags = analysis.Tags,
            dependencies = analysis.Dependencies,
            priority_category = analysis.CategoryName,
            priority_description = analysis.Description,
            metadata = analysis.Metadata
        };
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Called when the module state changes.
    /// </summary>
    protected virtual void OnStateChanged(ModuleState previousState, ModuleState newState)
    {
        // Default implementation does nothing
        // Derived classes can override to react to state changes
    }
    
    /// <summary>
    /// Log an informational message.
    /// </summary>
    protected void LogInfo(string message)
    {
        Logger?.LogInformation("[{FriendlyId}] {Message}", FriendlyId, message);
        AddLogEntry("Information", message);
    }
    
    /// <summary>
    /// Log a warning message.
    /// </summary>
    protected void LogWarning(string message)
    {
        Logger?.LogWarning("[{FriendlyId}] {Message}", FriendlyId, message);
        AddLogEntry("Warning", message);
    }
    
    /// <summary>
    /// Log an error message.
    /// </summary>
    protected void LogError(string message, Exception? exception = null)
    {
        Logger?.LogError(exception, "[{FriendlyId}] {Message}", FriendlyId, message);
        AddLogEntry("Error", message, exception);
    }
    
    /// <summary>
    /// Log a debug message.
    /// </summary>
    protected void LogDebug(string message)
    {
        Logger?.LogDebug("[{FriendlyId}] {Message}", FriendlyId, message);
        AddLogEntry("Debug", message);
    }
    
    private void AddLogEntry(string level, string message, Exception? exception = null)
    {
        lock (_recentLogs)
        {
            _recentLogs.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = message,
                Category = ModuleName,
                Exception = exception
            });
            
            // Keep only recent logs
            while (_recentLogs.Count > _maxLogEntries)
            {
                _recentLogs.RemoveAt(0);
            }
        }
    }
    
    private List<LogEntry> GetRecentLogs()
    {
        lock (_recentLogs)
        {
            return new List<LogEntry>(_recentLogs);
        }
    }
    
    /// <summary>
    /// Dispose the module.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && State != ModuleState.Disposed)
        {
            try
            {
                // Try to shutdown gracefully
                if (State != ModuleState.Shutdown)
                {
                    ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                LogError($"Error during disposal of module {FriendlyId}: {ex.Message}", ex);
            }
            finally
            {
                ModuleCancellationTokenSource?.Dispose();
                State = ModuleState.Disposed;
            }
        }
    }
}

/// <summary>
/// Context implementation for modules.
/// </summary>
internal class ModuleContext : IModuleContext
{
    public IServiceProvider ServiceProvider { get; }
    public ILoggerFactory LoggerFactory { get; }
    public IReadOnlyDictionary<string, object> Configuration { get; }
    public CancellationToken CancellationToken { get; }
    public IBurbujaEngine Engine { get; }
    
    public ModuleContext(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IReadOnlyDictionary<string, object> configuration,
        CancellationToken cancellationToken,
        IBurbujaEngine engine)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        CancellationToken = cancellationToken;
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }
}
