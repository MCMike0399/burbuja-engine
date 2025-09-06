using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using BurbujaEngine.Engine.Drivers;

namespace BurbujaEngine.Engine.Microkernel;

/// <summary>
/// Implementation of the inter-driver communication bus.
/// 
/// MICROKERNEL PATTERN: Step 4 - Inter-Process Communication (IPC)
/// 
/// This class implements the critical microkernel IPC mechanism that enables:
/// - Message passing between user-space drivers and services
/// - Asynchronous communication with timeout support
/// - Broadcast messaging for system-wide notifications
/// - Handler registration for specific message types
/// 
/// IPC DESIGN PRINCIPLES:
/// - Minimal overhead: Direct message routing without excessive processing
/// - Fault tolerance: Isolated failures don't cascade across the system
/// - Performance optimization: Concurrent message handling and response caching
/// - Security: Message validation and controlled access between components
/// 
/// This represents a core microkernel service that must remain in kernel space
/// to provide reliable communication infrastructure for all user-space components.
/// </summary>
public class DriverCommunicationBus : IDriverCommunicationBus, IDisposable
{
    private readonly ILogger<DriverCommunicationBus> _logger;
    private readonly ConcurrentDictionary<Guid, IEngineDriver> _registeredDrivers = new();
    private readonly ConcurrentDictionary<(Guid DriverId, string MessageType), Func<DriverMessage, Task<DriverMessage?>>> _messageHandlers = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<DriverMessage>> _pendingResponses = new();
    private readonly object _lockObject = new();
    private bool _disposed = false;
    
    public event EventHandler<DriverMessage>? UnhandledMessage;
    
    public DriverCommunicationBus(ILogger<DriverCommunicationBus> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Driver communication bus initialized");
    }
    
    /// <summary>
    /// Send a message to a specific driver.
    /// 
    /// MICROKERNEL IPC: This method implements the core message-passing mechanism
    /// that allows user-space drivers to communicate through the microkernel.
    /// 
    /// IPC FEATURES:
    /// - Direct routing: Messages are routed directly to target drivers
    /// - Handler resolution: Supports both specific handlers and general message handling
    /// - Error handling: Returns error responses for failed message delivery
    /// - Logging: Comprehensive logging for debugging and monitoring
    /// </summary>
    public async Task<DriverMessage?> SendMessageAsync(DriverMessage message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        
        _logger.LogDebug("Sending message {MessageId} of type '{MessageType}' from {SourceDriverId} to {TargetDriverId}",
            message.MessageId, message.MessageType, message.SourceDriverId, message.TargetDriverId);
        
        try
        {
            // Check if target driver is registered
            if (!_registeredDrivers.TryGetValue(message.TargetDriverId, out var targetDriver))
            {
                _logger.LogWarning("Target driver {TargetDriverId} not found for message {MessageId}", 
                    message.TargetDriverId, message.MessageId);
                return null;
            }
            
            // Check if there's a specific handler for this message type
            var handlerKey = (message.TargetDriverId, message.MessageType);
            if (_messageHandlers.TryGetValue(handlerKey, out var handler))
            {
                _logger.LogDebug("Using registered handler for message type '{MessageType}' on driver {TargetDriverId}",
                    message.MessageType, message.TargetDriverId);
                
                var response = await handler(message);
                
                _logger.LogDebug("Handler completed for message {MessageId}, response: {HasResponse}",
                    message.MessageId, response is not null);
                
                return response;
            }
            
            // Fall back to driver's general message handler
            _logger.LogDebug("Using driver's HandleMessageAsync for message {MessageId}", message.MessageId);
            var driverResponse = await targetDriver.HandleMessageAsync(message, cancellationToken);
            
            return driverResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message {MessageId} of type '{MessageType}': {Error}",
                message.MessageId, message.MessageType, ex.Message);
            
            // Return error response
            return new DriverMessage
            {
                MessageId = Guid.NewGuid(),
                SourceDriverId = message.TargetDriverId,
                TargetDriverId = message.SourceDriverId,
                MessageType = "Error",
                Payload = new { error = ex.Message, originalMessageId = message.MessageId },
                RequiresResponse = false,
                ResponseToMessageId = message.MessageId,
                Timestamp = DateTime.UtcNow
            };
        }
    }
    
    /// <summary>
    /// Send a message and wait for response.
    /// </summary>
    public async Task<DriverMessage?> SendMessageAndWaitForResponseAsync(DriverMessage message, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        
        message = new DriverMessage
        {
            MessageId = message.MessageId,
            SourceDriverId = message.SourceDriverId,
            TargetDriverId = message.TargetDriverId,
            MessageType = message.MessageType,
            Payload = message.Payload,
            RequiresResponse = true,
            ResponseToMessageId = message.ResponseToMessageId,
            Timestamp = message.Timestamp,
            Headers = message.Headers
        };
        
        _logger.LogDebug("Sending message {MessageId} and waiting for response (timeout: {Timeout})",
            message.MessageId, timeout);
        
        // Create a task completion source for the response
        var responseSource = new TaskCompletionSource<DriverMessage>();
        _pendingResponses[message.MessageId] = responseSource;
        
        try
        {
            // Send the message
            var immediateResponse = await SendMessageAsync(message, cancellationToken);
            
            if (immediateResponse is not null)
            {
                _logger.LogDebug("Received immediate response for message {MessageId}", message.MessageId);
                return immediateResponse;
            }
            
            // Wait for response with timeout
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            try
            {
                combinedCts.Token.Register(() => responseSource.TrySetCanceled());
                var response = await responseSource.Task;
                
                _logger.LogDebug("Received response for message {MessageId}", message.MessageId);
                return response;
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Timeout waiting for response to message {MessageId} after {Timeout}",
                    message.MessageId, timeout);
                return null;
            }
        }
        finally
        {
            _pendingResponses.TryRemove(message.MessageId, out _);
        }
    }
    
    /// <summary>
    /// Broadcast a message to all drivers of a specific type.
    /// </summary>
    public async Task BroadcastMessageAsync(DriverType targetType, DriverMessage message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (message == null)
            throw new ArgumentNullException(nameof(message));
        
        _logger.LogDebug("Broadcasting message {MessageId} of type '{MessageType}' to all drivers of type {TargetType}",
            message.MessageId, message.MessageType, targetType);
        
        var targetDrivers = _registeredDrivers.Values
            .Where(d => d.Type == targetType)
            .ToList();
        
        if (!targetDrivers.Any())
        {
            _logger.LogWarning("No drivers of type {TargetType} found for broadcast message {MessageId}",
                targetType, message.MessageId);
            return;
        }
        
        _logger.LogDebug("Broadcasting to {DriverCount} drivers of type {TargetType}",
            targetDrivers.Count, targetType);
        
        var broadcastTasks = targetDrivers.Select(async driver =>
        {
            try
            {
                var driverMessage = new DriverMessage
                {
                    MessageId = message.MessageId,
                    SourceDriverId = message.SourceDriverId,
                    TargetDriverId = driver.DriverId,
                    MessageType = message.MessageType,
                    Payload = message.Payload,
                    RequiresResponse = message.RequiresResponse,
                    ResponseToMessageId = message.ResponseToMessageId,
                    Timestamp = message.Timestamp,
                    Headers = message.Headers
                };
                await SendMessageAsync(driverMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast message {MessageId} to driver {DriverId}: {Error}",
                    message.MessageId, driver.DriverId, ex.Message);
            }
        });
        
        await Task.WhenAll(broadcastTasks);
        
        _logger.LogDebug("Completed broadcasting message {MessageId} to {DriverCount} drivers",
            message.MessageId, targetDrivers.Count);
    }
    
    /// <summary>
    /// Register a driver for receiving messages.
    /// </summary>
    public Task RegisterDriverAsync(IEngineDriver driver)
    {
        ThrowIfDisposed();
        
        if (driver == null)
            throw new ArgumentNullException(nameof(driver));
        
        lock (_lockObject)
        {
            if (_registeredDrivers.TryAdd(driver.DriverId, driver))
            {
                _logger.LogInformation("Registered driver {DriverId} ({DriverName}) for communication",
                    driver.DriverId, driver.DriverName);
                
                // Subscribe to driver state changes
                driver.StateChanged += OnDriverStateChanged;
            }
            else
            {
                _logger.LogWarning("Driver {DriverId} is already registered for communication", driver.DriverId);
            }
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Unregister a driver from receiving messages.
    /// </summary>
    public Task UnregisterDriverAsync(Guid driverId)
    {
        ThrowIfDisposed();
        
        lock (_lockObject)
        {
            if (_registeredDrivers.TryRemove(driverId, out var driver))
            {
                _logger.LogInformation("Unregistered driver {DriverId} ({DriverName}) from communication",
                    driverId, driver.DriverName);
                
                // Unsubscribe from driver state changes
                driver.StateChanged -= OnDriverStateChanged;
                
                // Remove all message handlers for this driver
                var handlersToRemove = _messageHandlers.Keys
                    .Where(key => key.DriverId == driverId)
                    .ToList();
                
                foreach (var handlerKey in handlersToRemove)
                {
                    _messageHandlers.TryRemove(handlerKey, out _);
                }
                
                _logger.LogDebug("Removed {HandlerCount} message handlers for driver {DriverId}",
                    handlersToRemove.Count, driverId);
            }
            else
            {
                _logger.LogWarning("Driver {DriverId} was not registered for communication", driverId);
            }
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Subscribe to messages of a specific type.
    /// </summary>
    public Task SubscribeToMessageTypeAsync(Guid driverId, string messageType, Func<DriverMessage, Task<DriverMessage?>> handler)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(messageType))
            throw new ArgumentException("Message type cannot be null or empty", nameof(messageType));
        
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        
        var handlerKey = (driverId, messageType);
        
        lock (_lockObject)
        {
            if (_messageHandlers.TryAdd(handlerKey, handler))
            {
                _logger.LogInformation("Registered message handler for driver {DriverId}, message type '{MessageType}'",
                    driverId, messageType);
            }
            else
            {
                _logger.LogWarning("Message handler for driver {DriverId}, message type '{MessageType}' already exists",
                    driverId, messageType);
                
                // Update the existing handler
                _messageHandlers[handlerKey] = handler;
                _logger.LogInformation("Updated message handler for driver {DriverId}, message type '{MessageType}'",
                    driverId, messageType);
            }
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Handle driver state changes.
    /// </summary>
    private void OnDriverStateChanged(object? sender, DriverStateChangedEventArgs e)
    {
        _logger.LogDebug("Driver {DriverId} state changed: {PreviousState} -> {NewState}",
            e.DriverId, e.PreviousState, e.NewState);
        
        // If driver is shutting down or in error state, we might want to handle pending messages
        if (e.NewState == DriverState.ShuttingDown || e.NewState == DriverState.Error)
        {
            HandleDriverUnavailable(e.DriverId);
        }
    }
    
    /// <summary>
    /// Handle cases where a driver becomes unavailable.
    /// </summary>
    private void HandleDriverUnavailable(Guid driverId)
    {
        _logger.LogDebug("Handling unavailable driver {DriverId}", driverId);
        
        // Cancel any pending responses for this driver
        var pendingForDriver = _pendingResponses
            .Where(kvp => kvp.Value.Task.IsCompleted == false)
            .ToList();
        
        foreach (var pending in pendingForDriver)
        {
            try
            {
                pending.Value.TrySetResult(new DriverMessage
                {
                    MessageId = Guid.NewGuid(),
                    SourceDriverId = driverId,
                    TargetDriverId = Guid.Empty,
                    MessageType = "DriverUnavailable",
                    Payload = "Driver became unavailable",
                    RequiresResponse = false,
                    ResponseToMessageId = pending.Key,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle pending response for unavailable driver {DriverId}: {Error}",
                    driverId, ex.Message);
            }
        }
    }
    
    /// <summary>
    /// Get statistics about the communication bus.
    /// </summary>
    public CommunicationBusStatistics GetStatistics()
    {
        ThrowIfDisposed();
        
        return new CommunicationBusStatistics
        {
            RegisteredDriverCount = _registeredDrivers.Count,
            MessageHandlerCount = _messageHandlers.Count,
            PendingResponseCount = _pendingResponses.Count,
            DriverTypes = _registeredDrivers.Values.GroupBy(d => d.Type).ToDictionary(g => g.Key, g => g.Count())
        };
    }
    
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DriverCommunicationBus));
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing driver communication bus");
            
            // Cancel all pending responses
            foreach (var pending in _pendingResponses.Values)
            {
                try
                {
                    pending.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cancelling pending response during disposal: {Error}", ex.Message);
                }
            }
            
            _pendingResponses.Clear();
            _messageHandlers.Clear();
            
            // Unsubscribe from all driver events
            foreach (var driver in _registeredDrivers.Values)
            {
                try
                {
                    driver.StateChanged -= OnDriverStateChanged;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error unsubscribing from driver {DriverId} events: {Error}",
                        driver.DriverId, ex.Message);
                }
            }
            
            _registeredDrivers.Clear();
            
            _disposed = true;
            _logger.LogInformation("Driver communication bus disposed");
        }
    }
}

/// <summary>
/// Statistics about the communication bus.
/// </summary>
public class CommunicationBusStatistics
{
    public int RegisteredDriverCount { get; set; }
    public int MessageHandlerCount { get; set; }
    public int PendingResponseCount { get; set; }
    public Dictionary<DriverType, int> DriverTypes { get; set; } = new();
}
