using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using BurbujaEngine.Engine.Drivers;
using BurbujaEngine.Engine.Microkernel;
using BurbujaEngine.Logging;

namespace BurbujaEngine.Engine.Core;

/// <summary>
/// BurbujaEngine - A microkernel architecture implementation following enterprise design patterns.
/// 
/// MICROKERNEL ARCHITECTURE COMPONENTS (Based on System Design Best Practices):
/// 
/// Step 1 - Core Functionality: This class implements the minimal microkernel with essential services:
/// - Module lifecycle management (loading, initialization, startup, shutdown)
/// - Driver registry and communication bus for IPC
/// - Service coordination and dependency resolution
/// - State management and health monitoring
/// 
/// Step 2 - Well-Defined Interfaces: Communicates with user-space modules through:
/// - IEngineModule interface for module contracts
/// - IModuleContext for providing microkernel services to modules
/// - Event-driven communication for state changes
/// 
/// Step 4 - Inter-Process Communication (IPC): Implements robust IPC through:
/// - DriverCommunicationBus for message passing between drivers
/// - Event system for state change notifications
/// - Service provider pattern for dependency injection
/// 
/// Step 6 - Service Management: Provides comprehensive service lifecycle management:
/// - Dynamic module registration and unregistration
/// - Dependency-aware initialization ordering
/// - Graceful shutdown with proper cleanup
/// - Health monitoring and diagnostics
/// 
/// Step 7 - Performance Optimization: Includes performance optimizations:
/// - Parallel initialization support (configurable)
/// - Efficient dependency resolution
/// - Minimal context switching overhead
/// - Asynchronous operations throughout
/// 
/// Step 8 - Security & Isolation: Implements security measures:
/// - Module isolation through service boundaries
/// - Controlled access to microkernel services via context
/// - Error containment to prevent cascade failures
/// 
/// This microkernel serves as the foundation for a modular, extensible system where
/// user-space modules (business logic, data access, etc.) operate independently
/// while leveraging core microkernel services for communication and coordination.
/// </summary>
public class BurbujaEngine : IBurbujaEngine, IDisposable
{
    private readonly Lock _stateLock = new();
    private EngineState _state = EngineState.Created;
    private readonly List<IEngineModule> _modules = new();
    private readonly ConcurrentDictionary<Guid, IEngineModule> _moduleIndex = new();
    private readonly IEngineConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BurbujaEngine> _logger;
    private readonly DateTime _createdAt = DateTime.UtcNow;
    private DateTime? _initializedAt;
    private DateTime? _startedAt;
    private CancellationTokenSource? _engineCancellationTokenSource;
    
    // Microkernel components - Step 1: Core Functionality
    // These represent the minimal set of services that must remain in kernel space
    private readonly IDriverRegistry _driverRegistry;          // Driver management service
    private readonly IDriverCommunicationBus _communicationBus; // IPC mechanism (Step 4)
    private readonly IDriverFactory _driverFactory;            // Driver instantiation service
    
    public Guid EngineId { get; }
    public string Version { get; }
    public IReadOnlyList<IEngineModule> Modules => _modules.AsReadOnly();
    public IServiceProvider ServiceProvider => _serviceProvider;
    
    // Microkernel core services - integrated directly into the engine
    public IDriverRegistry DriverRegistry => _driverRegistry;
    public IDriverCommunicationBus CommunicationBus => _communicationBus;
    public IDriverFactory DriverFactory => _driverFactory;
    
    public EngineState State
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
            EngineState oldState;
            lock (_stateLock)
            {
                oldState = _state;
                _state = value;
            }
            
            if (oldState != value)
            {
                OnStateChanged(oldState, value);
                StateChanged?.Invoke(this, new EngineStateChangedEventArgs(EngineId, oldState, value));
            }
        }
    }
    
    public event EventHandler<EngineStateChangedEventArgs>? StateChanged;
    public event EventHandler<ModuleStateChangedEventArgs>? ModuleStateChanged;
    
    // Driver events - core microkernel functionality
    public event EventHandler<IEngineDriver>? DriverRegistered;
    public event EventHandler<Guid>? DriverUnregistered;
    public event EventHandler<DriverStateChangedEventArgs>? DriverStateChanged;
    
    public BurbujaEngine(
        Guid engineId,
        IEngineConfiguration configuration,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory)
    {
        EngineId = engineId;
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<BurbujaEngine>();
        Version = configuration.Version;
        
        // Initialize microkernel components
        _communicationBus = new DriverCommunicationBus(_loggerFactory.CreateLogger<DriverCommunicationBus>());
        _driverRegistry = new DriverRegistry(_loggerFactory.CreateLogger<DriverRegistry>());
        _driverFactory = new DriverFactory(_serviceProvider);
        
        // Wire up events
        _driverRegistry.DriverRegistered += (sender, driver) => DriverRegistered?.Invoke(this, driver);
        _driverRegistry.DriverUnregistered += (sender, driverId) => DriverUnregistered?.Invoke(this, driverId);
        
        _logger.LogInformation("[{EngineId}] BurbujaEngine microkernel created with {ModuleCount} modules", 
            EngineId, _modules.Count);
    }
    
    /// <summary>
    /// Register a module with the engine.
    /// 
    /// MICROKERNEL PATTERN: Step 3 - Modularize Services
    /// This method implements the core microkernel principle of dynamic service registration.
    /// Modules represent user-space services that operate independently but can leverage
    /// microkernel services through well-defined interfaces.
    /// </summary>
    public IBurbujaEngine RegisterModule(IEngineModule module)
    {
        if (module == null) throw new ArgumentNullException(nameof(module));
        
        if (State != EngineState.Created)
        {
            throw new InvalidOperationException($"Cannot register modules when engine is in state {State}");
        }
        
        if (_moduleIndex.ContainsKey(module.ModuleId))
        {
            throw new InvalidOperationException($"Module with ID '{module.ModuleId}' is already registered");
        }
        
        _modules.Add(module);
        _moduleIndex[module.ModuleId] = module;
        
        // Subscribe to module state changes
        module.StateChanged += OnModuleStateChanged;
        
        _logger.LogInformation("[{EngineId}] Registered module '{FriendlyId}' (ID: {ModuleId})", 
            EngineId, module.FriendlyId, module.ModuleId);
        
        return this;
    }
    
    /// <summary>
    /// Register a module using a factory function.
    /// </summary>
    public IBurbujaEngine RegisterModule<T>(Func<T> moduleFactory) where T : class, IEngineModule
    {
        if (moduleFactory == null) throw new ArgumentNullException(nameof(moduleFactory));
        
        var module = moduleFactory();
        return RegisterModule(module);
    }
    
    /// <summary>
    /// Register a module with dependency injection.
    /// </summary>
    public IBurbujaEngine RegisterModule<T>() where T : class, IEngineModule
    {
        var module = _serviceProvider.GetRequiredService<T>();
        return RegisterModule(module);
    }
    
    /// <summary>
    /// Get a module by type.
    /// </summary>
    public T? GetModule<T>() where T : class, IEngineModule
    {
        return _modules.OfType<T>().FirstOrDefault();
    }
    
    /// <summary>
    /// Get a module by its ID.
    /// </summary>
    public IEngineModule? GetModule(Guid moduleId)
    {
        _moduleIndex.TryGetValue(moduleId, out var module);
        return module;
    }
    
    /// <summary>
    /// Initialize all modules in dependency order.
    /// 
    /// MICROKERNEL PATTERN: Step 6 - Service Management
    /// Implements sophisticated service lifecycle management with:
    /// - Dependency resolution to ensure proper initialization order
    /// - Parallel initialization support for performance (configurable)
    /// - Error handling and recovery mechanisms
    /// - Comprehensive logging and monitoring
    /// 
    /// This demonstrates the microkernel's role in coordinating user-space services
    /// while maintaining minimal core functionality.
    /// </summary>
    public async Task<EngineResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (State != EngineState.Created)
        {
            return EngineResult.Failed($"Engine is in state {State}, expected Created");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = EngineState.Initializing;
        
        try
        {
            _engineCancellationTokenSource = new CancellationTokenSource();
            var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _engineCancellationTokenSource.Token).Token;
            
            _logger.LogInformation("[{EngineId}] Initializing engine with {ModuleCount} modules", 
                EngineId, _modules.Count);
            
            // Resolve dependency order
            var orderedModules = ResolveDependencyOrder();
            _logger.LogDebug("[{EngineId}] Module initialization order: {ModuleOrder}", 
                EngineId, string.Join(" -> ", orderedModules.Select(m => m.FriendlyId)));
            
            var results = new Dictionary<Guid, ModuleResult>();
            
            // Configure services first
            ConfigureModuleServices();
            
            // Initialize modules in order
            if (_configuration.EnableParallelInitialization)
            {
                await InitializeModulesInParallel(orderedModules, results, combinedToken);
            }
            else
            {
                await InitializeModulesSequentially(orderedModules, results, combinedToken);
            }
            
            // Check for failures
            var failedModules = results.Where(r => !r.Value.Success).ToList();
            if (failedModules.Any() && !_configuration.ContinueOnModuleFailure)
            {
                var failureMessage = $"Failed to initialize modules: {string.Join(", ", failedModules.Select(f => f.Key))}";
                State = EngineState.Error;
                var failureResult = EngineResult.Failed(failureMessage, duration: stopwatch.Elapsed);
                return failureResult.WithModuleResults(results);
            }
            
            _initializedAt = DateTime.UtcNow;
            State = EngineState.Initialized;
            
            _logger.LogInformation("[{EngineId}] Engine initialized successfully in {Duration:F2}ms with {SuccessCount}/{TotalCount} modules", 
                EngineId, stopwatch.Elapsed.TotalMilliseconds, results.Count(r => r.Value.Success), results.Count);
            
            return EngineResult.Successful($"Engine initialized with {results.Count} modules", stopwatch.Elapsed)
                .WithModuleResults(results);
        }
        catch (Exception ex)
        {
            State = EngineState.Error;
            _logger.LogError(ex, "[{EngineId}] Failed to initialize engine: {Message}", EngineId, ex.Message);
            return EngineResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Start the engine and all its modules.
    /// </summary>
    public async Task<EngineResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (State != EngineState.Initialized)
        {
            return EngineResult.Failed($"Engine is in state {State}, expected Initialized");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = EngineState.Starting;
        
        try
        {
            var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _engineCancellationTokenSource?.Token ?? CancellationToken.None).Token;
            
            _logger.LogInformation("[{EngineId}] Starting engine with {ModuleCount} modules", 
                EngineId, _modules.Count);
            
            var results = new Dictionary<Guid, ModuleResult>();
            var orderedModules = ResolveDependencyOrder();
            
            // Start modules in order
            foreach (var module in orderedModules)
            {
                if (module.State == ModuleState.Initialized)
                {
                    var moduleResult = await module.StartAsync(combinedToken);
                    results[module.ModuleId] = moduleResult;
                    
                    if (!moduleResult.Success && !_configuration.ContinueOnModuleFailure)
                    {
                        State = EngineState.Error;
                        return EngineResult.Failed($"Failed to start module {module.FriendlyId}: {moduleResult.Message}", 
                            duration: stopwatch.Elapsed).WithModuleResults(results);
                    }
                }
            }
            
            _startedAt = DateTime.UtcNow;
            State = EngineState.Running;
            
            _logger.LogInformation("[{EngineId}] Engine started successfully in {Duration:F2}ms with {SuccessCount}/{TotalCount} modules running", 
                EngineId, stopwatch.Elapsed.TotalMilliseconds, results.Count(r => r.Value.Success), results.Count);
            
            return EngineResult.Successful($"Engine started with {results.Count} modules", stopwatch.Elapsed)
                .WithModuleResults(results);
        }
        catch (Exception ex)
        {
            State = EngineState.Error;
            _logger.LogError(ex, "[{EngineId}] Failed to start engine: {Message}", EngineId, ex.Message);
            return EngineResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Stop the engine and all its modules.
    /// </summary>
    public async Task<EngineResult> StopAsync(CancellationToken cancellationToken = default)
    {
        if (State != EngineState.Running)
        {
            return EngineResult.Successful($"Engine is not running (state: {State})");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = EngineState.Stopping;
        
        try
        {
            _logger.LogInformation("[{EngineId}] Stopping engine with {ModuleCount} modules", 
                EngineId, _modules.Count);
            
            var results = new Dictionary<Guid, ModuleResult>();
            var orderedModules = ResolveDependencyOrder();
            orderedModules.Reverse(); // Stop in reverse order
            
            // Stop modules in reverse dependency order
            foreach (var module in orderedModules)
            {
                if (module.State == ModuleState.Running)
                {
                    var moduleResult = await module.StopAsync(cancellationToken);
                    results[module.ModuleId] = moduleResult;
                }
            }
            
            State = EngineState.Stopped;
            
            _logger.LogInformation("[{EngineId}] Engine stopped successfully in {Duration:F2}ms", 
                EngineId, stopwatch.Elapsed.TotalMilliseconds);
            
            return EngineResult.Successful($"Engine stopped with {results.Count} modules", stopwatch.Elapsed)
                .WithModuleResults(results);
        }
        catch (Exception ex)
        {
            State = EngineState.Error;
            _logger.LogError(ex, "[{EngineId}] Failed to stop engine: {Message}", EngineId, ex.Message);
            return EngineResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Shutdown the engine gracefully.
    /// </summary>
    public async Task<EngineResult> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (State == EngineState.Shutdown || State == EngineState.Disposed)
        {
            return EngineResult.Successful("Engine already shut down");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = EngineState.ShuttingDown;
        
        try
        {
            _logger.LogInformation("[{EngineId}] Shutting down engine", EngineId);
            
            // Cancel all engine operations
            _engineCancellationTokenSource?.Cancel();
            
            // Stop first if running
            if (State == EngineState.Running)
            {
                await StopAsync(cancellationToken);
            }
            
            var results = new Dictionary<Guid, ModuleResult>();
            var orderedModules = ResolveDependencyOrder();
            orderedModules.Reverse(); // Shutdown in reverse order
            
            // Shutdown modules in reverse dependency order
            foreach (var module in orderedModules)
            {
                var moduleResult = await module.ShutdownAsync(cancellationToken);
                results[module.ModuleId] = moduleResult;
            }
            
            State = EngineState.Shutdown;
            
            _logger.LogInformation("[{EngineId}] Engine shut down successfully in {Duration:F2}ms", 
                EngineId, stopwatch.Elapsed.TotalMilliseconds);
            
            return EngineResult.Successful($"Engine shut down with {results.Count} modules", stopwatch.Elapsed)
                .WithModuleResults(results);
        }
        catch (Exception ex)
        {
            State = EngineState.Error;
            _logger.LogError(ex, "[{EngineId}] Failed to shut down engine: {Message}", EngineId, ex.Message);
            return EngineResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Get health information about the engine and all modules.
    /// </summary>
    public async Task<EngineHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var moduleHealthTasks = _modules.Select(async m =>
            {
                try
                {
                    return await m.GetHealthAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    return ModuleHealth.Unhealthy(m.ModuleId, m.ModuleName, $"Health check failed: {ex.Message}");
                }
            });
            
            var moduleHealths = await Task.WhenAll(moduleHealthTasks);
            var engineHealth = EngineHealth.FromModules(EngineId, moduleHealths);
            
            return engineHealth with { ResponseTime = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to get engine health: {Message}", EngineId, ex.Message);
            throw;
        }
    }
    
    /// <summary>
    /// Get diagnostic information about the engine.
    /// </summary>
    public async Task<EngineDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var uptime = _startedAt.HasValue ? (TimeSpan?)(DateTime.UtcNow - _startedAt.Value) : null;
            var process = Process.GetCurrentProcess();
            
            var moduleDiagnosticsTasks = _modules.Select(async m =>
            {
                try
                {
                    return await m.GetDiagnosticsAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get diagnostics for module {ModuleName}: {Message}", 
                        m.ModuleName, ex.Message);
                    return new ModuleDiagnostics
                    {
                        ModuleId = m.ModuleId,
                        ModuleName = m.ModuleName,
                        State = m.State,
                        CreatedAt = DateTime.UtcNow // We don't have access to the real created time
                    };
                }
            });
            
            var moduleDiagnostics = await Task.WhenAll(moduleDiagnosticsTasks);
            
            return new EngineDiagnostics
            {
                EngineId = EngineId,
                Version = Version,
                State = State,
                CreatedAt = _createdAt,
                InitializedAt = _initializedAt,
                StartedAt = _startedAt,
                Uptime = uptime,
                ModuleCount = _modules.Count,
                ModuleDiagnostics = moduleDiagnostics.ToDictionary(d => d.ModuleId, d => d),
                Configuration = _configuration.Values.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Environment = GetEnvironmentInfo(),
                Process = process
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to get engine diagnostics: {Message}", EngineId, ex.Message);
            throw;
        }
    }
    
    private void ConfigureModuleServices()
    {
        _logger.LogDebug("[{EngineId}] Configuring module services", EngineId);
        
        foreach (var module in _modules)
        {
            try
            {
                // Note: This might not work in all scenarios since we already have a built service provider
                // In a real implementation, module service configuration should happen before building the provider
                if (_serviceProvider is IServiceCollection services)
                {
                    module.ConfigureServices(services);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{EngineId}] Failed to configure services for module {FriendlyId}: {Message}", 
                    EngineId, module.FriendlyId, ex.Message);
            }
        }
    }
    
    private async Task InitializeModulesSequentially(
        IEnumerable<IEngineModule> orderedModules,
        Dictionary<Guid, ModuleResult> results,
        CancellationToken cancellationToken)
    {
        foreach (var module in orderedModules)
        {
            var context = CreateModuleContext(cancellationToken);
            var moduleResult = await module.InitializeAsync(context, cancellationToken);
            results[module.ModuleId] = moduleResult;
            
            if (!moduleResult.Success && !_configuration.ContinueOnModuleFailure)
            {
                break;
            }
        }
    }
    
    private async Task InitializeModulesInParallel(
        IEnumerable<IEngineModule> orderedModules,
        Dictionary<Guid, ModuleResult> results,
        CancellationToken cancellationToken)
    {
        // Group modules by their dependency level for parallel initialization
        var dependencyLevels = CalculateDependencyLevels(orderedModules);
        
        foreach (var level in dependencyLevels.OrderBy(kvp => kvp.Key))
        {
            var modulesAtLevel = level.Value;
            var tasks = modulesAtLevel.Select(async module =>
            {
                var context = CreateModuleContext(cancellationToken);
                var result = await module.InitializeAsync(context, cancellationToken);
                lock (results)
                {
                    results[module.ModuleId] = result;
                }
                return result;
            });
            
            var levelResults = await Task.WhenAll(tasks);
            
            // Check for failures at this level
            if (levelResults.Any(r => !r.Success) && !_configuration.ContinueOnModuleFailure)
            {
                break;
            }
        }
    }
    
    private Dictionary<int, List<IEngineModule>> CalculateDependencyLevels(IEnumerable<IEngineModule> modules)
    {
        var levels = new Dictionary<int, List<IEngineModule>>();
        var moduleMap = modules.ToDictionary(m => m.ModuleId, m => m);
        var visited = new HashSet<Guid>();
        
        foreach (var module in modules)
        {
            var level = CalculateModuleLevel(module, moduleMap, visited, new HashSet<Guid>());
            if (!levels.ContainsKey(level))
            {
                levels[level] = new List<IEngineModule>();
            }
            levels[level].Add(module);
        }
        
        return levels;
    }
    
    private int CalculateModuleLevel(
        IEngineModule module,
        Dictionary<Guid, IEngineModule> moduleMap,
        HashSet<Guid> visited,
        HashSet<Guid> visiting)
    {
        if (visiting.Contains(module.ModuleId))
        {
            throw new InvalidOperationException($"Circular dependency detected involving module {module.ModuleId}");
        }
        
        if (visited.Contains(module.ModuleId))
        {
            return 0; // Already calculated
        }
        
        visiting.Add(module.ModuleId);
        
        var maxDependencyLevel = -1;
        foreach (var dependencyId in module.Dependencies)
        {
            if (moduleMap.TryGetValue(dependencyId, out var dependency))
            {
                var dependencyLevel = CalculateModuleLevel(dependency, moduleMap, visited, visiting);
                maxDependencyLevel = Math.Max(maxDependencyLevel, dependencyLevel);
            }
        }
        
        visiting.Remove(module.ModuleId);
        visited.Add(module.ModuleId);
        
        return maxDependencyLevel + 1;
    }
    
    private IModuleContext CreateModuleContext(CancellationToken cancellationToken)
    {
        return new ModuleContext(
            _serviceProvider,
            _loggerFactory,
            _configuration.Values,
            cancellationToken,
            this);
    }
    
    private List<IEngineModule> ResolveDependencyOrder()
    {
        var result = new List<IEngineModule>();
        var visited = new HashSet<Guid>();
        var visiting = new HashSet<Guid>();
        var moduleMap = _modules.ToDictionary(m => m.ModuleId, m => m);
        
        // Get execution context for priority calculation
        var context = GetExecutionContext();
        
        // Sort by priority first using the new priority comparer, then resolve dependencies
                    var comparer = new ModulePriorityComparer(context);
        var modulesByPriority = _modules.OrderBy(m => m, comparer);
        
        _logger.LogDebug("[{EngineId}] Module priority order (context: {Context}): {ModuleOrder}", 
            EngineId, context ?? "default", 
            string.Join(" -> ", modulesByPriority.Select(m => $"{m.FriendlyId}({GetModulePriorityInfo(m, context)})")));
        
        foreach (var module in modulesByPriority)
        {
            if (!visited.Contains(module.ModuleId))
            {
                ResolveDependenciesRecursive(module, moduleMap, visited, visiting, result);
            }
        }
        
        return result;
    }
    
    private string GetExecutionContext()
    {
        try
        {
            // Check configuration for execution context
            if (_configuration.Values.TryGetValue("ExecutionContext", out var contextObj))
            {
                return contextObj?.ToString() ?? "Production";
            }
            
            if (_configuration.Values.TryGetValue("Environment", out var envObj))
            {
                return envObj?.ToString() ?? "Production";
            }
            
            // Fall back to environment variable
            return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        }
        catch
        {
            return "Production";
        }
    }
    
    private string GetModulePriorityInfo(IEngineModule module, string? context)
    {
        if (module is IModulePriorityModule priorityModule)
        {
            var priority = priorityModule.ModulePriority;
            var effectivePriority = priority.GetEffectivePriority(context);
            return $"{priority.Level}({effectivePriority})";
        }
        
        return module.Priority.ToString();
    }
    
    private void ResolveDependenciesRecursive(
        IEngineModule module,
        Dictionary<Guid, IEngineModule> moduleMap,
        HashSet<Guid> visited,
        HashSet<Guid> visiting,
        List<IEngineModule> result)
    {
        if (visiting.Contains(module.ModuleId))
        {
            throw new InvalidOperationException($"Circular dependency detected involving module {module.ModuleId}");
        }
        
        if (visited.Contains(module.ModuleId))
        {
            return;
        }
        
        visiting.Add(module.ModuleId);
        
        // Resolve dependencies first
        foreach (var dependencyId in module.Dependencies)
        {
            if (moduleMap.TryGetValue(dependencyId, out var dependency))
            {
                ResolveDependenciesRecursive(dependency, moduleMap, visited, visiting, result);
            }
            else
            {
                throw new InvalidOperationException($"Module {module.ModuleId} depends on {dependencyId} which is not registered");
            }
        }
        
        visiting.Remove(module.ModuleId);
        visited.Add(module.ModuleId);
        result.Add(module);
    }
    
    private Dictionary<string, object> GetEnvironmentInfo()
    {
        return new Dictionary<string, object>
        {
            ["MachineName"] = Environment.MachineName,
            ["ProcessorCount"] = Environment.ProcessorCount,
            ["OSVersion"] = Environment.OSVersion.ToString(),
            ["WorkingDirectory"] = Environment.CurrentDirectory,
            ["Is64BitProcess"] = Environment.Is64BitProcess,
            ["CLRVersion"] = Environment.Version.ToString()
        };
    }
    
    private void OnModuleStateChanged(object? sender, ModuleStateChangedEventArgs e)
    {
        _logger.LogDebug("[{EngineId}] Module {FriendlyId} state changed: {PreviousState} -> {NewState}", 
            EngineId, ((IEngineModule)sender!).FriendlyId, e.PreviousState, e.NewState);
        
        ModuleStateChanged?.Invoke(this, e);
    }
    
    protected virtual void OnStateChanged(EngineState previousState, EngineState newState)
    {
        _logger.LogStateTransition("BurbujaEngine", previousState.ToString(), newState.ToString());
    }
    
    #region Microkernel Driver Management
    
    /// <summary>
    /// Register a driver with the microkernel.
    /// 
    /// MICROKERNEL PATTERN: Step 5 - Device Drivers in User Space
    /// Drivers represent hardware/external service abstractions that run in user space
    /// but communicate with the microkernel through well-defined interfaces.
    /// This separation allows for:
    /// - Easy driver updates without kernel changes
    /// - Better fault isolation (driver crashes don't crash kernel)
    /// - Modular driver architecture
    /// </summary>
    public async Task<DriverResult> RegisterDriverAsync(IEngineDriver driver)
    {
        if (driver == null) throw new ArgumentNullException(nameof(driver));
        
        try
        {
            await _driverRegistry.RegisterDriverAsync(driver);
            await _communicationBus.RegisterDriverAsync(driver);
            
            // Subscribe to driver state changes
            driver.StateChanged += OnDriverStateChanged;
            
            _logger.LogInformation("[{EngineId}] Registered driver '{DriverName}' (ID: {DriverId})", 
                EngineId, driver.DriverName, driver.DriverId);
            
            return DriverResult.Successful($"Driver {driver.DriverName} registered successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to register driver '{DriverName}': {Message}", 
                EngineId, driver.DriverName, ex.Message);
            return DriverResult.Failed(ex);
        }
    }
    
    /// <summary>
    /// Register a driver using a factory function.
    /// </summary>
    public async Task<DriverResult> RegisterDriverAsync<T>(Func<T> driverFactory) where T : class, IEngineDriver
    {
        if (driverFactory == null) throw new ArgumentNullException(nameof(driverFactory));
        
        try
        {
            var driver = driverFactory();
            return await RegisterDriverAsync(driver);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to create and register driver of type {DriverType}: {Message}", 
                EngineId, typeof(T).Name, ex.Message);
            return DriverResult.Failed(ex);
        }
    }
    
    /// <summary>
    /// Register a driver with dependency injection.
    /// </summary>
    public async Task<DriverResult> RegisterDriverAsync<T>() where T : class, IEngineDriver
    {
        try
        {
            var driver = _driverFactory.CreateDriver<T>();
            return await RegisterDriverAsync(driver);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to create and register driver of type {DriverType}: {Message}", 
                EngineId, typeof(T).Name, ex.Message);
            return DriverResult.Failed(ex);
        }
    }
    
    /// <summary>
    /// Unregister a driver from the microkernel.
    /// </summary>
    public async Task<DriverResult> UnregisterDriverAsync(Guid driverId)
    {
        try
        {
            var driver = _driverRegistry.GetDriver(driverId);
            if (driver == null)
            {
                return DriverResult.Failed($"Driver with ID {driverId} not found");
            }
            
            // Unsubscribe from driver state changes
            driver.StateChanged -= OnDriverStateChanged;
            
            await _communicationBus.UnregisterDriverAsync(driverId);
            await _driverRegistry.UnregisterDriverAsync(driverId);
            
            _logger.LogInformation("[{EngineId}] Unregistered driver '{DriverName}' (ID: {DriverId})", 
                EngineId, driver.DriverName, driverId);
            
            return DriverResult.Successful($"Driver {driver.DriverName} unregistered successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to unregister driver {DriverId}: {Message}", 
                EngineId, driverId, ex.Message);
            return DriverResult.Failed(ex);
        }
    }
    
    /// <summary>
    /// Get a driver by its ID.
    /// </summary>
    public IEngineDriver? GetDriver(Guid driverId)
    {
        return _driverRegistry.GetDriver(driverId);
    }
    
    /// <summary>
    /// Get a driver by type.
    /// </summary>
    public T? GetDriver<T>() where T : class, IEngineDriver
    {
        return _driverRegistry.GetDriver<T>();
    }
    
    /// <summary>
    /// Get all drivers of a specific type.
    /// </summary>
    public IEnumerable<IEngineDriver> GetDriversByType(DriverType type)
    {
        return _driverRegistry.GetDriversByType(type);
    }
    
    /// <summary>
    /// Initialize all drivers in the microkernel.
    /// </summary>
    public async Task<MicrokernelResult> InitializeDriversAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("[{EngineId}] Initializing drivers", EngineId);
            
            var drivers = _driverRegistry.GetAllDrivers().ToList();
            var results = new Dictionary<Guid, DriverResult>();
            
            foreach (var driver in drivers)
            {
                var context = CreateDriverContext(cancellationToken);
                var result = await driver.InitializeAsync(context, cancellationToken);
                results[driver.DriverId] = result;
                
                if (!result.Success)
                {
                    _logger.LogError("[{EngineId}] Failed to initialize driver {DriverName}: {Message}", 
                        EngineId, driver.DriverName, result.Message);
                }
            }
            
            var successful = results.Count(r => r.Value.Success);
            _logger.LogInformation("[{EngineId}] Initialized {SuccessCount}/{TotalCount} drivers successfully", 
                EngineId, successful, results.Count);
            
            return MicrokernelResult.Successful($"Initialized {successful}/{results.Count} drivers", stopwatch.Elapsed)
                .WithDriverResults(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to initialize drivers: {Message}", EngineId, ex.Message);
            return MicrokernelResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Start all drivers in the microkernel.
    /// </summary>
    public async Task<MicrokernelResult> StartDriversAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("[{EngineId}] Starting drivers", EngineId);
            
            var drivers = _driverRegistry.GetAllDrivers()
                .Where(d => d.State == DriverState.Initialized)
                .ToList();
            
            var results = new Dictionary<Guid, DriverResult>();
            
            foreach (var driver in drivers)
            {
                var result = await driver.StartAsync(cancellationToken);
                results[driver.DriverId] = result;
                
                if (!result.Success)
                {
                    _logger.LogError("[{EngineId}] Failed to start driver {DriverName}: {Message}", 
                        EngineId, driver.DriverName, result.Message);
                }
            }
            
            var successful = results.Count(r => r.Value.Success);
            _logger.LogInformation("[{EngineId}] Started {SuccessCount}/{TotalCount} drivers successfully", 
                EngineId, successful, results.Count);
            
            return MicrokernelResult.Successful($"Started {successful}/{results.Count} drivers", stopwatch.Elapsed)
                .WithDriverResults(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to start drivers: {Message}", EngineId, ex.Message);
            return MicrokernelResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Stop all drivers in the microkernel.
    /// </summary>
    public async Task<MicrokernelResult> StopDriversAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("[{EngineId}] Stopping drivers", EngineId);
            
            var drivers = _driverRegistry.GetAllDrivers()
                .Where(d => d.State == DriverState.Running)
                .ToList();
            
            var results = new Dictionary<Guid, DriverResult>();
            
            foreach (var driver in drivers)
            {
                var result = await driver.StopAsync(cancellationToken);
                results[driver.DriverId] = result;
                
                if (!result.Success)
                {
                    _logger.LogError("[{EngineId}] Failed to stop driver {DriverName}: {Message}", 
                        EngineId, driver.DriverName, result.Message);
                }
            }
            
            var successful = results.Count(r => r.Value.Success);
            _logger.LogInformation("[{EngineId}] Stopped {SuccessCount}/{TotalCount} drivers successfully", 
                EngineId, successful, results.Count);
            
            return MicrokernelResult.Successful($"Stopped {successful}/{results.Count} drivers", stopwatch.Elapsed)
                .WithDriverResults(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to stop drivers: {Message}", EngineId, ex.Message);
            return MicrokernelResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Shutdown all drivers in the microkernel.
    /// </summary>
    public async Task<MicrokernelResult> ShutdownDriversAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("[{EngineId}] Shutting down drivers", EngineId);
            
            var drivers = _driverRegistry.GetAllDrivers().ToList();
            var results = new Dictionary<Guid, DriverResult>();
            
            foreach (var driver in drivers)
            {
                var result = await driver.ShutdownAsync(cancellationToken);
                results[driver.DriverId] = result;
                
                if (!result.Success)
                {
                    _logger.LogError("[{EngineId}] Failed to shutdown driver {DriverName}: {Message}", 
                        EngineId, driver.DriverName, result.Message);
                }
            }
            
            var successful = results.Count(r => r.Value.Success);
            _logger.LogInformation("[{EngineId}] Shut down {SuccessCount}/{TotalCount} drivers successfully", 
                EngineId, successful, results.Count);
            
            return MicrokernelResult.Successful($"Shut down {successful}/{results.Count} drivers", stopwatch.Elapsed)
                .WithDriverResults(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to shutdown drivers: {Message}", EngineId, ex.Message);
            return MicrokernelResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Get health information about all drivers.
    /// </summary>
    public async Task<MicrokernelHealth> GetDriversHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var drivers = _driverRegistry.GetAllDrivers().ToList();
            var healthTasks = drivers.Select(async driver =>
            {
                try
                {
                    var health = await driver.GetHealthAsync(cancellationToken);
                    return (driver.DriverId, health, (Exception?)null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{EngineId}] Failed to get health for driver {DriverId}: {Message}", 
                        EngineId, driver.DriverId, ex.Message);
                    return (driver.DriverId, (DriverHealth?)null, ex);
                }
            });
            
            var healthResults = await Task.WhenAll(healthTasks);
            var driverHealths = new Dictionary<Guid, DriverHealth>();
            
            foreach (var (driverId, health, exception) in healthResults)
            {
                if (health != null)
                {
                    driverHealths[driverId] = health;
                }
                else if (exception != null)
                {
                    var driver = _driverRegistry.GetDriver(driverId);
                    driverHealths[driverId] = DriverHealth.Unhealthy(driverId, 
                        driver?.DriverName ?? "Unknown", $"Health check failed: {exception.Message}");
                }
            }
            
            var microkernelHealth = MicrokernelHealth.FromDrivers(driverHealths);
            return microkernelHealth with { ResponseTime = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to get drivers health: {Message}", EngineId, ex.Message);
            throw;
        }
    }
    
    /// <summary>
    /// Get diagnostic information about all drivers.
    /// </summary>
    public async Task<MicrokernelDiagnostics> GetDriversDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var drivers = _driverRegistry.GetAllDrivers().ToList();
            var diagnosticTasks = drivers.Select(async driver =>
            {
                try
                {
                    var diagnostics = await driver.GetDiagnosticsAsync(cancellationToken);
                    return (driver.DriverId, diagnostics, (Exception?)null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{EngineId}] Failed to get diagnostics for driver {DriverId}: {Message}", 
                        EngineId, driver.DriverId, ex.Message);
                    return (driver.DriverId, (DriverDiagnostics?)null, ex);
                }
            });
            
            var diagnosticResults = await Task.WhenAll(diagnosticTasks);
            var driverDiagnostics = new Dictionary<Guid, DriverDiagnostics>();
            
            foreach (var (driverId, diagnostics, exception) in diagnosticResults)
            {
                if (diagnostics != null)
                {
                    driverDiagnostics[driverId] = diagnostics;
                }
                else if (exception != null)
                {
                    var driver = _driverRegistry.GetDriver(driverId);
                    driverDiagnostics[driverId] = new DriverDiagnostics
                    {
                        DriverId = driverId,
                        DriverName = driver?.DriverName ?? "Unknown",
                        State = driver?.State ?? DriverState.Error,
                        Metadata = { ["diagnostics_error"] = exception.Message }
                    };
                }
            }
            
            var uptime = _startedAt.HasValue ? (TimeSpan?)(DateTime.UtcNow - _startedAt.Value) : null;
            
            return new MicrokernelDiagnostics
            {
                MicrokernelId = EngineId,
                Version = Version,
                CreatedAt = _createdAt,
                InitializedAt = _initializedAt,
                StartedAt = _startedAt,
                Uptime = uptime,
                DriverCount = drivers.Count,
                ModuleCount = _modules.Count,
                DriverDiagnostics = driverDiagnostics,
                Configuration = _configuration.Values.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Environment = GetEnvironmentInfo(),
                Metadata = new Dictionary<string, object>
                {
                    ["microkernel_version"] = "1.0.0",
                    ["architecture"] = "microkernel",
                    ["driver_communication_enabled"] = true
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{EngineId}] Failed to get drivers diagnostics: {Message}", EngineId, ex.Message);
            throw;
        }
    }
    
    /// <summary>
    /// Create a driver context for initialization.
    /// </summary>
    private IDriverContext CreateDriverContext(CancellationToken cancellationToken)
    {
        return new DriverContext(
            _serviceProvider,
            _loggerFactory,
            _configuration.Values,
            cancellationToken,
            this,
            _communicationBus);
    }
    
    /// <summary>
    /// Handle driver state changes.
    /// </summary>
    private void OnDriverStateChanged(object? sender, DriverStateChangedEventArgs e)
    {
        _logger.LogDebug("[{EngineId}] Driver {DriverName} state changed: {PreviousState} -> {NewState}", 
            EngineId, e.DriverName, e.PreviousState, e.NewState);
        
        DriverStateChanged?.Invoke(this, e);
    }
    
    #endregion
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && State != EngineState.Disposed)
        {
            try
            {
                // Try to shutdown gracefully
                if (State != EngineState.Shutdown)
                {
                    ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{EngineId}] Error during engine disposal: {Message}", EngineId, ex.Message);
            }
            finally
            {
                // Dispose modules
                foreach (var module in _modules.OfType<IDisposable>())
                {
                    try
                    {
                        module.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{EngineId}] Error disposing module: {Message}", EngineId, ex.Message);
                    }
                }
                
                // Dispose microkernel components
                try
                {
                    _communicationBus?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{EngineId}] Error disposing communication bus: {Message}", EngineId, ex.Message);
                }
                
                try
                {
                    _driverRegistry?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{EngineId}] Error disposing driver registry: {Message}", EngineId, ex.Message);
                }
                
                try
                {
                    _driverFactory?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{EngineId}] Error disposing driver factory: {Message}", EngineId, ex.Message);
                }
                
                _engineCancellationTokenSource?.Dispose();
                State = EngineState.Disposed;
            }
        }
    }
}
