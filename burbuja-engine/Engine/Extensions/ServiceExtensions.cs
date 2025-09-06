using BurbujaEngine.Engine.Core;
using BurbujaEngine.Engine.Modules;
using BurbujaEngine.Database.Extensions;

namespace BurbujaEngine.Engine.Extensions;

/// <summary>
/// Extension methods for integrating BurbujaEngine with ASP.NET Core.
/// </summary>
public static class EngineServiceExtensions
{
    /// <summary>
    /// Add BurbujaEngine with automatic module discovery.
    /// This is the primary method developers should use - the microkernel handles everything else.
    /// </summary>
    public static EngineBuilder AddBurbujaEngine(
        this IServiceCollection services,
        Guid? engineId = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        
        var id = engineId ?? Guid.NewGuid();
        return new EngineBuilder(id, services);
    }

    /// <summary>
    /// Add BurbujaEngine with configuration and automatic module discovery.
    /// </summary>
    public static EngineBuilder AddBurbujaEngine(
        this IServiceCollection services,
        Action<EngineConfigurationBuilder> configure,
        Guid? engineId = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        
        var id = engineId ?? Guid.NewGuid();
        return new EngineBuilder(id, services).WithConfiguration(configure);
    }
    
    /// <summary>
    /// Add a specific engine module to the engine builder.
    /// This follows the explicit registration pattern used throughout ASP.NET Core.
    /// </summary>
    public static EngineBuilder AddEngineModule<TModule>(this EngineBuilder builder)
        where TModule : class, IEngineModule
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        
        return builder.IncludeModule<TModule>();
    }
    
    /// <summary>
    /// Add a specific engine module instance to the engine builder.
    /// Use this when you need to configure the module before registration.
    /// </summary>
    public static EngineBuilder AddEngineModule<TModule>(this EngineBuilder builder, TModule module)
        where TModule : class, IEngineModule
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (module == null) throw new ArgumentNullException(nameof(module));
        
        // Register the specific instance
        builder.Services.AddSingleton<TModule>(module);
        builder.Services.AddSingleton<IEngineModule>(module);
        return builder.IncludeModule<TModule>();
    }
    
    /// <summary>
    /// Build and register the engine with all configured modules.
    /// Call this after configuring all modules and engine settings.
    /// </summary>
    public static IServiceCollection BuildEngine(this EngineBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        
        return builder.Build();
    }

    /// <summary>
    /// Build and register the engine with all configured modules (async version).
    /// Call this after configuring all modules and engine settings.
    /// </summary>
    public static async Task<IServiceCollection> BuildEngineAsync(this EngineBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        
        return await builder.BuildAsync();
    }
}

/// <summary>
/// Extension methods for adding specific engine modules with standardized service registration.
/// 
/// MICROKERNEL PATTERN: Step 3 - Modularize Services (Standardized Implementation)
/// 
/// This standardized pattern ensures that:
/// 1. Each module manages its own service registration through ConfigureServices()
/// 2. The microkernel handles all lifecycle management consistently
/// 3. Developers get a predictable, simple registration pattern
/// 4. Service dependencies are resolved before module instantiation
/// 
/// DEVELOPER BENEFITS:
/// - Consistent Pattern: Same approach for all modules
/// - Automatic Service Registration: Modules declare their own dependencies
/// - Clean Separation: Service registration separate from business logic
/// - Microkernel Management: Engine handles all infrastructure concerns
/// </summary>
public static class EngineModuleExtensions
{
    /// <summary>
    /// Add the Database module to the engine using the standardized pattern.
    /// 
    /// The DatabaseModule will automatically register its required services
    /// through its ConfigureServices() method, ensuring proper dependency resolution.
    /// </summary>
    public static EngineBuilder AddDatabaseModule(this EngineBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        
        // The standardized pattern: let the module register its own services
        return builder.AddEngineModule<DatabaseModule>();
    }
    
    /// <summary>
    /// Add the Monitor module to the engine using the standardized pattern.
    /// 
    /// The MonitorModule will automatically register its required services
    /// through its ConfigureServices() method, ensuring proper dependency resolution.
    /// </summary>
    public static EngineBuilder AddMonitorModule(this EngineBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        
        // The standardized pattern: let the module register its own services
        return builder.AddEngineModule<MonitorModule>();
    }
    
    /// <summary>
    /// Add a custom module using the standardized pattern.
    /// 
    /// The module will automatically register its required services through
    /// its ConfigureServices() method, following the microkernel principle
    /// of modular service registration.
    /// </summary>
    public static EngineBuilder AddModule<TModule>(this EngineBuilder builder)
        where TModule : class, IEngineModule
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        
        // The standardized pattern: let the module register its own services
        return builder.AddEngineModule<TModule>();
    }
    
    /// <summary>
    /// Add a module with additional service configuration.
    /// 
    /// This allows for advanced scenarios where additional services need to be
    /// registered beyond what the module provides through ConfigureServices().
    /// </summary>
    public static EngineBuilder AddModule<TModule>(this EngineBuilder builder, Action<IServiceCollection> additionalServices)
        where TModule : class, IEngineModule
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (additionalServices == null) throw new ArgumentNullException(nameof(additionalServices));
        
        // Apply additional service configuration first
        additionalServices(builder.Services);
        
        // Then add the module using the standardized pattern
        return builder.AddEngineModule<TModule>();
    }
    
    /// <summary>
    /// Add a module conditionally based on environment or configuration.
    /// 
    /// This enables environment-specific module loading while maintaining
    /// the standardized service registration pattern.
    /// </summary>
    public static EngineBuilder AddModuleWhen<TModule>(this EngineBuilder builder, Func<bool> condition)
        where TModule : class, IEngineModule
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        
        if (condition())
        {
            return builder.AddEngineModule<TModule>();
        }
        
        return builder;
    }
}

/// <summary>
/// Hosted service for managing BurbujaEngine lifecycle.
/// Integrates engine startup/shutdown with ASP.NET Core application lifecycle.
/// </summary>
public class EngineHostedService : IHostedService
{
    private readonly IBurbujaEngine _engine;
    private readonly ILogger<EngineHostedService> _logger;
    
    public EngineHostedService(IBurbujaEngine engine, ILogger<EngineHostedService> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    /// Start the engine when the application starts.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting BurbujaEngine hosted service...");
        
        try
        {
            // Initialize the engine
            var initResult = await _engine.InitializeAsync(cancellationToken);
            if (!initResult.Success)
            {
                _logger.LogError("Failed to initialize BurbujaEngine: {Message}", initResult.Message);
                throw new InvalidOperationException($"Engine initialization failed: {initResult.Message}", initResult.Exception);
            }
            
            _logger.LogInformation("BurbujaEngine initialized successfully with {ModuleCount} modules", 
                initResult.ModuleResults.Count);
            
            // Start the engine
            var startResult = await _engine.StartAsync(cancellationToken);
            if (!startResult.Success)
            {
                _logger.LogError("Failed to start BurbujaEngine: {Message}", startResult.Message);
                throw new InvalidOperationException($"Engine start failed: {startResult.Message}", startResult.Exception);
            }
            
            _logger.LogInformation("BurbujaEngine started successfully with {ModuleCount} modules running", 
                startResult.ModuleResults.Count);
            
            // Log module status
            foreach (var moduleResult in startResult.ModuleResults)
            {
                var module = _engine.GetModule(moduleResult.Key);
                var moduleIdentifier = module?.FriendlyId ?? moduleResult.Key.ToString();
                
                if (moduleResult.Value.Success)
                {
                    _logger.LogDebug("Module {ModuleIdentifier} started successfully", moduleIdentifier);
                }
                else
                {
                    _logger.LogWarning("Module {ModuleIdentifier} failed to start: {Message}", 
                        moduleIdentifier, moduleResult.Value.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start BurbujaEngine hosted service");
            throw;
        }
    }
    
    /// <summary>
    /// Stop the engine when the application shuts down.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping BurbujaEngine hosted service...");
        
        try
        {
            // Shutdown the engine gracefully
            var shutdownResult = await _engine.ShutdownAsync(cancellationToken);
            if (!shutdownResult.Success)
            {
                _logger.LogWarning("BurbujaEngine shutdown completed with issues: {Message}", shutdownResult.Message);
            }
            else
            {
                _logger.LogInformation("BurbujaEngine shut down successfully");
            }
            
            // Log module shutdown status
            foreach (var moduleResult in shutdownResult.ModuleResults)
            {
                var module = _engine.GetModule(moduleResult.Key);
                var moduleIdentifier = module?.FriendlyId ?? moduleResult.Key.ToString();
                
                if (moduleResult.Value.Success)
                {
                    _logger.LogDebug("Module {ModuleIdentifier} shut down successfully", moduleIdentifier);
                }
                else
                {
                    _logger.LogWarning("Module {ModuleIdentifier} shutdown failed: {Message}", 
                        moduleIdentifier, moduleResult.Value.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during BurbujaEngine shutdown");
            // Don't rethrow on shutdown - log and continue
        }
    }
}

/// <summary>
/// Extension methods for IHost to provide engine access.
/// </summary>
public static class HostExtensions
{
    /// <summary>
    /// Get the BurbujaEngine instance from the host.
    /// </summary>
    public static IBurbujaEngine GetBurbujaEngine(this IHost host)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        
        return host.Services.GetRequiredService<IBurbujaEngine>();
    }
    
    /// <summary>
    /// Get engine health information.
    /// </summary>
    public static async Task<EngineHealth> GetEngineHealthAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        var engine = host.GetBurbujaEngine();
        return await engine.GetHealthAsync(cancellationToken);
    }
    
    /// <summary>
    /// Get engine diagnostics information.
    /// </summary>
    public static async Task<EngineDiagnostics> GetEngineDiagnosticsAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        var engine = host.GetBurbujaEngine();
        return await engine.GetDiagnosticsAsync(cancellationToken);
    }
}

/// <summary>
/// Extension methods for WebApplication to add engine endpoints.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Map engine health and diagnostics endpoints.
    /// </summary>
    public static WebApplication MapEngineEndpoints(this WebApplication app, string basePath = "/engine")
    {
        if (app == null) throw new ArgumentNullException(nameof(app));
        
        // Health endpoint
        app.MapGet($"{basePath}/health", async (IBurbujaEngine engine, CancellationToken cancellationToken) =>
        {
            try
            {
                var health = await engine.GetHealthAsync(cancellationToken);
                return Results.Ok(new
                {
                    status = health.Status.ToString().ToLowerInvariant(),
                    message = health.Message,
                    checked_at = health.CheckedAt,
                    response_time_ms = health.ResponseTime.TotalMilliseconds,
                    modules = health.ModuleHealth.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new
                        {
                            status = kvp.Value.Status.ToString().ToLowerInvariant(),
                            message = kvp.Value.Message,
                            issues = kvp.Value.Issues,
                            warnings = kvp.Value.Warnings
                        })
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Engine Health Check Failed",
                    detail: ex.Message,
                    statusCode: 500);
            }
        })
        .WithName("EngineHealth");
        
        // Diagnostics endpoint
        app.MapGet($"{basePath}/diagnostics", async (IBurbujaEngine engine, CancellationToken cancellationToken) =>
        {
            try
            {
                var diagnostics = await engine.GetDiagnosticsAsync(cancellationToken);
                return Results.Ok(new
                {
                    engine_id = diagnostics.EngineId,
                    version = diagnostics.Version,
                    state = diagnostics.State.ToString().ToLowerInvariant(),
                    created_at = diagnostics.CreatedAt,
                    initialized_at = diagnostics.InitializedAt,
                    started_at = diagnostics.StartedAt,
                    uptime_ms = diagnostics.Uptime?.TotalMilliseconds,
                    module_count = diagnostics.ModuleCount,
                    modules = diagnostics.ModuleDiagnostics.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new
                        {
                            module_id = kvp.Value.ModuleId,
                            friendly_id = engine.GetModule(kvp.Value.ModuleId)?.FriendlyId ?? kvp.Value.ModuleId.ToString(),
                            module_name = kvp.Value.ModuleName,
                            version = kvp.Value.Version,
                            state = kvp.Value.State.ToString().ToLowerInvariant(),
                            created_at = kvp.Value.CreatedAt,
                            initialized_at = kvp.Value.InitializedAt,
                            started_at = kvp.Value.StartedAt,
                            uptime_ms = kvp.Value.Uptime?.TotalMilliseconds,
                            dependencies = kvp.Value.Dependencies
                        }),
                    environment = diagnostics.Environment,
                    process = diagnostics.Process != null ? new
                    {
                        id = diagnostics.Process.Id,
                        process_name = diagnostics.Process.ProcessName,
                        start_time = diagnostics.Process.StartTime,
                        working_set_mb = diagnostics.Process.WorkingSet64 / 1024 / 1024,
                        private_memory_mb = diagnostics.Process.PrivateMemorySize64 / 1024 / 1024
                    } : null
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Engine Diagnostics Failed",
                    detail: ex.Message,
                    statusCode: 500);
            }
        })
        .WithName("EngineDiagnostics");
        
        // Status endpoint (simple)
        app.MapGet($"{basePath}/status", (IBurbujaEngine engine) =>
        {
            return Results.Ok(new
            {
                engine_id = engine.EngineId,
                version = engine.Version,
                state = engine.State.ToString().ToLowerInvariant(),
                module_count = engine.Modules.Count,
                modules = engine.Modules.ToDictionary(
                    m => m.ModuleId,
                    m => new
                    {
                        name = m.ModuleName,
                        version = m.Version,
                        state = m.State.ToString().ToLowerInvariant(),
                        priority = m.Priority
                    })
            });
        })
        .WithName("EngineStatus");
        
        return app;
    }
}
