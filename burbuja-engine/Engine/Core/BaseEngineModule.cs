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
    /// Module ID with automatic generation based on module name if not overridden.
    /// </summary>
    public virtual Guid ModuleId => GenerateModuleId(ModuleName);
    
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
    /// Generate a consistent Module ID based on the module name.
    /// This ensures modules have stable IDs without hardcoding GUIDs.
    /// For multiple instances, includes a unique suffix.
    /// </summary>
    protected virtual Guid GenerateModuleId(string moduleName)
    {
        // Create a unique suffix based on creation time and random data for multiple instances
        var uniqueSuffix = $"{DateTime.UtcNow.Ticks}.{Environment.CurrentManagedThreadId}.{Random.Shared.Next()}";
        var uniqueModuleName = $"BurbujaEngine.Module.{moduleName}.{uniqueSuffix}";
        
        // Use a deterministic approach to generate GUID from module name
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(uniqueModuleName));
        
        // Take first 16 bytes and create a GUID
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        
        // Set version (4) and variant bits for a valid GUID
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40); // Version 4
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // Variant bits
        
        return new Guid(guidBytes);
    }
    
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
    
    /// <summary>
    /// Helper method to create priority configuration with sub-priority.
    /// </summary>
    [Obsolete("Use ModulePriority.Create() builder pattern instead.")]
    protected static LegacyModulePriority CreatePriorityConfig(PriorityLevel priority, int subPriority = 0)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return (LegacyModulePriority)(int)priority;
#pragma warning restore CS0618 // Type or member is obsolete
    }
    
    /// <summary>
    /// Helper method to create priority configuration with context adjustments.
    /// </summary>
    [Obsolete("Use ModulePriority.Create() builder pattern instead.")]
    protected static ModulePriorityConfig CreateContextualPriorityConfig(
        PriorityLevel priority, 
        Dictionary<string, int> contextAdjustments)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return ModulePriorityConfig.WithContext((LegacyModulePriority)(int)priority, contextAdjustments);
#pragma warning restore CS0618 // Type or member is obsolete
    }
    
    /// <summary>
    /// Helper method to create a comprehensive priority configuration.
    /// </summary>
    [Obsolete("Use ModulePriority.Create() builder pattern instead.")]
    protected static ModulePriorityConfig CreateAdvancedPriorityConfig(
        PriorityLevel priority,
        int subPriority = 0,
        bool canParallelInitialize = true,
        Dictionary<string, int>? contextAdjustments = null,
        HashSet<string>? tags = null)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return new ModulePriorityConfig
        {
            BasePriority = (LegacyModulePriority)(int)priority,
            SubPriority = subPriority,
            CanParallelInitialize = canParallelInitialize,
            ContextAdjustments = contextAdjustments ?? new(),
            Tags = tags ?? new()
        };
#pragma warning restore CS0618 // Type or member is obsolete
    }
    
    /// <summary>
    /// Create a priority configuration using the builder pattern.
    /// </summary>
    protected static ModulePriority CreateModulePriority(PriorityLevel priority)
    {
        return ModulePriority.Create(priority).Build();
    }
    
    /// <summary>
    /// Create a priority with sub-priority.
    /// </summary>
    protected static ModulePriority CreateModulePriorityWithSub(PriorityLevel priority, int subPriority)
    {
        return ModulePriority.Create(priority).WithSubPriority(subPriority).Build();
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
            
            LogInfo($"Initializing module {ModuleName} (ID: {ModuleId})");
            
            // Call template method for specific initialization
            await OnInitializeAsync(cancellationToken);
            
            InitializedAt = DateTime.UtcNow;
            State = ModuleState.Initialized;
            
            LogInfo($"Module {ModuleName} initialized successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return ModuleResult.Successful($"Module {ModuleName} initialized", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = ModuleState.Error;
            LogError($"Failed to initialize module {ModuleName}: {ex.Message}", ex);
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
            LogInfo($"Starting module {ModuleName}");
            
            // Call template method for specific startup logic
            await OnStartAsync(cancellationToken);
            
            StartedAt = DateTime.UtcNow;
            State = ModuleState.Running;
            
            LogInfo($"Module {ModuleName} started successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return ModuleResult.Successful($"Module {ModuleName} started", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = ModuleState.Error;
            LogError($"Failed to start module {ModuleName}: {ex.Message}", ex);
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
            LogInfo($"Stopping module {ModuleName}");
            
            // Cancel module operations
            ModuleCancellationTokenSource?.Cancel();
            
            // Call template method for specific stop logic
            await OnStopAsync(cancellationToken);
            
            State = ModuleState.Stopped;
            
            LogInfo($"Module {ModuleName} stopped successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return ModuleResult.Successful($"Module {ModuleName} stopped", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = ModuleState.Error;
            LogError($"Failed to stop module {ModuleName}: {ex.Message}", ex);
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
            LogInfo($"Shutting down module {ModuleName}");
            
            // Stop first if running
            if (State == ModuleState.Running)
            {
                await StopAsync(cancellationToken);
            }
            
            // Call template method for specific shutdown logic
            await OnShutdownAsync(cancellationToken);
            
            State = ModuleState.Shutdown;
            
            LogInfo($"Module {ModuleName} shut down successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return ModuleResult.Successful($"Module {ModuleName} shut down", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = ModuleState.Error;
            LogError($"Failed to shut down module {ModuleName}: {ex.Message}", ex);
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
            LogError($"Health check failed for module {ModuleName}: {ex.Message}", ex);
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
            LogError($"Failed to get diagnostics for module {ModuleName}: {ex.Message}", ex);
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
        Logger?.LogInformation("[{ModuleName}] {Message}", ModuleName, message);
        AddLogEntry("Information", message);
    }
    
    /// <summary>
    /// Log a warning message.
    /// </summary>
    protected void LogWarning(string message)
    {
        Logger?.LogWarning("[{ModuleName}] {Message}", ModuleName, message);
        AddLogEntry("Warning", message);
    }
    
    /// <summary>
    /// Log an error message.
    /// </summary>
    protected void LogError(string message, Exception? exception = null)
    {
        Logger?.LogError(exception, "[{ModuleName}] {Message}", ModuleName, message);
        AddLogEntry("Error", message, exception);
    }
    
    /// <summary>
    /// Log a debug message.
    /// </summary>
    protected void LogDebug(string message)
    {
        Logger?.LogDebug("[{ModuleName}] {Message}", ModuleName, message);
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
                LogError($"Error during disposal of module {ModuleName}: {ex.Message}", ex);
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
