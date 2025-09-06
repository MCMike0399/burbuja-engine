using Microsoft.Extensions.DependencyInjection;
using BurbujaEngine.Engine.Extensions;

namespace BurbujaEngine.Engine.Core;

/// <summary>
/// MICROKERNEL PATTERN: Preset Configurations
/// 
/// Provides ready-to-use engine configurations for common scenarios,
/// embodying the microkernel principle of making complex systems simple to use.
/// 
/// PRESET BENEFITS:
/// - Zero Configuration: Works out-of-the-box for common scenarios
/// - Best Practices: Incorporates recommended settings and patterns
/// - Environment Aware: Automatically adjusts for development vs production
/// - Extensible: Can be customized further if needed
/// </summary>
public static class EnginePresets
{
    /// <summary>
    /// Create a complete engine with all standard modules for web applications.
    /// Includes database, monitoring, and all common infrastructure modules.
    /// </summary>
    public static async Task<IServiceCollection> AddCompleteWebEngineAsync(
        this IServiceCollection services)
    {
        return await services.AddBurbujaEngine()
            .EnableProductionMode()
            .WithConfiguration(config =>
            {
                config.WithVersion("1.0.0")
                      .WithModuleTimeout(TimeSpan.FromMinutes(5))
                      .WithShutdownTimeout(TimeSpan.FromMinutes(2))
                      .ContinueOnModuleFailure(true)
                      .EnableParallelInitialization(true);
            })
            .BuildAsync();
    }

    /// <summary>
    /// Create a minimal engine for microservices with essential modules only.
    /// Optimized for lightweight, fast-starting applications.
    /// </summary>
    public static async Task<IServiceCollection> AddMicroserviceEngineAsync(
        this IServiceCollection services)
    {
        return await services.AddBurbujaEngine()
            .EnableProductionMode()
            .WithConfiguration(config =>
            {
                config.WithVersion("1.0.0")
                      .WithModuleTimeout(TimeSpan.FromSeconds(30))
                      .WithShutdownTimeout(TimeSpan.FromSeconds(10))
                      .ContinueOnModuleFailure(true)
                      .EnableParallelInitialization(true);
            })
            .WithModuleDiscovery(discovery =>
            {
                // Only essential modules for microservices
                discovery.AssemblyFilter = name => 
                    name.Name?.Contains("BurbujaEngine.Core", StringComparison.OrdinalIgnoreCase) == true ||
                    name.Name?.Contains("BurbujaEngine.Database", StringComparison.OrdinalIgnoreCase) == true;
            })
            .BuildAsync();
    }

    /// <summary>
    /// Create a development-optimized engine with debugging and development tools.
    /// Includes all modules and enhanced error reporting.
    /// </summary>
    public static async Task<IServiceCollection> AddDevelopmentEngineAsync(
        this IServiceCollection services)
    {
        return await services.AddBurbujaEngine()
            .EnableDevelopmentMode()
            .WithConfiguration(config =>
            {
                config.WithVersion("1.0.0-dev")
                      .WithModuleTimeout(TimeSpan.FromMinutes(10)) // Longer timeouts for debugging
                      .WithShutdownTimeout(TimeSpan.FromMinutes(5))
                      .ContinueOnModuleFailure(false) // Fail fast in development
                      .EnableParallelInitialization(false); // Sequential for easier debugging
            })
            .BuildAsync();
    }

    /// <summary>
    /// Create a testing engine with deterministic behavior and test-friendly settings.
    /// </summary>
    public static async Task<IServiceCollection> AddTestingEngineAsync(
        this IServiceCollection services)
    {
        return await services.AddBurbujaEngine()
            .WithConfiguration(config =>
            {
                config.WithVersion("1.0.0-test")
                      .WithModuleTimeout(TimeSpan.FromSeconds(30))
                      .WithShutdownTimeout(TimeSpan.FromSeconds(5))
                      .ContinueOnModuleFailure(false) // Fail fast in tests
                      .EnableParallelInitialization(false); // Deterministic order
            })
            .WithModuleDiscovery(discovery =>
            {
                discovery.CurrentEnvironment = "Testing";
                discovery.EnableConventionBasedDiscovery = true;
                
                // Include test modules
                discovery.AssemblyFilter = name => 
                    name.Name?.Contains("BurbujaEngine", StringComparison.OrdinalIgnoreCase) == true ||
                    name.Name?.Contains("Test", StringComparison.OrdinalIgnoreCase) == true;
            })
            .BuildAsync();
    }

    /// <summary>
    /// Create a custom engine with specific modules only.
    /// For specialized use cases where you need precise control.
    /// </summary>
    public static EngineBuilder AddCustomEngine(
        this IServiceCollection services,
        params Type[] moduleTypes)
    {
        var builder = services.AddBurbujaEngine()
            .WithModuleDiscovery(discovery =>
            {
                // Disable auto-discovery for custom engines
                discovery.EnableConventionBasedDiscovery = false;
                discovery.EnableConfigurationBasedDiscovery = false;
            });

        // Add specific modules
        foreach (var moduleType in moduleTypes)
        {
            if (typeof(IEngineModule).IsAssignableFrom(moduleType))
            {
                builder = builder.IncludeModuleByType(moduleType);
            }
        }

        return builder;
    }

    /// <summary>
    /// Helper method to include a module type in a custom engine.
    /// </summary>
    private static EngineBuilder IncludeModuleByType(this EngineBuilder builder, Type moduleType)
    {
        // Use reflection to call the generic IncludeModule method
        var method = typeof(EngineBuilder)
            .GetMethod(nameof(EngineBuilder.IncludeModule))!
            .MakeGenericMethod(moduleType);
        
        return (EngineBuilder)method.Invoke(builder, null)!;
    }
}

/// <summary>
/// Extension methods for environment-specific engine configurations.
/// </summary>
public static class EnvironmentEngineExtensions
{
    /// <summary>
    /// Add engine with automatic environment detection.
    /// </summary>
    public static async Task<IServiceCollection> AddBurbujaEngineForEnvironmentAsync(
        this IServiceCollection services,
        string environment)
    {
        return environment.ToLowerInvariant() switch
        {
            "development" => await services.AddDevelopmentEngineAsync(),
            "testing" or "test" => await services.AddTestingEngineAsync(),
            "production" => await services.AddCompleteWebEngineAsync(),
            "microservice" => await services.AddMicroserviceEngineAsync(),
            _ => await services.AddCompleteWebEngineAsync()
        };
    }

    /// <summary>
    /// Add engine with Microsoft's hosting environment.
    /// </summary>
    public static async Task<IServiceCollection> AddBurbujaEngineForHostEnvironmentAsync(
        this IServiceCollection services,
        Microsoft.Extensions.Hosting.IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return await services.AddDevelopmentEngineAsync();
        }
        else if (environment.IsEnvironment("Testing"))
        {
            return await services.AddTestingEngineAsync();
        }
        else
        {
            return await services.AddCompleteWebEngineAsync();
        }
    }
}
