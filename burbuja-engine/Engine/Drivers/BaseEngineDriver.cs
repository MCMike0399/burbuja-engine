using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Engine.Drivers;

/// <summary>
/// Base implementation for engine drivers.
/// Provides common functionality for all microkernel drivers.
/// </summary>
public abstract class BaseEngineDriver : IEngineDriver, IDisposable
{
    private readonly object _stateLock = new();
    private DriverState _state = DriverState.Created;
    
    protected ILogger Logger { get; private set; } = default!;
    protected IDriverContext Context { get; private set; } = default!;
    protected DateTime CreatedAt { get; } = DateTime.UtcNow;
    protected DateTime? InitializedAt { get; private set; }
    protected DateTime? StartedAt { get; private set; }
    protected CancellationTokenSource? DriverCancellationTokenSource { get; private set; }
    
    public Guid DriverId { get; } = Guid.NewGuid();
    public abstract string DriverName { get; }
    public virtual string Version => "1.0.0";
    public abstract DriverType Type { get; }
    
    public DriverState State
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
            DriverState oldState;
            lock (_stateLock)
            {
                oldState = _state;
                _state = value;
            }
            
            if (oldState != value)
            {
                OnStateChanged(oldState, value);
                StateChanged?.Invoke(this, new DriverStateChangedEventArgs(DriverId, DriverName, oldState, value));
            }
        }
    }
    
    public event EventHandler<DriverStateChangedEventArgs>? StateChanged;
    
    /// <summary>
    /// Initialize the driver with the given context.
    /// </summary>
    public async Task<DriverResult> InitializeAsync(IDriverContext context, CancellationToken cancellationToken = default)
    {
        if (State != DriverState.Created)
        {
            return DriverResult.Failed($"Driver {DriverId} is in state {State}, expected Created");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = DriverState.Initializing;
        
        try
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Logger = context.LoggerFactory.CreateLogger(GetType());
            DriverCancellationTokenSource = new CancellationTokenSource();
            
            LogInfo($"Initializing driver {DriverName}");
            
            // Register with communication bus
            await Context.CommunicationBus.RegisterDriverAsync(this);
            
            // Call template method for specific initialization
            await OnInitializeAsync(cancellationToken);
            
            InitializedAt = DateTime.UtcNow;
            State = DriverState.Initialized;
            
            LogInfo($"Driver {DriverName} initialized successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return DriverResult.Successful($"Driver {DriverName} initialized", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = DriverState.Error;
            LogError($"Failed to initialize driver {DriverName}: {ex.Message}", ex);
            return DriverResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Start the driver and begin its operations.
    /// </summary>
    public async Task<DriverResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (State != DriverState.Initialized)
        {
            return DriverResult.Failed($"Driver {DriverId} is in state {State}, expected Initialized");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = DriverState.Starting;
        
        try
        {
            LogInfo($"Starting driver {DriverName}");
            
            // Call template method for specific startup logic
            await OnStartAsync(cancellationToken);
            
            StartedAt = DateTime.UtcNow;
            State = DriverState.Running;
            
            LogInfo($"Driver {DriverName} started successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return DriverResult.Successful($"Driver {DriverName} started", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = DriverState.Error;
            LogError($"Failed to start driver {DriverName}: {ex.Message}", ex);
            return DriverResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Stop the driver and cleanup resources.
    /// </summary>
    public async Task<DriverResult> StopAsync(CancellationToken cancellationToken = default)
    {
        if (State != DriverState.Running)
        {
            return DriverResult.Successful($"Driver {DriverName} is not running (state: {State})");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = DriverState.Stopping;
        
        try
        {
            LogInfo($"Stopping driver {DriverName}");
            
            // Cancel driver operations
            DriverCancellationTokenSource?.Cancel();
            
            // Call template method for specific stop logic
            await OnStopAsync(cancellationToken);
            
            State = DriverState.Stopped;
            
            LogInfo($"Driver {DriverName} stopped successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return DriverResult.Successful($"Driver {DriverName} stopped", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = DriverState.Error;
            LogError($"Failed to stop driver {DriverName}: {ex.Message}", ex);
            return DriverResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Shutdown the driver gracefully.
    /// </summary>
    public async Task<DriverResult> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (State == DriverState.Shutdown || State == DriverState.Disposed)
        {
            return DriverResult.Successful($"Driver {DriverName} already shut down");
        }
        
        var stopwatch = Stopwatch.StartNew();
        State = DriverState.ShuttingDown;
        
        try
        {
            LogInfo($"Shutting down driver {DriverName}");
            
            // Stop first if running
            if (State == DriverState.Running)
            {
                await StopAsync(cancellationToken);
            }
            
            // Unregister from communication bus
            if (Context?.CommunicationBus != null)
            {
                await Context.CommunicationBus.UnregisterDriverAsync(DriverId);
            }
            
            // Call template method for specific shutdown logic
            await OnShutdownAsync(cancellationToken);
            
            State = DriverState.Shutdown;
            
            LogInfo($"Driver {DriverName} shut down successfully in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            
            return DriverResult.Successful($"Driver {DriverName} shut down", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            State = DriverState.Error;
            LogError($"Failed to shut down driver {DriverName}: {ex.Message}", ex);
            return DriverResult.Failed(ex, stopwatch.Elapsed);
        }
    }
    
    /// <summary>
    /// Get health information about the driver.
    /// </summary>
    public virtual async Task<DriverHealth> GetHealthAsync(CancellationToken cancellationToken = default)
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
            LogError($"Health check failed for driver {DriverName}: {ex.Message}", ex);
            return DriverHealth.Unhealthy(DriverId, DriverName, $"Health check failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get diagnostic information about the driver.
    /// </summary>
    public virtual async Task<DriverDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var uptime = StartedAt.HasValue ? (TimeSpan?)(DateTime.UtcNow - StartedAt.Value) : null;
            
            var diagnostics = new DriverDiagnostics
            {
                DriverId = DriverId,
                DriverName = DriverName,
                Version = Version,
                Type = Type,
                State = State,
                CreatedAt = CreatedAt,
                InitializedAt = InitializedAt,
                StartedAt = StartedAt,
                Uptime = uptime
            };
            
            // Allow derived classes to add specific diagnostics
            await OnPopulateDiagnosticsAsync(diagnostics, cancellationToken);
            
            return diagnostics;
        }
        catch (Exception ex)
        {
            LogError($"Failed to get diagnostics for driver {DriverName}: {ex.Message}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// Handle inter-driver communication requests.
    /// </summary>
    public virtual async Task<DriverMessage> HandleMessageAsync(DriverMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            LogDebug($"Received message of type '{message.MessageType}' from driver {message.SourceDriverId}");
            
            // Call template method for specific message handling
            var response = await OnHandleMessageAsync(message, cancellationToken);
            
            if (response != null)
            {
                LogDebug($"Sending response to driver {message.SourceDriverId}");
                return response;
            }
            
            LogDebug($"No response generated for message type '{message.MessageType}'");
            return new DriverMessage
            {
                MessageId = Guid.NewGuid(),
                SourceDriverId = DriverId,
                TargetDriverId = message.SourceDriverId,
                MessageType = "NoResponse",
                RequiresResponse = false,
                ResponseToMessageId = message.MessageId,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogError($"Failed to handle message of type '{message.MessageType}': {ex.Message}", ex);
            
            return new DriverMessage
            {
                MessageId = Guid.NewGuid(),
                SourceDriverId = DriverId,
                TargetDriverId = message.SourceDriverId,
                MessageType = "Error",
                Payload = ex.Message,
                RequiresResponse = false,
                ResponseToMessageId = message.MessageId,
                Timestamp = DateTime.UtcNow
            };
        }
    }
    
    /// <summary>
    /// Configure services that this driver provides.
    /// </summary>
    public virtual void ConfigureServices(IServiceCollection services)
    {
        // Default implementation does nothing
        // Derived classes can override to register services
    }
    
    /// <summary>
    /// Template method for driver-specific initialization.
    /// </summary>
    protected virtual Task OnInitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    /// Template method for driver-specific startup.
    /// </summary>
    protected virtual Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    /// Template method for driver-specific stop logic.
    /// </summary>
    protected virtual Task OnStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    /// Template method for driver-specific shutdown logic.
    /// </summary>
    protected virtual Task OnShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    /// <summary>
    /// Template method for driver-specific health checks.
    /// </summary>
    protected virtual Task<DriverHealth> OnGetHealthAsync(CancellationToken cancellationToken)
    {
        var status = State switch
        {
            DriverState.Running => DriverHealthStatus.Healthy,
            DriverState.Initialized or DriverState.Stopped => DriverHealthStatus.Warning,
            DriverState.Error => DriverHealthStatus.Unhealthy,
            _ => DriverHealthStatus.Unknown
        };
        
        var message = State switch
        {
            DriverState.Running => "Driver is running normally",
            DriverState.Initialized => "Driver is initialized but not started",
            DriverState.Stopped => "Driver is stopped",
            DriverState.Error => "Driver is in error state",
            _ => $"Driver is in {State} state"
        };
        
        var health = new DriverHealth
        {
            DriverId = DriverId,
            DriverName = DriverName,
            Status = status,
            Message = message
        };
        
        return Task.FromResult(health);
    }
    
    /// <summary>
    /// Template method for populating driver-specific diagnostics.
    /// </summary>
    protected virtual Task OnPopulateDiagnosticsAsync(DriverDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        diagnostics.Metadata["driver_type"] = Type.ToString();
        diagnostics.Metadata["created_at"] = CreatedAt;
        diagnostics.Performance["state"] = State.ToString();
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Template method for handling driver-specific messages.
    /// </summary>
    protected virtual Task<DriverMessage?> OnHandleMessageAsync(DriverMessage message, CancellationToken cancellationToken)
    {
        // Default implementation returns null (no response)
        return Task.FromResult<DriverMessage?>(null);
    }
    
    /// <summary>
    /// Called when the driver state changes.
    /// </summary>
    protected virtual void OnStateChanged(DriverState previousState, DriverState newState)
    {
        // Default implementation does nothing
        // Derived classes can override to react to state changes
    }
    
    /// <summary>
    /// Log an informational message.
    /// </summary>
    protected void LogInfo(string message)
    {
        Logger?.LogInformation("[{DriverName}] {Message}", DriverName, message);
    }
    
    /// <summary>
    /// Log a warning message.
    /// </summary>
    protected void LogWarning(string message)
    {
        Logger?.LogWarning("[{DriverName}] {Message}", DriverName, message);
    }
    
    /// <summary>
    /// Log an error message.
    /// </summary>
    protected void LogError(string message, Exception? exception = null)
    {
        Logger?.LogError(exception, "[{DriverName}] {Message}", DriverName, message);
    }
    
    /// <summary>
    /// Log a debug message.
    /// </summary>
    protected void LogDebug(string message)
    {
        Logger?.LogDebug("[{DriverName}] {Message}", DriverName, message);
    }
    
    /// <summary>
    /// Send a message to another driver.
    /// </summary>
    protected async Task<DriverMessage?> SendMessageToDriverAsync(Guid targetDriverId, string messageType, object? payload = null, bool requiresResponse = false, CancellationToken cancellationToken = default)
    {
        var message = new DriverMessage
        {
            MessageId = Guid.NewGuid(),
            SourceDriverId = DriverId,
            TargetDriverId = targetDriverId,
            MessageType = messageType,
            Payload = payload,
            RequiresResponse = requiresResponse,
            Timestamp = DateTime.UtcNow
        };
        
        if (requiresResponse)
        {
            return await Context.CommunicationBus.SendMessageAndWaitForResponseAsync(message, TimeSpan.FromSeconds(30), cancellationToken);
        }
        else
        {
            return await Context.CommunicationBus.SendMessageAsync(message, cancellationToken);
        }
    }
    
    /// <summary>
    /// Broadcast a message to all drivers of a specific type.
    /// </summary>
    protected async Task BroadcastMessageToDriverTypeAsync(DriverType targetType, string messageType, object? payload = null, CancellationToken cancellationToken = default)
    {
        var message = new DriverMessage
        {
            MessageId = Guid.NewGuid(),
            SourceDriverId = DriverId,
            TargetDriverId = Guid.Empty, // Broadcast message
            MessageType = messageType,
            Payload = payload,
            RequiresResponse = false,
            Timestamp = DateTime.UtcNow
        };
        
        await Context.CommunicationBus.BroadcastMessageAsync(targetType, message, cancellationToken);
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && State != DriverState.Disposed)
        {
            try
            {
                // Try to shutdown gracefully
                if (State != DriverState.Shutdown)
                {
                    ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                LogError($"Error during disposal of driver {DriverName}: {ex.Message}", ex);
            }
            finally
            {
                DriverCancellationTokenSource?.Dispose();
                State = DriverState.Disposed;
            }
        }
    }
}
