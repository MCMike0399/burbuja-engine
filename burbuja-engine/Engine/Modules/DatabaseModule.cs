using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BurbujaEngine.Database.Core.Interfaces;
using BurbujaEngine.Database.Extensions;
using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Engine.Modules;

/// <summary>
/// Database module for BurbujaEngine - A User-Space Service Example.
/// 
/// MICROKERNEL PATTERN: Step 3 - Modularize Services (Practical Example)
/// 
/// This module demonstrates how complex functionality (database access) is implemented
/// as a user-space service in the microkernel architecture:
/// 
/// USER-SPACE SERVICE CHARACTERISTICS:
/// - Business Logic: Encapsulates all database-related functionality
/// - Microkernel Integration: Uses microkernel services through IModuleContext
/// - Service Provision: Registers database services for other modules to use
/// - Health Monitoring: Provides detailed health checks for database connectivity
/// - Error Isolation: Database failures don't crash the microkernel or other modules
/// 
/// MODULAR DESIGN BENEFITS:
/// - Independent Development: Can be developed and tested separately
/// - Easy Updates: Database logic can be updated without touching the microkernel
/// - Fault Tolerance: Module failures are contained and reported
/// - Reusability: Can be reused across different engine configurations
/// 
/// INFRASTRUCTURE MODULE PRIORITY:
/// - High Priority: Database is critical infrastructure other modules depend on
/// - Context-Aware: Priority adjusts based on execution environment
/// - Dependency Coordination: Initializes before modules that need database access
/// 
/// This exemplifies how the microkernel architecture enables building complex,
/// enterprise-grade systems with clean separation of concerns.
/// </summary>
[DiscoverableModule(
    Name = "Database Module",
    Version = "1.0.0",
    Priority = 1000,
    Tags = new[] { "database", "infrastructure", "storage" },
    Enabled = true
)]
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
    protected override ModulePriority ConfigurePriority()
    {
        return ModulePriority.Create(PriorityLevel.Infrastructure)
            .WithSubPriority(10) // High priority within infrastructure
            .CanParallelInitialize(false) // Database should initialize alone
            .WithContextAdjustment("Development", -5) // Slightly higher priority in development
            .WithContextAdjustment("Testing", -10) // Even higher priority in testing
            .WithTags("database", "infrastructure", "critical")
            .Build();
    }
    /// <summary>
    /// Initialize the database module.
    /// MICROKERNEL PRINCIPLE: Graceful Degradation - Module should initialize even if services are unavailable
    /// </summary>
    protected override Task OnInitializeAsync(CancellationToken cancellationToken)
    {
        LogInfo("Initializing database module...");
        
        try
        {
            // Try to get database connection from DI - non-critical if it fails
            _databaseConnection = Context.ServiceProvider.GetService<IDatabaseConnection>();
            _healthChecker = Context.ServiceProvider.GetService<IHealthChecker>();
            
            if (_databaseConnection == null)
            {
                LogWarning("Database connection service not available - module will run in degraded mode");
            }
            
            if (_healthChecker == null)
            {
                LogWarning("Health checker service not available - basic health checks only");
            }
            
            if (_databaseConnection != null)
            {
                LogInfo("Database services resolved successfully");
            }
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to resolve database services: {ex.Message} - module will run in degraded mode");
            // Don't throw - allow module to continue in degraded state
            return Task.CompletedTask;
        }
    }
    
    /// <summary>
    /// Start the database module.
    /// MICROKERNEL PRINCIPLE: Fault Tolerance - Module should start gracefully even if database is unavailable
    /// </summary>
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        LogInfo("Starting database module...");
        
        if (_databaseConnection == null)
        {
            LogError("Database connection not initialized - module will run in degraded mode");
            return; // Continue without database connection
        }
        
        try
        {
            // Attempt database connection with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            var connected = await _databaseConnection.ConnectAsync();
            
            if (!connected)
            {
                LogWarning("Failed to establish database connection - module will run in degraded mode");
                return; // Continue without connection - other modules can still work
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
        catch (OperationCanceledException)
        {
            LogWarning("Database connection timed out - module will run in degraded mode");
            // Don't throw - let the module continue in degraded state
        }
        catch (Exception ex)
        {
            LogWarning($"Database connection failed: {ex.Message} - module will run in degraded mode");
            // Don't throw - let the module continue in degraded state
            // Other modules (like Monitor) can still function
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
    /// MICROKERNEL PRINCIPLE: Health monitoring should work even in degraded state
    /// </summary>
    protected override async Task<ModuleHealth> OnGetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_databaseConnection == null)
            {
                return ModuleHealth.Warning(ModuleId, ModuleName, 
                    "Database module running in degraded mode - connection not initialized");
            }
            
            if (!_databaseConnection.IsConnected)
            {
                return ModuleHealth.Warning(ModuleId, ModuleName, 
                    "Database module running in degraded mode - connection not established");
            }
            
            // Perform comprehensive health check
            if (_healthChecker != null)
            {
                var isHealthy = await _healthChecker.CheckConnectivityAsync();
                if (!isHealthy)
                {
                    return ModuleHealth.Warning(ModuleId, ModuleName, 
                        "Database connectivity check failed - module in degraded mode");
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
                    return ModuleHealth.Warning(ModuleId, ModuleName, 
                        "Database connection established but ping failed");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Database health check failed: {ex.Message}", ex);
            return ModuleHealth.Warning(ModuleId, ModuleName, 
                $"Database module in degraded mode - health check exception: {ex.Message}");
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
    
    /// <summary>
    /// Configure services that this module provides and requires.
    /// 
    /// SIMPLIFIED MICROKERNEL PATTERN: Direct Service Registration
    /// 
    /// This method implements the simplified microkernel pattern where modules
    /// register their services directly with the DI container. This eliminates
    /// the need for complex driver communication patterns.
    /// 
    /// BENEFITS:
    /// - Direct Integration: Services are directly available via DI
    /// - Standard Patterns: Uses familiar .NET DI patterns
    /// - Better Performance: No message passing overhead
    /// - Easier Testing: Standard mocking and testing patterns
    /// - Simpler Debugging: Direct call stacks
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Register all database-related services that this module provides
        services.AddBurbujaEngineDatabase();
        
        // Register the module itself as a service so other modules can access it
        services.AddSingleton<DatabaseModule>(provider =>
        {
            var engine = provider.GetService<IBurbujaEngine>();
            return engine?.GetModule<DatabaseModule>() ?? throw new InvalidOperationException("DatabaseModule not found");
        });
        
        // Register database services directly for easy consumption by other modules
        services.AddSingleton<IDatabaseConnection>(provider =>
        {
            var databaseModule = provider.GetRequiredService<DatabaseModule>();
            return databaseModule.GetDatabaseConnection() ?? throw new InvalidOperationException("Database connection not available");
        });
        
        services.AddSingleton<IHealthChecker>(provider =>
        {
            var databaseModule = provider.GetRequiredService<DatabaseModule>();
            return databaseModule.GetHealthChecker() ?? throw new InvalidOperationException("Database health checker not available");
        });
        
        // Register collection factory if available
        services.AddSingleton<ICollectionFactory>(provider =>
        {
            var databaseModule = provider.GetRequiredService<DatabaseModule>();
            return databaseModule.GetCollectionFactory() ?? throw new InvalidOperationException("Database collection factory not available");
        });
    }
    
    /// <summary>
    /// Get the collection factory instance.
    /// </summary>
    public ICollectionFactory? GetCollectionFactory() => Context.ServiceProvider.GetService<ICollectionFactory>();
}
