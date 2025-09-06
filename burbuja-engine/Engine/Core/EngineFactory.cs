using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BurbujaEngine.Engine.Core;

/// <summary>
/// Factory for creating BurbujaEngine instances.
/// </summary>
public class EngineFactory : IEngineFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    
    public EngineFactory(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }
    
    /// <summary>
    /// Create a new engine instance.
    /// </summary>
    public IBurbujaEngine CreateEngine(Guid engineId, EngineConfiguration configuration)
    {
        if (engineId == Guid.Empty)
            throw new ArgumentException("Engine ID cannot be empty", nameof(engineId));
        
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));
        
        return new BurbujaEngine(engineId, configuration, _serviceProvider, _loggerFactory);
    }
}

/// <summary>
/// Builder for creating and configuring engines.
/// Implements a fluent interface for engine configuration.
/// </summary>
public class EngineBuilder
{
    private readonly Guid _engineId;
    private readonly IServiceCollection _services;
    private readonly List<Func<IBurbujaEngine, IBurbujaEngine>> _moduleRegistrations = new();
    private EngineConfiguration _configuration = EngineConfiguration.Default(Guid.NewGuid());
    
    public EngineBuilder(Guid engineId, IServiceCollection services)
    {
        _engineId = engineId;
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }
    
    /// <summary>
    /// Configure the engine with the specified configuration.
    /// </summary>
    public EngineBuilder WithConfiguration(EngineConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        return this;
    }
    
    /// <summary>
    /// Configure the engine using a configuration builder.
    /// </summary>
    public EngineBuilder WithConfiguration(Action<EngineConfigurationBuilder> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        
        var builder = new EngineConfigurationBuilder(_engineId);
        configure(builder);
        _configuration = builder.Build();
        return this;
    }
    
    /// <summary>
    /// Register a module with the engine.
    /// </summary>
    public EngineBuilder AddModule<T>() where T : class, IEngineModule
    {
        _services.AddSingleton<T>(); // Use Singleton for modules since they're part of the engine lifecycle
        _moduleRegistrations.Add(engine => engine.RegisterModule<T>());
        return this;
    }
    
    /// <summary>
    /// Register a module with the engine using a factory.
    /// </summary>
    public EngineBuilder AddModule<T>(Func<IServiceProvider, T> factory) where T : class, IEngineModule
    {
        _services.AddSingleton<T>(factory); // Use Singleton for modules
        _moduleRegistrations.Add(engine => engine.RegisterModule<T>());
        return this;
    }
    
    /// <summary>
    /// Register a module instance with the engine.
    /// </summary>
    public EngineBuilder AddModule(IEngineModule module)
    {
        if (module == null) throw new ArgumentNullException(nameof(module));
        
        _moduleRegistrations.Add(engine => engine.RegisterModule(module));
        return this;
    }
    
    /// <summary>
    /// Build the engine and register it with the service collection.
    /// </summary>
    public IServiceCollection Build()
    {
        // Register engine configuration
        _services.AddSingleton<IEngineConfiguration>(_configuration);
        
        // Register engine factory
        _services.AddSingleton<IEngineFactory, EngineFactory>();
        
        // Register engine as singleton
        _services.AddSingleton<IBurbujaEngine>(serviceProvider =>
        {
            var factory = serviceProvider.GetRequiredService<IEngineFactory>();
            var engine = factory.CreateEngine(_engineId, _configuration);
            
            // Apply module registrations
            foreach (var registration in _moduleRegistrations)
            {
                registration(engine);
            }
            
            return engine;
        });
        
        return _services;
    }
}

/// <summary>
/// Extension methods for easier engine setup.
/// </summary>
public static class EngineBuilderExtensions
{
    /// <summary>
    /// Add BurbujaEngine to the service collection.
    /// </summary>
    public static EngineBuilder AddBurbujaEngine(this IServiceCollection services, Guid? engineId = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        var id = engineId ?? Guid.NewGuid();
        
        return new EngineBuilder(id, services);
    }
    
    /// <summary>
    /// Configure and add BurbujaEngine to the service collection.
    /// </summary>
    public static IServiceCollection AddBurbujaEngine(
        this IServiceCollection services,
        Guid engineId,
        Action<EngineBuilder> configure)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        
        var builder = services.AddBurbujaEngine(engineId);
        configure(builder);
        return builder.Build();
    }
}
