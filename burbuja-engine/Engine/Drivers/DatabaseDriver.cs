using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BurbujaEngine.Database.Core.Interfaces;
using BurbujaEngine.Database.Extensions;
using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Engine.Drivers;

/// <summary>
/// Database driver for the BurbujaEngine microkernel.
/// Provides database services and handles inter-module database communication.
/// </summary>
public class DatabaseDriver : BaseEngineDriver
{
    private IDatabaseConnection? _databaseConnection;
    private IHealthChecker? _healthChecker;
    private ICollectionFactory? _collectionFactory;
    
    public override string DriverName => "Database Driver";
    public override string Version => "1.0.0";
    public override DriverType Type => DriverType.Database;
    
    /// <summary>
    /// Initialize the database driver.
    /// </summary>
    protected override async Task OnInitializeAsync(CancellationToken cancellationToken)
    {
        LogInfo("Initializing database driver...");
        
        try
        {
            // Get database services from DI
            _databaseConnection = Context.ServiceProvider.GetRequiredService<IDatabaseConnection>();
            _healthChecker = Context.ServiceProvider.GetRequiredService<IHealthChecker>();
            _collectionFactory = Context.ServiceProvider.GetRequiredService<ICollectionFactory>();
            
            LogInfo("Database services resolved successfully");
            
            // Subscribe to driver communication messages
            await Context.CommunicationBus.SubscribeToMessageTypeAsync(DriverId, "DatabaseQuery", HandleDatabaseQueryMessage);
            await Context.CommunicationBus.SubscribeToMessageTypeAsync(DriverId, "DatabaseHealth", HandleDatabaseHealthMessage);
            await Context.CommunicationBus.SubscribeToMessageTypeAsync(DriverId, "CreateCollection", HandleCreateCollectionMessage);
            await Context.CommunicationBus.SubscribeToMessageTypeAsync(DriverId, "GetConnectionInfo", HandleGetConnectionInfoMessage);
            
            LogInfo("Database driver message handlers registered");
        }
        catch (Exception ex)
        {
            LogError($"Failed to initialize database driver: {ex.Message}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// Start the database driver.
    /// </summary>
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        LogInfo("Starting database driver...");
        
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
            
            // Notify other drivers that database is available
            await BroadcastMessageToDriverTypeAsync(DriverType.Database, "DatabaseReady", new
            {
                DriverId,
                ConnectionInfo = connectionInfo,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            LogError($"Failed to start database driver: {ex.Message}", ex);
            throw;
        }
    }
    
    /// <summary>
    /// Stop the database driver.
    /// </summary>
    protected override async Task OnStopAsync(CancellationToken cancellationToken)
    {
        LogInfo("Stopping database driver...");
        
        // Notify other drivers that database is going down
        await BroadcastMessageToDriverTypeAsync(DriverType.Database, "DatabaseStopping", new
        {
            DriverId,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);
        
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
    /// Shutdown the database driver.
    /// </summary>
    protected override async Task OnShutdownAsync(CancellationToken cancellationToken)
    {
        LogInfo("Shutting down database driver...");
        
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
        
        LogInfo("Database driver shutdown complete");
    }
    
    /// <summary>
    /// Get health information for the database driver.
    /// </summary>
    protected override async Task<DriverHealth> OnGetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_databaseConnection == null)
            {
                return DriverHealth.Unhealthy(DriverId, DriverName, "Database connection not initialized");
            }
            
            if (!_databaseConnection.IsConnected)
            {
                return DriverHealth.Unhealthy(DriverId, DriverName, "Database connection not established");
            }
            
            // Perform comprehensive health check
            if (_healthChecker != null)
            {
                var isHealthy = await _healthChecker.CheckConnectivityAsync();
                if (!isHealthy)
                {
                    return DriverHealth.Critical(DriverId, DriverName, "Database connectivity check failed");
                }
                
                // Get additional health details
                var healthDetails = await _healthChecker.CheckHealthAsync();
                var serverInfo = await _healthChecker.GetServerInfoAsync();
                var dbStats = await _healthChecker.GetDatabaseStatsAsync();
                
                var health = DriverHealth.Healthy(DriverId, DriverName, "Database is operating normally");
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
                    return DriverHealth.Healthy(DriverId, DriverName, "Database connection is responsive");
                }
                else
                {
                    return DriverHealth.Warning(DriverId, DriverName, "Database connection established but ping failed");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Database health check failed: {ex.Message}", ex);
            return DriverHealth.Unhealthy(DriverId, DriverName, $"Health check exception: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Populate database-specific diagnostics.
    /// </summary>
    protected override async Task OnPopulateDiagnosticsAsync(DriverDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        await base.OnPopulateDiagnosticsAsync(diagnostics, cancellationToken);
        
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
            diagnostics.Metadata["driver_capabilities"] = new[]
            {
                "ConnectionManagement",
                "HealthChecking",
                "CollectionFactory",
                "InterDriverCommunication"
            };
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to populate database diagnostics: {ex.Message}");
            diagnostics.Metadata["diagnostics_error"] = ex.Message;
        }
    }
    
    /// <summary>
    /// Handle database query messages from other drivers.
    /// </summary>
    private async Task<DriverMessage?> HandleDatabaseQueryMessage(DriverMessage message)
    {
        try
        {
            LogDebug($"Handling database query from driver {message.SourceDriverId}");
            
            if (message.Payload is not DatabaseQueryRequest request)
            {
                return CreateErrorResponse(message, "Invalid query request payload");
            }
            
            // Execute the database query based on the request
            var result = await ExecuteDatabaseQuery(request);
            
            return new DriverMessage
            {
                MessageId = Guid.NewGuid(),
                SourceDriverId = DriverId,
                TargetDriverId = message.SourceDriverId,
                MessageType = "DatabaseQueryResponse",
                Payload = result,
                RequiresResponse = false,
                ResponseToMessageId = message.MessageId,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogError($"Failed to handle database query: {ex.Message}", ex);
            return CreateErrorResponse(message, ex.Message);
        }
    }
    
    /// <summary>
    /// Handle database health check messages from other drivers.
    /// </summary>
    private async Task<DriverMessage?> HandleDatabaseHealthMessage(DriverMessage message)
    {
        try
        {
            var health = await GetHealthAsync(CancellationToken.None);
            
            return new DriverMessage
            {
                MessageId = Guid.NewGuid(),
                SourceDriverId = DriverId,
                TargetDriverId = message.SourceDriverId,
                MessageType = "DatabaseHealthResponse",
                Payload = health,
                RequiresResponse = false,
                ResponseToMessageId = message.MessageId,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogError($"Failed to handle database health request: {ex.Message}", ex);
            return CreateErrorResponse(message, ex.Message);
        }
    }
    
    /// <summary>
    /// Handle collection creation messages from other drivers.
    /// </summary>
    private async Task<DriverMessage?> HandleCreateCollectionMessage(DriverMessage message)
    {
        try
        {
            if (message.Payload is not CreateCollectionRequest request)
            {
                return CreateErrorResponse(message, "Invalid collection creation request payload");
            }
            
            if (_collectionFactory == null)
            {
                return CreateErrorResponse(message, "Collection factory not available");
            }
            
            // Check if collection already exists
            var exists = await _collectionFactory.CollectionExistsAsync(request.CollectionName);
            
            var response = new CreateCollectionResponse
            {
                CollectionName = request.CollectionName,
                AlreadyExists = exists,
                Success = true,
                Message = exists ? "Collection already exists" : "Collection ready for use"
            };
            
            return new DriverMessage
            {
                MessageId = Guid.NewGuid(),
                SourceDriverId = DriverId,
                TargetDriverId = message.SourceDriverId,
                MessageType = "CreateCollectionResponse",
                Payload = response,
                RequiresResponse = false,
                ResponseToMessageId = message.MessageId,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogError($"Failed to handle collection creation request: {ex.Message}", ex);
            return CreateErrorResponse(message, ex.Message);
        }
    }
    
    /// <summary>
    /// Handle connection info requests from other drivers.
    /// </summary>
    private async Task<DriverMessage?> HandleGetConnectionInfoMessage(DriverMessage message)
    {
        try
        {
            if (_databaseConnection == null)
            {
                return CreateErrorResponse(message, "Database connection not available");
            }
            
            var connectionInfo = await _databaseConnection.GetConnectionInfoAsync();
            
            return new DriverMessage
            {
                MessageId = Guid.NewGuid(),
                SourceDriverId = DriverId,
                TargetDriverId = message.SourceDriverId,
                MessageType = "ConnectionInfoResponse",
                Payload = connectionInfo,
                RequiresResponse = false,
                ResponseToMessageId = message.MessageId,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogError($"Failed to handle connection info request: {ex.Message}", ex);
            return CreateErrorResponse(message, ex.Message);
        }
    }
    
    /// <summary>
    /// Execute a database query request.
    /// </summary>
    private Task<DatabaseQueryResponse> ExecuteDatabaseQuery(DatabaseQueryRequest request)
    {
        // This is a simplified implementation - in a real system, you'd have more sophisticated query handling
        var response = new DatabaseQueryResponse
        {
            Success = true,
            Message = "Query executed successfully",
            Data = new { timestamp = DateTime.UtcNow, request.QueryType }
        };
        
        return Task.FromResult(response);
    }
    
    /// <summary>
    /// Create an error response message.
    /// </summary>
    private DriverMessage CreateErrorResponse(DriverMessage originalMessage, string errorMessage)
    {
        return new DriverMessage
        {
            MessageId = Guid.NewGuid(),
            SourceDriverId = DriverId,
            TargetDriverId = originalMessage.SourceDriverId,
            MessageType = "Error",
            Payload = new { error = errorMessage, timestamp = DateTime.UtcNow },
            RequiresResponse = false,
            ResponseToMessageId = originalMessage.MessageId,
            Timestamp = DateTime.UtcNow
        };
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
    /// Get the collection factory instance.
    /// </summary>
    public ICollectionFactory? GetCollectionFactory() => _collectionFactory;
}

/// <summary>
/// Database query request structure for inter-driver communication.
/// </summary>
public class DatabaseQueryRequest
{
    public string QueryType { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public object? Parameters { get; set; }
    public Dictionary<string, object> Options { get; set; } = new();
}

/// <summary>
/// Database query response structure for inter-driver communication.
/// </summary>
public class DatabaseQueryResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Collection creation request structure for inter-driver communication.
/// </summary>
public class CreateCollectionRequest
{
    public string CollectionName { get; set; } = string.Empty;
    public Dictionary<string, object> Options { get; set; } = new();
}

/// <summary>
/// Collection creation response structure for inter-driver communication.
/// </summary>
public class CreateCollectionResponse
{
    public string CollectionName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool AlreadyExists { get; set; }
    public string Message { get; set; } = string.Empty;
}
