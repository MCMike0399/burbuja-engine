using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BurbujaEngine.Engine.Core;
using BurbujaEngine.Engine.Modules;

namespace BurbujaEngine.Engine.Extensions;

/// <summary>
/// Extension methods for integrating BurbujaEngine with ASP.NET Core.
/// Provides seamless integration following .NET conventions.
/// </summary>
public static class EngineServiceExtensions
{
    /// <summary>
    /// Add BurbujaEngine with common modules to the service collection.
    /// </summary>
    public static IServiceCollection AddBurbujaEngine(
        this IServiceCollection services,
        Guid? engineId = null,
        Action<EngineBuilder>? configure = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        
        var id = engineId ?? Guid.NewGuid();
        var builder = new EngineBuilder(id, services);
        
        // Add common modules by default
        builder.AddModule<DatabaseModule>();
        
        // Allow custom configuration
        configure?.Invoke(builder);
        
        // Register hosted service for engine lifecycle management
        services.AddHostedService<EngineHostedService>();
        
        return builder.Build();
    }
    
    /// <summary>
    /// Add BurbujaEngine with custom configuration.
    /// </summary>
    public static IServiceCollection AddBurbujaEngine(
        this IServiceCollection services,
        Action<EngineConfigurationBuilder> configureEngine,
        Action<EngineBuilder>? configureModules = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configureEngine == null) throw new ArgumentNullException(nameof(configureEngine));
        
        var builder = new EngineBuilder(Guid.NewGuid(), services)
            .WithConfiguration(configureEngine);
        
        // Add common modules
        builder.AddModule<DatabaseModule>();
        
        // Allow custom module configuration
        configureModules?.Invoke(builder);
        
        // Register hosted service
        services.AddHostedService<EngineHostedService>();
        
        return builder.Build();
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
                if (moduleResult.Value.Success)
                {
                    _logger.LogDebug("Module {ModuleId} started successfully", moduleResult.Key);
                }
                else
                {
                    _logger.LogWarning("Module {ModuleId} failed to start: {Message}", 
                        moduleResult.Key, moduleResult.Value.Message);
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
                if (moduleResult.Value.Success)
                {
                    _logger.LogDebug("Module {ModuleId} shut down successfully", moduleResult.Key);
                }
                else
                {
                    _logger.LogWarning("Module {ModuleId} shutdown failed: {Message}", 
                        moduleResult.Key, moduleResult.Value.Message);
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
