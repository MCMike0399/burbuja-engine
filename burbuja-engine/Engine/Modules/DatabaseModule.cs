using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BurbujaEngine.Database.Core.Interfaces;
using BurbujaEngine.Database.Extensions;
using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Engine.Modules;

/// <summary>
/// Database module for BurbujaEngine.
/// Manages database connections and provides database services to other modules.
/// </summary>
public class DatabaseModule : BaseEngineModule
{
    private IDatabaseConnection? _databaseConnection;
    private IHealthChecker? _healthChecker;
    
    public override string ModuleName => "Database Module";
    public override string Version => "1.0.0";
    
    /// <summary>
    /// Configure priority for the database module.
    /// Database is critical infrastructure that other modules depend on.
    /// </summary>
    protected override ModulePriorityConfig ConfigurePriority()
    {
        return CreateAdvancedPriorityConfig(
            priority: ModulePriority.Infrastructure,
            subPriority: 10, // High priority within infrastructure
            canParallelInitialize: false, // Database should initialize alone
            contextAdjustments: new()
            {
                ["Development"] = -5, // Slightly higher priority in development
                ["Testing"] = -10 // Even higher priority in testing
            },
            tags: new() { "database", "infrastructure", "critical" }
        );
    }
    
    /// <summary>
    /// Configure database services.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Add database services to the container
        services.AddBurbujaEngineDatabase();
        
        LogInfo("Database services configured");
    }
    
    /// <summary>
    /// Initialize the database module.
    /// </summary>
    protected override Task OnInitializeAsync(CancellationToken cancellationToken)
    {
        LogInfo("Initializing database module...");
        
        try
        {
            // Get database connection from DI
            _databaseConnection = Context.ServiceProvider.GetRequiredService<IDatabaseConnection>();
            _healthChecker = Context.ServiceProvider.GetRequiredService<IHealthChecker>();
            
            LogInfo("Database services resolved successfully");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogError($"Failed to resolve database services: {ex.Message}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// Start the database module.
    /// </summary>
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        LogInfo("Starting database module...");
        
        if (_databaseConnection == null)
        {
            throw new InvalidOperationException("Database connection not initialized");
        }
        
        try
        {
            // Establish database connection
            var connected = await _databaseConnection.ConnectAsync();
            
            if (!connected)
            {
                throw new InvalidOperationException("Failed to establish database connection");
            }
            
            LogInfo("Database connection established successfully");
            
            // Perform basic health check
            var isHealthy = await _databaseConnection.PingAsync();
            if (!isHealthy)
            {
                LogWarning("Database ping failed, but connection is established");
            }
            else
            {
                LogInfo("Database health check passed");
            }
            
            // Get connection info for logging
            var connectionInfo = await _databaseConnection.GetConnectionInfoAsync();
            LogInfo($"Database info: {string.Join(", ", connectionInfo.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to start database module: {ex.Message}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// Stop the database module.
    /// </summary>
    protected override async Task OnStopAsync(CancellationToken cancellationToken)
    {
        LogInfo("Stopping database module...");
        
        if (_databaseConnection != null)
        {
            try
            {
                await _databaseConnection.DisconnectAsync();
                LogInfo("Database connection closed");
            }
            catch (Exception ex)
            {
                LogWarning($"Error closing database connection: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Shutdown the database module.
    /// </summary>
    protected override async Task OnShutdownAsync(CancellationToken cancellationToken)
    {
        LogInfo("Shutting down database module...");
        
        // Database connection should already be closed in stop
        if (_databaseConnection?.IsConnected == true)
        {
            try
            {
                await _databaseConnection.DisconnectAsync();
                LogInfo("Database connection closed during shutdown");
            }
            catch (Exception ex)
            {
                LogWarning($"Error closing database connection during shutdown: {ex.Message}");
            }
        }
        
        LogInfo("Database module shutdown complete");
    }
    
    /// <summary>
    /// Get health information for the database module.
    /// </summary>
    protected override async Task<ModuleHealth> OnGetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_databaseConnection == null)
            {
                return ModuleHealth.Unhealthy(ModuleId, ModuleName, "Database connection not initialized");
            }
            
            if (!_databaseConnection.IsConnected)
            {
                return ModuleHealth.Unhealthy(ModuleId, ModuleName, "Database connection not established");
            }
            
            // Perform comprehensive health check
            if (_healthChecker != null)
            {
                var isHealthy = await _healthChecker.CheckConnectivityAsync();
                if (!isHealthy)
                {
                    return ModuleHealth.Critical(ModuleId, ModuleName, "Database connectivity check failed");
                }
                
                // Get additional health details
                var healthDetails = await _healthChecker.CheckHealthAsync();
                var serverInfo = await _healthChecker.GetServerInfoAsync();
                var dbStats = await _healthChecker.GetDatabaseStatsAsync();
                
                var health = ModuleHealth.Healthy(ModuleId, ModuleName, "Database is operating normally");
                health.Details["health"] = healthDetails;
                health.Details["server"] = serverInfo;
                health.Details["stats"] = dbStats;
                health.Details["connected"] = _databaseConnection.IsConnected;
                
                return health;
            }
            else
            {
                // Basic ping check
                var canPing = await _databaseConnection.PingAsync();
                if (canPing)
                {
                    return ModuleHealth.Healthy(ModuleId, ModuleName, "Database connection is responsive");
                }
                else
                {
                    return ModuleHealth.Warning(ModuleId, ModuleName, "Database connection established but ping failed");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Database health check failed: {ex.Message}", ex);
            return ModuleHealth.Unhealthy(ModuleId, ModuleName, $"Health check exception: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Populate database-specific diagnostics.
    /// </summary>
    protected override async Task OnPopulateDiagnosticsAsync(ModuleDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        try
        {
            if (_databaseConnection != null)
            {
                diagnostics.Performance["connected"] = _databaseConnection.IsConnected;
                
                if (_databaseConnection.IsConnected)
                {
                    var connectionInfo = await _databaseConnection.GetConnectionInfoAsync();
                    diagnostics.Configuration["connection"] = connectionInfo;
                    
                    if (_healthChecker != null)
                    {
                        var serverInfo = await _healthChecker.GetServerInfoAsync();
                        var dbStats = await _healthChecker.GetDatabaseStatsAsync();
                        
                        diagnostics.Performance["server_info"] = serverInfo;
                        diagnostics.Performance["database_stats"] = dbStats;
                    }
                }
            }
            
            diagnostics.Metadata["database_provider"] = "MongoDB";
            diagnostics.Metadata["supports_transactions"] = true;
            diagnostics.Metadata["supports_indexes"] = true;
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to populate database diagnostics: {ex.Message}");
            diagnostics.Metadata["diagnostics_error"] = ex.Message;
        }
    }
    
    /// <summary>
    /// Get the database connection instance.
    /// Provides access to other modules that depend on database.
    /// </summary>
    public IDatabaseConnection? GetDatabaseConnection() => _databaseConnection;
    
    /// <summary>
    /// Get the health checker instance.
    /// </summary>
    public IHealthChecker? GetHealthChecker() => _healthChecker;
}
