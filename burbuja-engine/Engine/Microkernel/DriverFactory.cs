using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using BurbujaEngine.Engine.Drivers;

namespace BurbujaEngine.Engine.Microkernel;

/// <summary>
/// Implementation of the driver factory for creating driver instances.
/// </summary>
public class DriverFactory : IDriverFactory, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DriverFactory> _logger;
    private readonly ConcurrentDictionary<string, Type> _registeredTypes = new();
    private bool _disposed = false;
    
    public DriverFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<DriverFactory>>();
        
        // Register built-in driver types
        RegisterBuiltInDriverTypes();
        
        _logger.LogInformation("Driver factory initialized with {TypeCount} registered types",
            _registeredTypes.Count);
    }
    
    /// <summary>
    /// Create a driver instance by type.
    /// </summary>
    public T CreateDriver<T>() where T : class, IEngineDriver
    {
        ThrowIfDisposed();
        
        try
        {
            var driver = _serviceProvider.GetRequiredService<T>();
            _logger.LogInformation("Created driver instance of type {DriverType}", typeof(T).Name);
            return driver;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create driver of type {DriverType}: {Error}",
                typeof(T).Name, ex.Message);
            throw;
        }
    }
    
    /// <summary>
    /// Create a driver instance by type name.
    /// </summary>
    public IEngineDriver? CreateDriver(string driverTypeName)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(driverTypeName))
            throw new ArgumentException("Driver type name cannot be null or empty", nameof(driverTypeName));
        
        try
        {
            if (_registeredTypes.TryGetValue(driverTypeName, out var driverType))
            {
                var driver = (IEngineDriver)_serviceProvider.GetRequiredService(driverType);
                _logger.LogInformation("Created driver instance of type {DriverType}", driverTypeName);
                return driver;
            }
            
            _logger.LogWarning("Driver type {DriverType} not found in registered types", driverTypeName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create driver of type {DriverType}: {Error}",
                driverTypeName, ex.Message);
            throw;
        }
    }
    
    /// <summary>
    /// Register a driver type with the factory.
    /// </summary>
    public void RegisterDriverType<T>() where T : class, IEngineDriver
    {
        ThrowIfDisposed();
        
        var driverType = typeof(T);
        var typeName = driverType.Name;
        
        if (_registeredTypes.TryAdd(typeName, driverType))
        {
            _logger.LogInformation("Registered driver type {DriverType}", typeName);
        }
        else
        {
            _logger.LogWarning("Driver type {DriverType} is already registered", typeName);
        }
    }
    
    /// <summary>
    /// Register a driver type by Type instance.
    /// </summary>
    public void RegisterDriverType(Type driverType)
    {
        ThrowIfDisposed();
        
        if (driverType == null)
            throw new ArgumentNullException(nameof(driverType));
        
        if (!typeof(IEngineDriver).IsAssignableFrom(driverType))
            throw new ArgumentException($"Type {driverType.Name} does not implement IEngineDriver", nameof(driverType));
        
        var typeName = driverType.Name;
        
        if (_registeredTypes.TryAdd(typeName, driverType))
        {
            _logger.LogInformation("Registered driver type {DriverType}", typeName);
        }
        else
        {
            _logger.LogWarning("Driver type {DriverType} is already registered", typeName);
        }
    }
    
    /// <summary>
    /// Get available driver types.
    /// </summary>
    public IEnumerable<Type> GetAvailableDriverTypes()
    {
        ThrowIfDisposed();
        
        return _registeredTypes.Values.ToList();
    }
    
    /// <summary>
    /// Get registered driver type names.
    /// </summary>
    public IEnumerable<string> GetRegisteredTypeNames()
    {
        ThrowIfDisposed();
        
        return _registeredTypes.Keys.ToList();
    }
    
    /// <summary>
    /// Check if a driver type is registered.
    /// </summary>
    public bool IsDriverTypeRegistered<T>() where T : class, IEngineDriver
    {
        ThrowIfDisposed();
        
        return _registeredTypes.ContainsKey(typeof(T).Name);
    }
    
    /// <summary>
    /// Check if a driver type is registered by name.
    /// </summary>
    public bool IsDriverTypeRegistered(string driverTypeName)
    {
        ThrowIfDisposed();
        
        return _registeredTypes.ContainsKey(driverTypeName);
    }
    
    /// <summary>
    /// Create multiple driver instances of different types.
    /// </summary>
    public async Task<List<IEngineDriver>> CreateDriversAsync(IEnumerable<string> driverTypeNames, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var drivers = new List<IEngineDriver>();
        
        foreach (var typeName in driverTypeNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                var driver = CreateDriver(typeName);
                if (driver != null)
                {
                    drivers.Add(driver);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create driver of type {DriverType}: {Error}",
                    typeName, ex.Message);
                
                // Decide whether to continue or fail - for now, continue with other drivers
                continue;
            }
        }
        
        _logger.LogInformation("Created {DriverCount} drivers out of {RequestedCount} requested",
            drivers.Count, driverTypeNames.Count());
        
        return drivers;
    }
    
    /// <summary>
    /// Register built-in driver types.
    /// </summary>
    private void RegisterBuiltInDriverTypes()
    {
        try
        {
            // Register the DatabaseDriver
            RegisterDriverType<DatabaseDriver>();
            
            _logger.LogDebug("Registered built-in driver types");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register built-in driver types: {Error}", ex.Message);
        }
    }
    
    /// <summary>
    /// Auto-discover and register driver types from assemblies.
    /// </summary>
    public void AutoRegisterDriverTypes(params System.Reflection.Assembly[] assemblies)
    {
        ThrowIfDisposed();
        
        var assembliesToScan = assemblies.Any() ? assemblies : new[] { System.Reflection.Assembly.GetExecutingAssembly() };
        
        foreach (var assembly in assembliesToScan)
        {
            try
            {
                var driverTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(IEngineDriver).IsAssignableFrom(t))
                    .ToList();
                
                foreach (var driverType in driverTypes)
                {
                    RegisterDriverType(driverType);
                }
                
                _logger.LogInformation("Auto-registered {TypeCount} driver types from assembly {AssemblyName}",
                    driverTypes.Count, assembly.GetName().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-register driver types from assembly {AssemblyName}: {Error}",
                    assembly.GetName().Name, ex.Message);
            }
        }
    }
    
    /// <summary>
    /// Get factory statistics.
    /// </summary>
    public DriverFactoryStatistics GetStatistics()
    {
        ThrowIfDisposed();
        
        return new DriverFactoryStatistics
        {
            RegisteredTypeCount = _registeredTypes.Count,
            RegisteredTypes = _registeredTypes.Keys.ToList()
        };
    }
    
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DriverFactory));
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing driver factory");
            _registeredTypes.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// Statistics about the driver factory.
/// </summary>
public class DriverFactoryStatistics
{
    public int RegisteredTypeCount { get; set; }
    public List<string> RegisteredTypes { get; set; } = new();
}
