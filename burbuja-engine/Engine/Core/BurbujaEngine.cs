using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace BurbujaEngine.Engine.Core;

/// <summary>
/// Main BurbujaEngine implementation.
/// Manages the lifecycle of all modules.
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
    
    public Guid EngineId { get; }
    public string Version { get; }
    public IReadOnlyList<IEngineModule> Modules => _modules.AsReadOnly();
    public IServiceProvider ServiceProvider => _serviceProvider;
    
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
        
        _logger.LogInformation("[{EngineId}] BurbujaEngine created with {ModuleCount} modules", 
            EngineId, _modules.Count);
    }
    
    /// <summary>
    /// Register a module with the engine.
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
        _logger.LogInformation("[{EngineId}] Engine state changed: {PreviousState} -> {NewState}", 
            EngineId, previousState, newState);
    }
    
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
                
                _engineCancellationTokenSource?.Dispose();
                State = EngineState.Disposed;
            }
        }
    }
}
