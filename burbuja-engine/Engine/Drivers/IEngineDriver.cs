using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Engine.Drivers;

/// <summary>
/// Base interface for all engine drivers.
/// 
/// MICROKERNEL PATTERN: Step 5 - Device Drivers in User Space
/// 
/// Drivers represent the hardware/external service abstraction layer in the microkernel
/// architecture. They operate in user space but provide controlled access to system
/// resources through the microkernel's IPC mechanisms.
/// 
/// DRIVER ARCHITECTURE BENEFITS:
/// - Isolation: Driver failures don't crash the microkernel or other drivers
/// - Modularity: Drivers can be updated independently of the core system
/// - Flexibility: Easy to add support for new hardware or external services
/// - Security: Drivers operate with limited privileges through microkernel APIs
/// 
/// USER-SPACE DRIVER CHARACTERISTICS:
/// - Hardware abstraction: Provide clean interfaces to hardware/external systems
/// - Message handling: Support IPC communication with other system components
/// - Lifecycle management: Initialize, start, stop, and shutdown cleanly
/// - Health monitoring: Report status and handle error conditions gracefully
/// - Service configuration: Register services they provide with the DI container
/// 
/// DRIVER TYPES SUPPORTED:
/// - Database drivers (MongoDB, SQL Server, etc.)
/// - Network communication drivers  
/// - Storage and file system drivers
/// - Security and authentication drivers
/// - External API integration drivers
/// - Custom plugin drivers
/// 
/// This interface demonstrates the microkernel principle of moving complex,
/// non-essential functionality out of the kernel and into user space where
/// it can be managed more safely and flexibly.
/// </summary>
public interface IEngineDriver
{
    /// <summary>
    /// Unique identifier for this driver.
    /// </summary>
    Guid DriverId { get; }
    
    /// <summary>
    /// Human-readable name of the driver.
    /// </summary>
    string DriverName { get; }
    
    /// <summary>
    /// Version of the driver.
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// Driver type (Database, Network, Storage, etc.).
    /// </summary>
    DriverType Type { get; }
    
    /// <summary>
    /// Current state of the driver.
    /// </summary>
    DriverState State { get; }
    
    /// <summary>
    /// Initialize the driver with the microkernel context.
    /// </summary>
    Task<DriverResult> InitializeAsync(IDriverContext context, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Start the driver operations.
    /// </summary>
    Task<DriverResult> StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop the driver operations.
    /// </summary>
    Task<DriverResult> StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Shutdown the driver gracefully.
    /// </summary>
    Task<DriverResult> ShutdownAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get health information about the driver.
    /// </summary>
    Task<DriverHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get diagnostic information about the driver.
    /// </summary>
    Task<DriverDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Handle inter-driver communication requests.
    /// </summary>
    Task<DriverMessage> HandleMessageAsync(DriverMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Configure services that this driver provides.
    /// </summary>
    void ConfigureServices(IServiceCollection services);
    
    /// <summary>
    /// Event fired when driver state changes.
    /// </summary>
    event EventHandler<DriverStateChangedEventArgs>? StateChanged;
}

/// <summary>
/// Types of drivers in the microkernel system.
/// </summary>
public enum DriverType
{
    /// <summary>
    /// Database drivers (MongoDB, SQL Server, etc.).
    /// </summary>
    Database,
    
    /// <summary>
    /// Network communication drivers.
    /// </summary>
    Network,
    
    /// <summary>
    /// File system and storage drivers.
    /// </summary>
    Storage,
    
    /// <summary>
    /// Security and authentication drivers.
    /// </summary>
    Security,
    
    /// <summary>
    /// Caching system drivers.
    /// </summary>
    Cache,
    
    /// <summary>
    /// Message queue and event system drivers.
    /// </summary>
    Messaging,
    
    /// <summary>
    /// External API integration drivers.
    /// </summary>
    ExternalApi,
    
    /// <summary>
    /// Hardware abstraction drivers.
    /// </summary>
    Hardware,
    
    /// <summary>
    /// Custom or plugin drivers.
    /// </summary>
    Custom
}

/// <summary>
/// States that a driver can be in.
/// </summary>
public enum DriverState
{
    /// <summary>
    /// Driver has been created but not yet initialized.
    /// </summary>
    Created,
    
    /// <summary>
    /// Driver is currently being initialized.
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Driver has been successfully initialized.
    /// </summary>
    Initialized,
    
    /// <summary>
    /// Driver is currently starting up.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Driver is running and operational.
    /// </summary>
    Running,
    
    /// <summary>
    /// Driver is currently stopping.
    /// </summary>
    Stopping,
    
    /// <summary>
    /// Driver has been stopped.
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Driver is shutting down.
    /// </summary>
    ShuttingDown,
    
    /// <summary>
    /// Driver has been shut down.
    /// </summary>
    Shutdown,
    
    /// <summary>
    /// Driver is in an error state.
    /// </summary>
    Error,
    
    /// <summary>
    /// Driver has been disposed.
    /// </summary>
    Disposed
}

/// <summary>
/// Context provided to drivers during initialization.
/// Contains microkernel services and configuration.
/// 
/// MICROKERNEL PATTERN: Step 2 - Interface Design for Driver-Kernel Communication
/// 
/// This context provides drivers with controlled access to microkernel services:
/// - Service Provider: Access to shared services through dependency injection
/// - Logger Factory: Centralized logging infrastructure  
/// - Configuration: System and driver-specific configuration
/// - Engine Reference: Access to microkernel for service discovery
/// - Communication Bus: IPC mechanism for inter-driver communication
/// 
/// SECURITY AND ISOLATION:
/// - Controlled access: Drivers only get necessary microkernel services
/// - Message passing: Communication happens through well-defined IPC channels
/// - Service boundaries: Clear separation between kernel and user space
/// </summary>
public interface IDriverContext
{
    /// <summary>
    /// Service provider from the microkernel.
    /// </summary>
    IServiceProvider ServiceProvider { get; }
    
    /// <summary>
    /// Logger factory for creating driver loggers.
    /// </summary>
    ILoggerFactory LoggerFactory { get; }
    
    /// <summary>
    /// Configuration values for the driver.
    /// </summary>
    IReadOnlyDictionary<string, object> Configuration { get; }
    
    /// <summary>
    /// Cancellation token for the driver operation.
    /// </summary>
    CancellationToken CancellationToken { get; }
    
    /// <summary>
    /// Reference to the microkernel engine.
    /// </summary>
    IBurbujaEngine Engine { get; }
    
    /// <summary>
    /// Driver communication bus for inter-driver communication.
    /// </summary>
    IDriverCommunicationBus CommunicationBus { get; }
}

/// <summary>
/// Result of a driver operation.
/// </summary>
public record DriverResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan Duration { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    public static DriverResult Successful(string? message = null, TimeSpan duration = default) =>
        new() { Success = true, Message = message, Duration = duration };
    
    public static DriverResult Failed(string message, Exception? exception = null, TimeSpan duration = default) =>
        new() { Success = false, Message = message, Exception = exception, Duration = duration };
    
    public static DriverResult Failed(Exception exception, TimeSpan duration = default) =>
        new() { Success = false, Message = exception.Message, Exception = exception, Duration = duration };
}

/// <summary>
/// Health status for drivers.
/// </summary>
public enum DriverHealthStatus
{
    Healthy,
    Warning,
    Critical,
    Unhealthy,
    Unknown
}

/// <summary>
/// Health information for a driver.
/// </summary>
public record DriverHealth
{
    public Guid DriverId { get; init; }
    public string DriverName { get; init; } = string.Empty;
    public DriverHealthStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public TimeSpan ResponseTime { get; init; }
    public Dictionary<string, object> Details { get; init; } = new();
    
    public static DriverHealth Healthy(Guid driverId, string driverName, string message = "Driver is healthy") =>
        new() { DriverId = driverId, DriverName = driverName, Status = DriverHealthStatus.Healthy, Message = message };
    
    public static DriverHealth Warning(Guid driverId, string driverName, string message) =>
        new() { DriverId = driverId, DriverName = driverName, Status = DriverHealthStatus.Warning, Message = message };
    
    public static DriverHealth Critical(Guid driverId, string driverName, string message) =>
        new() { DriverId = driverId, DriverName = driverName, Status = DriverHealthStatus.Critical, Message = message };
    
    public static DriverHealth Unhealthy(Guid driverId, string driverName, string message) =>
        new() { DriverId = driverId, DriverName = driverName, Status = DriverHealthStatus.Unhealthy, Message = message };
}

/// <summary>
/// Diagnostic information for a driver.
/// </summary>
public class DriverDiagnostics
{
    public Guid DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DriverType Type { get; set; }
    public DriverState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? InitializedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public TimeSpan? Uptime { get; set; }
    public Dictionary<string, object> Configuration { get; set; } = new();
    public Dictionary<string, object> Performance { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Event arguments for driver state changes.
/// </summary>
public class DriverStateChangedEventArgs : EventArgs
{
    public Guid DriverId { get; }
    public string DriverName { get; }
    public DriverState PreviousState { get; }
    public DriverState NewState { get; }
    public DateTime Timestamp { get; }
    
    public DriverStateChangedEventArgs(Guid driverId, string driverName, DriverState previousState, DriverState newState)
    {
        DriverId = driverId;
        DriverName = driverName;
        PreviousState = previousState;
        NewState = newState;
        Timestamp = DateTime.UtcNow;
    }
}
