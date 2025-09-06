using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BurbujaEngine.Engine.Drivers;

namespace BurbujaEngine.Engine.Core;

/// <summary>
/// Core interface for all engine modules.
/// </summary>
public interface IEngineModule
{
    /// <summary>
    /// Unique identifier for this module.
    /// </summary>
    Guid ModuleId { get; }
    
    /// <summary>
    /// Human-readable name of the module.
    /// </summary>
    string ModuleName { get; }
    
    /// <summary>
    /// Friendly identifier for debugging purposes. Combines module name with short ID.
    /// </summary>
    string FriendlyId { get; }
    
    /// <summary>
    /// Version of the module.
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// Dependencies that this module requires to be loaded before it.
    /// </summary>
    IReadOnlyList<Guid> Dependencies { get; }
    
    /// <summary>
    /// Priority of the module (lower numbers = higher priority).
    /// Used for initialization order when no explicit dependencies exist.
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// Current state of the module.
    /// </summary>
    ModuleState State { get; }
    
    /// <summary>
    /// Initialize the module with the given context.
    /// </summary>
    Task<ModuleResult> InitializeAsync(IModuleContext context, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Start the module and begin its operations.
    /// </summary>
    Task<ModuleResult> StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop the module and cleanup resources.
    /// </summary>
    Task<ModuleResult> StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Shutdown the module gracefully.
    /// </summary>
    Task<ModuleResult> ShutdownAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get health information about the module.
    /// </summary>
    Task<ModuleHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get diagnostic information about the module.
    /// </summary>
    Task<ModuleDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Configure services that this module provides.
    /// Called during the service configuration phase.
    /// </summary>
    void ConfigureServices(IServiceCollection services);
    
    /// <summary>
    /// Event fired when module state changes.
    /// </summary>
    event EventHandler<ModuleStateChangedEventArgs>? StateChanged;
}

/// <summary>
/// Context provided to modules during initialization.
/// Contains all necessary dependencies and configuration.
/// </summary>
public interface IModuleContext
{
    /// <summary>
    /// Service provider for dependency injection.
    /// </summary>
    IServiceProvider ServiceProvider { get; }
    
    /// <summary>
    /// Logger factory for creating module-specific loggers.
    /// </summary>
    ILoggerFactory LoggerFactory { get; }
    
    /// <summary>
    /// Configuration values accessible to the module.
    /// </summary>
    IReadOnlyDictionary<string, object> Configuration { get; }
    
    /// <summary>
    /// Cancellation token for cancelling initialization.
    /// </summary>
    CancellationToken CancellationToken { get; }
    
    /// <summary>
    /// Engine instance that owns this module.
    /// </summary>
    IBurbujaEngine Engine { get; }
}

/// <summary>
/// Main engine interface that implements microkernel architecture.
/// This IS the microkernel - managing modules, drivers, and providing core IPC services.
/// </summary>
public interface IBurbujaEngine
{
    /// <summary>
    /// Unique identifier for this microkernel instance.
    /// </summary>
    Guid EngineId { get; }
    
    /// <summary>
    /// Version of the microkernel.
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// Current state of the microkernel.
    /// </summary>
    EngineState State { get; }
    
    /// <summary>
    /// All registered modules in the microkernel.
    /// </summary>
    IReadOnlyList<IEngineModule> Modules { get; }
    
    /// <summary>
    /// Service provider for the microkernel.
    /// </summary>
    IServiceProvider ServiceProvider { get; }
    
    /// <summary>
    /// Driver registry - core microkernel service.
    /// </summary>
    IDriverRegistry DriverRegistry { get; }
    
    /// <summary>
    /// Communication bus - core microkernel IPC service.
    /// </summary>
    IDriverCommunicationBus CommunicationBus { get; }
    
    /// <summary>
    /// Driver factory - core microkernel service.
    /// </summary>
    IDriverFactory DriverFactory { get; }
    
    /// <summary>
    /// Register a module with the engine.
    /// </summary>
    IBurbujaEngine RegisterModule(IEngineModule module);
    
    /// <summary>
    /// Register a module using a factory function.
    /// </summary>
    IBurbujaEngine RegisterModule<T>(Func<T> moduleFactory) where T : class, IEngineModule;
    
    /// <summary>
    /// Register a module with dependency injection.
    /// </summary>
    IBurbujaEngine RegisterModule<T>() where T : class, IEngineModule;
    
    /// <summary>
    /// Get a module by its ID.
    /// </summary>
    T? GetModule<T>() where T : class, IEngineModule;
    
    /// <summary>
    /// Get a module by its ID.
    /// </summary>
    IEngineModule? GetModule(Guid moduleId);
    
    /// <summary>
    /// Initialize all modules in dependency order.
    /// </summary>
    Task<EngineResult> InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Start the engine and all its modules.
    /// </summary>
    Task<EngineResult> StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop the engine and all its modules.
    /// </summary>
    Task<EngineResult> StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Shutdown the engine gracefully.
    /// </summary>
    Task<EngineResult> ShutdownAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get health information about the engine and all modules.
    /// </summary>
    Task<EngineHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get diagnostic information about the engine and all modules/drivers.
    /// </summary>
    Task<EngineDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
    
    // Microkernel Driver Management
    
    /// <summary>
    /// Register a driver with the microkernel.
    /// </summary>
    Task<DriverResult> RegisterDriverAsync(IEngineDriver driver);
    
    /// <summary>
    /// Register a driver using a factory function.
    /// </summary>
    Task<DriverResult> RegisterDriverAsync<T>(Func<T> driverFactory) where T : class, IEngineDriver;
    
    /// <summary>
    /// Register a driver with dependency injection.
    /// </summary>
    Task<DriverResult> RegisterDriverAsync<T>() where T : class, IEngineDriver;
    
    /// <summary>
    /// Unregister a driver from the microkernel.
    /// </summary>
    Task<DriverResult> UnregisterDriverAsync(Guid driverId);
    
    /// <summary>
    /// Get a driver by its ID.
    /// </summary>
    IEngineDriver? GetDriver(Guid driverId);
    
    /// <summary>
    /// Get a driver by type.
    /// </summary>
    T? GetDriver<T>() where T : class, IEngineDriver;
    
    /// <summary>
    /// Get all drivers of a specific type.
    /// </summary>
    IEnumerable<IEngineDriver> GetDriversByType(DriverType type);
    
    /// <summary>
    /// Event fired when engine state changes.
    /// </summary>
    event EventHandler<EngineStateChangedEventArgs>? StateChanged;
    
    /// <summary>
    /// Event fired when a module state changes.
    /// </summary>
    event EventHandler<ModuleStateChangedEventArgs>? ModuleStateChanged;
}

/// <summary>
/// Factory interface for creating engine instances.
/// </summary>
public interface IEngineFactory
{
    /// <summary>
    /// Create a new engine instance.
    /// </summary>
    IBurbujaEngine CreateEngine(Guid engineId, EngineConfiguration configuration);
}

/// <summary>
/// Interface for engine configuration.
/// </summary>
public interface IEngineConfiguration
{
    /// <summary>
    /// Engine identifier.
    /// </summary>
    Guid EngineId { get; }
    
    /// <summary>
    /// Engine version.
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// Configuration values.
    /// </summary>
    IReadOnlyDictionary<string, object> Values { get; }
    
    /// <summary>
    /// Timeout for module operations.
    /// </summary>
    TimeSpan ModuleTimeout { get; }
    
    /// <summary>
    /// Maximum time to wait for shutdown.
    /// </summary>
    TimeSpan ShutdownTimeout { get; }
    
    /// <summary>
    /// Whether to continue engine startup if a module fails.
    /// </summary>
    bool ContinueOnModuleFailure { get; }
    
    /// <summary>
    /// Whether to enable parallel module initialization.
    /// </summary>
    bool EnableParallelInitialization { get; }
}
