using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Engine.Drivers;

/// <summary>
/// Inter-driver communication message.
/// Enables microkernel-style IPC between drivers.
/// </summary>
public record DriverMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public Guid SourceDriverId { get; init; }
    public Guid TargetDriverId { get; init; }
    public string MessageType { get; init; } = string.Empty;
    public object? Payload { get; init; }
    public bool RequiresResponse { get; init; }
    public Guid? ResponseToMessageId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Dictionary<string, object> Headers { get; init; } = new();
}

/// <summary>
/// Communication bus for inter-driver messaging.
/// Implements the microkernel IPC mechanism.
/// </summary>
public interface IDriverCommunicationBus : IDisposable
{
    /// <summary>
    /// Send a message to a specific driver.
    /// </summary>
    Task<DriverMessage?> SendMessageAsync(DriverMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send a message and wait for response.
    /// </summary>
    Task<DriverMessage?> SendMessageAndWaitForResponseAsync(DriverMessage message, TimeSpan timeout, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Broadcast a message to all drivers of a specific type.
    /// </summary>
    Task BroadcastMessageAsync(DriverType targetType, DriverMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Register a driver for receiving messages.
    /// </summary>
    Task RegisterDriverAsync(IEngineDriver driver);
    
    /// <summary>
    /// Unregister a driver from receiving messages.
    /// </summary>
    Task UnregisterDriverAsync(Guid driverId);
    
    /// <summary>
    /// Subscribe to messages of a specific type.
    /// </summary>
    Task SubscribeToMessageTypeAsync(Guid driverId, string messageType, Func<DriverMessage, Task<DriverMessage?>> handler);
    
    /// <summary>
    /// Event fired when a message is received but no handler is registered.
    /// </summary>
    event EventHandler<DriverMessage>? UnhandledMessage;
}

/// <summary>
/// Driver registry for managing driver instances in the microkernel.
/// </summary>
public interface IDriverRegistry : IDisposable
{
    /// <summary>
    /// Register a driver instance.
    /// </summary>
    Task RegisterDriverAsync(IEngineDriver driver);
    
    /// <summary>
    /// Unregister a driver instance.
    /// </summary>
    Task UnregisterDriverAsync(Guid driverId);
    
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
    /// Get all registered drivers.
    /// </summary>
    IReadOnlyList<IEngineDriver> GetAllDrivers();
    
    /// <summary>
    /// Check if a driver is registered.
    /// </summary>
    bool IsDriverRegistered(Guid driverId);
    
    /// <summary>
    /// Event fired when a driver is registered.
    /// </summary>
    event EventHandler<IEngineDriver>? DriverRegistered;
    
    /// <summary>
    /// Event fired when a driver is unregistered.
    /// </summary>
    event EventHandler<Guid>? DriverUnregistered;
}

/// <summary>
/// Factory for creating driver instances.
/// </summary>
public interface IDriverFactory : IDisposable
{
    /// <summary>
    /// Create a driver instance by type.
    /// </summary>
    T CreateDriver<T>() where T : class, IEngineDriver;
    
    /// <summary>
    /// Create a driver instance by type name.
    /// </summary>
    IEngineDriver? CreateDriver(string driverTypeName);
    
    /// <summary>
    /// Register a driver type with the factory.
    /// </summary>
    void RegisterDriverType<T>() where T : class, IEngineDriver;
    
    /// <summary>
    /// Get available driver types.
    /// </summary>
    IEnumerable<Type> GetAvailableDriverTypes();
}

/// <summary>
/// Context implementation for drivers.
/// </summary>
internal class DriverContext : IDriverContext
{
    public IServiceProvider ServiceProvider { get; }
    public ILoggerFactory LoggerFactory { get; }
    public IReadOnlyDictionary<string, object> Configuration { get; }
    public CancellationToken CancellationToken { get; }
    public IBurbujaEngine Engine { get; }
    public IDriverCommunicationBus CommunicationBus { get; }
    
    public DriverContext(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IReadOnlyDictionary<string, object> configuration,
        CancellationToken cancellationToken,
        IBurbujaEngine engine,
        IDriverCommunicationBus communicationBus)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        CancellationToken = cancellationToken;
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        CommunicationBus = communicationBus ?? throw new ArgumentNullException(nameof(communicationBus));
    }
}

/// <summary>
/// Result of a microkernel operation.
/// </summary>
public record MicrokernelResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan Duration { get; init; }
    public Dictionary<Guid, DriverResult> DriverResults { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    public static MicrokernelResult Successful(string? message = null, TimeSpan duration = default) =>
        new() { Success = true, Message = message, Duration = duration };
    
    public static MicrokernelResult Failed(string message, Exception? exception = null, TimeSpan duration = default) =>
        new() { Success = false, Message = message, Exception = exception, Duration = duration };
    
    public static MicrokernelResult Failed(Exception exception, TimeSpan duration = default) =>
        new() { Success = false, Message = exception.Message, Exception = exception, Duration = duration };
    
    public MicrokernelResult WithDriverResults(Dictionary<Guid, DriverResult> driverResults)
    {
        return this with { DriverResults = driverResults };
    }
}

/// <summary>
/// Health status for the microkernel system.
/// </summary>
public enum MicrokernelHealthStatus
{
    Healthy,
    Warning,
    Critical,
    Unhealthy,
    Unknown
}

/// <summary>
/// Health information for the microkernel system.
/// </summary>
public record MicrokernelHealth
{
    public Guid MicrokernelId { get; init; }
    public MicrokernelHealthStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public TimeSpan ResponseTime { get; init; }
    public Dictionary<Guid, DriverHealth> DriverHealths { get; init; } = new();
    public Dictionary<string, object> Details { get; init; } = new();
    
    public static MicrokernelHealth FromDrivers(Dictionary<Guid, DriverHealth> driverHealths)
    {
        var status = MicrokernelHealthStatus.Healthy;
        var unhealthyCount = 0;
        var warningCount = 0;
        
        foreach (var driverHealth in driverHealths.Values)
        {
            switch (driverHealth.Status)
            {
                case DriverHealthStatus.Unhealthy:
                case DriverHealthStatus.Critical:
                    unhealthyCount++;
                    break;
                case DriverHealthStatus.Warning:
                    warningCount++;
                    break;
            }
        }
        
        if (unhealthyCount > 0)
        {
            status = unhealthyCount >= driverHealths.Count / 2 
                ? MicrokernelHealthStatus.Critical 
                : MicrokernelHealthStatus.Unhealthy;
        }
        else if (warningCount > 0)
        {
            status = MicrokernelHealthStatus.Warning;
        }
        
        var message = status switch
        {
            MicrokernelHealthStatus.Healthy => $"All {driverHealths.Count} drivers are healthy",
            MicrokernelHealthStatus.Warning => $"{warningCount} driver(s) have warnings",
            MicrokernelHealthStatus.Unhealthy => $"{unhealthyCount} driver(s) are unhealthy",
            MicrokernelHealthStatus.Critical => $"Critical: {unhealthyCount}/{driverHealths.Count} drivers are unhealthy",
            _ => "Unknown health status"
        };
        
        return new MicrokernelHealth
        {
            MicrokernelId = Guid.Empty, // Will be set by caller
            Status = status,
            Message = message,
            DriverHealths = driverHealths,
            Details = new Dictionary<string, object>
            {
                ["total_drivers"] = driverHealths.Count,
                ["healthy_drivers"] = driverHealths.Count - unhealthyCount - warningCount,
                ["warning_drivers"] = warningCount,
                ["unhealthy_drivers"] = unhealthyCount
            }
        };
    }
}

/// <summary>
/// Diagnostic information for the microkernel system.
/// </summary>
public class MicrokernelDiagnostics
{
    public Guid MicrokernelId { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? InitializedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public TimeSpan? Uptime { get; set; }
    public int DriverCount { get; set; }
    public int ModuleCount { get; set; }
    public Dictionary<Guid, DriverDiagnostics> DriverDiagnostics { get; set; } = new();
    public Dictionary<string, object> Configuration { get; set; } = new();
    public Dictionary<string, object> Environment { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}
