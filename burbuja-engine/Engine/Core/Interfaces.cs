using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BurbujaEngine.Engine.Core;

/// <summary>
/// Core interface for all engine modules.
/// 
/// MICROKERNEL PATTERN: Step 2 - Design Interfaces
/// 
/// This interface represents the fundamental contract between the microkernel
/// and user-space modules. It defines the well-structured interface that enables:
/// 
/// - Module lifecycle management (initialize, start, stop, shutdown)
/// - Dependency declaration and resolution
/// - Health monitoring and diagnostics
/// - Service configuration and registration
/// - Event-driven state communication
/// 
/// INTERFACE DESIGN PRINCIPLES:
/// - Stability: Interface remains stable while implementations can evolve
/// - Completeness: Covers all aspects of module lifecycle
/// - Modularity: Modules can be developed, tested, and deployed independently
/// - Observability: Built-in health and diagnostic capabilities
/// 
/// This demonstrates the microkernel architecture principle of clean separation
/// between core functionality (provided by the microkernel) and extended
/// functionality (provided by user-space modules through this interface).
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
/// 
/// MICROKERNEL PATTERN: Step 2 - Interface Design for Microkernel Services
/// 
/// This context interface represents how the microkernel exposes its core services
/// to user-space modules in a controlled and well-defined manner:
/// 
/// - Service Provider: Dependency injection container access
/// - Logger Factory: Centralized logging infrastructure
/// - Configuration: Access to system-wide configuration
/// - Cancellation Token: Cooperative cancellation support
/// - Engine Reference: Access to microkernel services and other modules
/// 
/// CONTEXT PATTERN BENEFITS:
/// - Controlled access: Modules only get what they need from the microkernel
/// - Service abstraction: Modules don't directly depend on microkernel internals
/// - Testing support: Context can be mocked for unit testing
/// - Security boundary: Limits what user-space code can access
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
/// Main engine interface that implements simplified microkernel architecture.
/// 
/// SIMPLIFIED MICROKERNEL PATTERN: True Minimal Core
/// 
/// This interface represents the MICROKERNEL itself - the minimal, essential core
/// that manages all user-space services and provides fundamental system services:
/// 
/// Step 1 - Core Functionality (Minimal):
/// - Module lifecycle management (register, initialize, start, stop, shutdown)
/// - Service coordination and dependency resolution
/// - State management and health monitoring
/// - Configuration management
/// 
/// Step 2 - Well-Defined Interfaces:
/// - Clean contracts for module interaction
/// - Context-based service provisioning
/// - Event-driven communication
/// 
/// Step 3 - Modularized Services:
/// - All business logic runs in user-space modules
/// - Direct service dependency injection (no complex IPC)
/// - Standard .NET DI patterns for inter-module communication
/// 
/// Step 6 - Service Management:
/// - Dynamic service registration and discovery
/// - Dependency-aware initialization
/// - Health monitoring and diagnostics
/// 
/// SIMPLIFIED MICROKERNEL CHARACTERISTICS:
/// - Truly minimal core: Only essential lifecycle services in the microkernel
/// - User-space modules: All business logic (database, monitoring, etc.) as modules
/// - Direct DI: Standard dependency injection instead of complex message passing
/// - Fault isolation: Module failures don't crash the microkernel
/// - Simple modularity: Easy to add, remove, or update functionality
/// 
/// This IS a true microkernel - managing only essential services while delegating
/// all business functionality to well-isolated user-space modules.
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
