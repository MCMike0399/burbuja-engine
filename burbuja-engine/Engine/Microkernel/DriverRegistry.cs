using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using BurbujaEngine.Engine.Drivers;

namespace BurbujaEngine.Engine.Microkernel;

/// <summary>
/// Implementation of the driver registry for managing driver instances.
/// </summary>
public class DriverRegistry : IDriverRegistry, IDisposable
{
    private readonly ILogger<DriverRegistry> _logger;
    private readonly ConcurrentDictionary<Guid, IEngineDriver> _drivers = new();
    private readonly ConcurrentDictionary<Type, List<IEngineDriver>> _driversByType = new();
    private readonly object _lockObject = new();
    private bool _disposed = false;
    
    public event EventHandler<IEngineDriver>? DriverRegistered;
    public event EventHandler<Guid>? DriverUnregistered;
    
    public DriverRegistry(ILogger<DriverRegistry> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Driver registry initialized");
    }
    
    /// <summary>
    /// Register a driver instance.
    /// </summary>
    public Task RegisterDriverAsync(IEngineDriver driver)
    {
        ThrowIfDisposed();
        
        if (driver == null)
            throw new ArgumentNullException(nameof(driver));
        
        lock (_lockObject)
        {
            if (_drivers.TryAdd(driver.DriverId, driver))
            {
                // Add to type-based index
                var driverType = driver.GetType();
                if (!_driversByType.ContainsKey(driverType))
                {
                    _driversByType[driverType] = new List<IEngineDriver>();
                }
                _driversByType[driverType].Add(driver);
                
                _logger.LogInformation("Registered driver {DriverId} ({DriverName}) of type {DriverType}",
                    driver.DriverId, driver.DriverName, driverType.Name);
                
                // Subscribe to driver state changes for monitoring
                driver.StateChanged += OnDriverStateChanged;
                
                // Fire event
                DriverRegistered?.Invoke(this, driver);
            }
            else
            {
                _logger.LogWarning("Driver {DriverId} is already registered", driver.DriverId);
                throw new InvalidOperationException($"Driver with ID {driver.DriverId} is already registered");
            }
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Unregister a driver instance.
    /// </summary>
    public Task UnregisterDriverAsync(Guid driverId)
    {
        ThrowIfDisposed();
        
        lock (_lockObject)
        {
            if (_drivers.TryRemove(driverId, out var driver))
            {
                // Remove from type-based index
                var driverType = driver.GetType();
                if (_driversByType.TryGetValue(driverType, out var typeList))
                {
                    typeList.Remove(driver);
                    if (typeList.Count == 0)
                    {
                        _driversByType.TryRemove(driverType, out _);
                    }
                }
                
                _logger.LogInformation("Unregistered driver {DriverId} ({DriverName}) of type {DriverType}",
                    driverId, driver.DriverName, driverType.Name);
                
                // Unsubscribe from driver state changes
                driver.StateChanged -= OnDriverStateChanged;
                
                // Fire event
                DriverUnregistered?.Invoke(this, driverId);
            }
            else
            {
                _logger.LogWarning("Driver {DriverId} was not found for unregistration", driverId);
                throw new InvalidOperationException($"Driver with ID {driverId} is not registered");
            }
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Get a driver by its ID.
    /// </summary>
    public IEngineDriver? GetDriver(Guid driverId)
    {
        ThrowIfDisposed();
        
        _drivers.TryGetValue(driverId, out var driver);
        return driver;
    }
    
    /// <summary>
    /// Get a driver by type.
    /// </summary>
    public T? GetDriver<T>() where T : class, IEngineDriver
    {
        ThrowIfDisposed();
        
        var driverType = typeof(T);
        
        lock (_lockObject)
        {
            if (_driversByType.TryGetValue(driverType, out var typeList))
            {
                return typeList.FirstOrDefault() as T;
            }
        }
        
        // Fallback: search through all drivers
        return _drivers.Values.OfType<T>().FirstOrDefault();
    }
    
    /// <summary>
    /// Get all drivers of a specific type.
    /// </summary>
    public IEnumerable<IEngineDriver> GetDriversByType(DriverType type)
    {
        ThrowIfDisposed();
        
        return _drivers.Values.Where(d => d.Type == type).ToList();
    }
    
    /// <summary>
    /// Get all registered drivers.
    /// </summary>
    public IReadOnlyList<IEngineDriver> GetAllDrivers()
    {
        ThrowIfDisposed();
        
        return _drivers.Values.ToList();
    }
    
    /// <summary>
    /// Check if a driver is registered.
    /// </summary>
    public bool IsDriverRegistered(Guid driverId)
    {
        ThrowIfDisposed();
        
        return _drivers.ContainsKey(driverId);
    }
    
    /// <summary>
    /// Get drivers by their current state.
    /// </summary>
    public IEnumerable<IEngineDriver> GetDriversByState(DriverState state)
    {
        ThrowIfDisposed();
        
        return _drivers.Values.Where(d => d.State == state).ToList();
    }
    
    /// <summary>
    /// Get registry statistics.
    /// </summary>
    public DriverRegistryStatistics GetStatistics()
    {
        ThrowIfDisposed();
        
        var allDrivers = _drivers.Values.ToList();
        
        return new DriverRegistryStatistics
        {
            TotalDrivers = allDrivers.Count,
            DriversByType = allDrivers.GroupBy(d => d.Type).ToDictionary(g => g.Key, g => g.Count()),
            DriversByState = allDrivers.GroupBy(d => d.State).ToDictionary(g => g.Key, g => g.Count()),
            RegisteredTypes = _driversByType.Keys.Select(t => t.Name).ToList()
        };
    }
    
    /// <summary>
    /// Find drivers by name pattern.
    /// </summary>
    public IEnumerable<IEngineDriver> FindDriversByName(string namePattern)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(namePattern))
            return new List<IEngineDriver>();
        
        var pattern = namePattern.ToLowerInvariant();
        return _drivers.Values.Where(d => d.DriverName.ToLowerInvariant().Contains(pattern)).ToList();
    }
    
    /// <summary>
    /// Handle driver state changes for monitoring.
    /// </summary>
    private void OnDriverStateChanged(object? sender, DriverStateChangedEventArgs e)
    {
        _logger.LogDebug("Driver {DriverId} ({DriverName}) state changed: {PreviousState} -> {NewState}",
            e.DriverId, e.DriverName, e.PreviousState, e.NewState);
        
        // Log critical state changes
        if (e.NewState == DriverState.Error)
        {
            _logger.LogError("Driver {DriverId} ({DriverName}) entered error state",
                e.DriverId, e.DriverName);
        }
        else if (e.NewState == DriverState.Running && e.PreviousState != DriverState.Running)
        {
            _logger.LogInformation("Driver {DriverId} ({DriverName}) is now running",
                e.DriverId, e.DriverName);
        }
    }
    
    /// <summary>
    /// Validate that all registered drivers are in a healthy state.
    /// </summary>
    public async Task<DriverRegistryHealthReport> ValidateDriverHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var allDrivers = _drivers.Values.ToList();
        var healthTasks = allDrivers.Select(async driver =>
        {
            try
            {
                var health = await driver.GetHealthAsync(cancellationToken);
                return (driver.DriverId, health, (Exception?)null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get health for driver {DriverId}: {Error}",
                    driver.DriverId, ex.Message);
                return (driver.DriverId, (DriverHealth?)null, ex);
            }
        });
        
        var healthResults = await Task.WhenAll(healthTasks);
        
        var healthyDrivers = new List<Guid>();
        var unhealthyDrivers = new List<Guid>();
        var errorDrivers = new List<(Guid DriverId, Exception Exception)>();
        
        foreach (var (driverId, health, exception) in healthResults)
        {
            if (exception != null)
            {
                errorDrivers.Add((driverId, exception));
            }
            else if (health != null)
            {
                if (health.Status == DriverHealthStatus.Healthy)
                {
                    healthyDrivers.Add(driverId);
                }
                else
                {
                    unhealthyDrivers.Add(driverId);
                }
            }
        }
        
        return new DriverRegistryHealthReport
        {
            TotalDrivers = allDrivers.Count,
            HealthyDrivers = healthyDrivers,
            UnhealthyDrivers = unhealthyDrivers,
            ErrorDrivers = errorDrivers,
            Timestamp = DateTime.UtcNow
        };
    }
    
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DriverRegistry));
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing driver registry");
            
            // Unsubscribe from all driver events
            foreach (var driver in _drivers.Values)
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
            
            _drivers.Clear();
            _driversByType.Clear();
            
            _disposed = true;
            _logger.LogInformation("Driver registry disposed");
        }
    }
}

/// <summary>
/// Statistics about the driver registry.
/// </summary>
public class DriverRegistryStatistics
{
    public int TotalDrivers { get; set; }
    public Dictionary<DriverType, int> DriversByType { get; set; } = new();
    public Dictionary<DriverState, int> DriversByState { get; set; } = new();
    public List<string> RegisteredTypes { get; set; } = new();
}

/// <summary>
/// Health report for all drivers in the registry.
/// </summary>
public class DriverRegistryHealthReport
{
    public int TotalDrivers { get; set; }
    public List<Guid> HealthyDrivers { get; set; } = new();
    public List<Guid> UnhealthyDrivers { get; set; } = new();
    public List<(Guid DriverId, Exception Exception)> ErrorDrivers { get; set; } = new();
    public DateTime Timestamp { get; set; }
    
    public bool IsOverallHealthy => UnhealthyDrivers.Count == 0 && ErrorDrivers.Count == 0;
    public double HealthPercentage => TotalDrivers > 0 ? (double)HealthyDrivers.Count / TotalDrivers * 100 : 0;
}
