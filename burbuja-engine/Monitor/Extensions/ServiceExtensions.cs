using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BurbujaEngine.Monitor.Core;
using BurbujaEngine.Monitor.Services;
using BurbujaEngine.Monitor.Controllers;
using BurbujaEngine.Monitor.Endpoints;
using BurbujaEngine.Engine.Extensions;
using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Monitor.Extensions;

/// <summary>
/// Extension methods for registering Monitor module services.
/// Following the same pattern as the Database package.
/// </summary>
public static class MonitorServiceExtensions
{
    /// <summary>
    /// Add BurbujaEngine monitor services to the dependency injection container.
    /// </summary>
    public static IServiceCollection AddBurbujaEngineMonitor(this IServiceCollection services)
    {
        // Register monitor core services
        services.AddSingleton<MonitorModule>();
        services.AddSingleton<IMonitorService>(provider => provider.GetRequiredService<MonitorModule>());
        services.AddSingleton<IMetricsProvider>(provider => provider.GetRequiredService<MonitorModule>());
        services.AddSingleton<IMonitorEventLogger>(provider => provider.GetRequiredService<MonitorModule>());
        
        // Register the module as a hosted service for background processing
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<MonitorModule>());
        
        // Register controllers
        services.AddScoped<MonitorController>();
        
        return services;
    }

    /// <summary>
    /// Configure monitor endpoints.
    /// </summary>
    public static IEndpointRouteBuilder UseMonitorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapMonitorEndpoints();
    }

    /// <summary>
    /// Add Blazor services required for monitor dashboard.
    /// </summary>
    public static IServiceCollection AddMonitorBlazorServices(this IServiceCollection services)
    {
        services.AddRazorPages();
        services.AddServerSideBlazor();
        return services;
    }

    /// <summary>
    /// Configure the monitor dashboard in the application.
    /// </summary>
    public static WebApplication UseMonitorDashboard(this WebApplication app)
    {
        // Add static files support for monitor CSS
        app.UseStaticFiles();
        
        // Add Blazor hub
        app.MapBlazorHub();
        
        // Map monitor endpoints
        app.UseMonitorEndpoints();
        
        return app;
    }
}
