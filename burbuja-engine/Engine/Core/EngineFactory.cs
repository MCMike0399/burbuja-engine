using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BurbujaEngine.Engine.Extensions;

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
/// MICROKERNEL PATTERN: Engine Builder
/// 
/// This builder implements the microkernel principle of minimal, clean APIs
/// where the engine handles complexity internally while providing developers
/// with simple, intuitive configuration methods.
/// 
/// MICROKERNEL BENEFITS:
/// - Clean Separation: Engine manages module lifecycle, developers focus on business logic
/// - Auto-Discovery: Modules are found automatically, no manual registration needed
/// - Configuration-Driven: Behavior controlled through configuration, not code
/// - Plug-and-Play: Modules can be added/removed without changing application code
/// </summary>
public class EngineBuilder
{
    private readonly Guid _engineId;
    private readonly IServiceCollection _services;
    private EngineConfiguration _configuration;
    private ModuleDiscoveryOptions _discoveryOptions;
    private readonly List<Type> _explicitModules = new();
    private readonly List<Type> _excludedModules = new();

    public EngineBuilder(Guid engineId, IServiceCollection services)
    {
        _engineId = engineId;
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _configuration = EngineConfiguration.Default(engineId);
        _discoveryOptions = new ModuleDiscoveryOptions();
    }

    /// <summary>
    /// Access to the underlying service collection for advanced configuration scenarios.
    /// This follows the ASP.NET Core pattern where builders expose their service collection.
    /// </summary>
    public IServiceCollection Services => _services;

    /// <summary>
    /// Configure the engine with minimal, essential settings.
    /// The microkernel handles all complex configuration internally.
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
    /// Configure module discovery options.
    /// Control how the microkernel finds and loads modules.
    /// </summary>
    public EngineBuilder WithModuleDiscovery(Action<ModuleDiscoveryOptions> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        configure(_discoveryOptions);
        return this;
    }

    /// <summary>
    /// Explicitly include a specific module type.
    /// Use this for modules that can't be auto-discovered or need explicit control.
    /// </summary>
    public EngineBuilder IncludeModule<TModule>() where TModule : class, IEngineModule
    {
        _explicitModules.Add(typeof(TModule));
        return this;
    }

    /// <summary>
    /// Exclude a specific module type from auto-discovery.
    /// Use this to disable modules that would otherwise be auto-discovered.
    /// </summary>
    public EngineBuilder ExcludeModule<TModule>() where TModule : class, IEngineModule
    {
        _excludedModules.Add(typeof(TModule));
        return this;
    }

    /// <summary>
    /// Enable development mode with enhanced discovery and debugging features.
    /// </summary>
    public EngineBuilder EnableDevelopmentMode()
    {
        _discoveryOptions.CurrentEnvironment = "Development";
        _discoveryOptions.EnableConventionBasedDiscovery = true;
        _discoveryOptions.EnableConfigurationBasedDiscovery = true;
        
        // Enhanced logging in development
        _configuration = _configuration with
        {
            ContinueOnModuleFailure = false, // Fail fast in development
            EnableParallelInitialization = false // Sequential for easier debugging
        };
        
        return this;
    }

    /// <summary>
    /// Enable production mode with optimized discovery and performance settings.
    /// </summary>
    public EngineBuilder EnableProductionMode()
    {
        _discoveryOptions.CurrentEnvironment = "Production";
        _discoveryOptions.EnableConventionBasedDiscovery = false; // More strict in production
        _discoveryOptions.EnableConfigurationBasedDiscovery = true;
        
        // Production optimizations
        _configuration = _configuration with
        {
            ContinueOnModuleFailure = true, // Continue on non-critical failures
            EnableParallelInitialization = true // Parallel for better performance
        };
        
        return this;
    }

    /// <summary>
    /// Build and configure the engine with auto-discovered modules.
    /// This is where the microkernel takes over and handles all complexity.
    /// </summary>
    public async Task<IServiceCollection> BuildAsync()
    {
        // Register core engine services
        RegisterCoreServices();

        // Discover modules automatically
        var discoveredModules = await DiscoverModulesAsync();

        // Register all modules
        RegisterModules(discoveredModules);

        return _services;
    }

    /// <summary>
    /// Build the engine synchronously (for backward compatibility).
    /// Note: Auto-discovery will be limited in synchronous mode.
    /// </summary>
    public IServiceCollection Build()
    {
        // Register core engine services
        RegisterCoreServices();

        // Register explicit modules only (no auto-discovery in sync mode)
        var explicitDescriptors = _explicitModules.Select(CreateExplicitModuleDescriptor);
        RegisterModules(explicitDescriptors);

        return _services;
    }

    private void RegisterCoreServices()
    {
        // Register engine configuration
        _services.AddSingleton(_configuration);

        // Register module discovery service
        _services.AddSingleton(_discoveryOptions);
        _services.AddSingleton<IModuleDiscovery, DefaultModuleDiscovery>();

        // Register core engine services
        _services.AddSingleton<IEngineFactory, EngineFactory>();
        _services.AddSingleton<IBurbujaEngine>(serviceProvider =>
        {
            var factory = serviceProvider.GetRequiredService<IEngineFactory>();
            return factory.CreateEngine(_engineId, _configuration);
        });
        _services.AddHostedService<EngineHostedService>();

        // Register logging if not already configured
        if (!_services.Any(s => s.ServiceType == typeof(ILoggerFactory)))
        {
            _services.AddLogging();
        }
    }

    private async Task<IEnumerable<ModuleDescriptor>> DiscoverModulesAsync()
    {
        using var serviceProvider = _services.BuildServiceProvider();
        var discovery = serviceProvider.GetRequiredService<IModuleDiscovery>();
        
        var discovered = await discovery.DiscoverModulesAsync();
        
        // Add explicit modules
        var explicitDescriptors = _explicitModules.Select(CreateExplicitModuleDescriptor);
        var allModules = discovered.Concat(explicitDescriptors);
        
        // Remove excluded modules
        var filteredModules = allModules.Where(m => !_excludedModules.Contains(m.ModuleType));
        
        return filteredModules.ToList();
    }

    private void RegisterModules(IEnumerable<ModuleDescriptor> modules)
    {
        foreach (var moduleDescriptor in modules)
        {
            try
            {
                // Register the module type
                _services.AddSingleton(moduleDescriptor.ModuleType);
                _services.AddSingleton(typeof(IEngineModule), provider => 
                    provider.GetRequiredService(moduleDescriptor.ModuleType));

                // Let the module configure its own services
                var moduleInstance = CreateModuleInstance(moduleDescriptor.ModuleType);
                if (moduleInstance != null)
                {
                    moduleInstance.ConfigureServices(_services);
                }

                // Register the module with the engine
                _services.Configure<EngineModuleRegistration>(options =>
                {
                    options.ModuleTypes.Add(moduleDescriptor.ModuleType);
                });
            }
            catch (Exception ex)
            {
                // In development, fail fast. In production, log and continue.
                if (_discoveryOptions.CurrentEnvironment == "Development")
                {
                    throw new InvalidOperationException(
                        $"Failed to register module {moduleDescriptor.ModuleName} ({moduleDescriptor.ModuleType.Name})", ex);
                }
                
                // Log the error and continue in production
                using var serviceProvider = _services.BuildServiceProvider();
                var logger = serviceProvider.GetService<ILogger<EngineBuilder>>();
                logger?.LogError(ex, "Failed to register module {ModuleName} ({ModuleType})", 
                    moduleDescriptor.ModuleName, moduleDescriptor.ModuleType.Name);
            }
        }
    }

    private ModuleDescriptor CreateExplicitModuleDescriptor(Type moduleType)
    {
        return new ModuleDescriptor
        {
            ModuleType = moduleType,
            ModuleName = moduleType.Name,
            Version = "1.0.0",
            Priority = ModulePriority.Simple(0),
            Tags = Array.Empty<string>(),
            IsEnabled = true,
            Dependencies = Array.Empty<Type>(),
            Metadata = new Dictionary<string, object>
            {
                ["source"] = "explicit"
            }.AsReadOnly()
        };
    }

    private IEngineModule? CreateModuleInstance(Type moduleType)
    {
        try
        {
            // Try to create a temporary instance to call ConfigureServices
            // This is a lightweight operation just for service registration
            var constructor = moduleType.GetConstructors()
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (constructor == null) return null;

            var parameters = constructor.GetParameters();
            var args = new object[parameters.Length];

            // For ConfigureServices, we don't need fully resolved dependencies
            // Just pass null/default values as this is only for service registration
            for (int i = 0; i < parameters.Length; i++)
            {
                args[i] = GetDefaultValue(parameters[i].ParameterType) ?? 
                         (parameters[i].ParameterType.IsValueType ? Activator.CreateInstance(parameters[i].ParameterType) : null)!;
            }

            return Activator.CreateInstance(moduleType, args) as IEngineModule;
        }
        catch
        {
            // If we can't create an instance, that's okay - the DI container will handle it later
            return null;
        }
    }

    private static object? GetDefaultValue(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

/// <summary>
/// Configuration for engine module registration.
/// </summary>
public class EngineModuleRegistration
{
    public List<Type> ModuleTypes { get; } = new();
}
