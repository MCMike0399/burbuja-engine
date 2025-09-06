using BurbujaEngine.Configuration;
using BurbujaEngine.Database.Core.Interfaces;
using BurbujaEngine.Database.Core.Exceptions;
using BurbujaEngine.Database.Connection.Strategies;
using MongoDB.Driver;
using MongoDB.Bson;

namespace BurbujaEngine.Database.Connection;

/// <summary>
/// Singleton database connection manager.
/// Implements connection management with thread safety.
/// </summary>
public class DatabaseConnectionManager : IDatabaseConnection, IHealthChecker
{
    private readonly EnvironmentConfig _config;
    private readonly ConnectionStrategyFactory _strategyFactory;
    private readonly ILogger<DatabaseConnectionManager> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    
    private MongoClient? _client;
    private IMongoDatabase? _database;
    private IConnectionStrategy? _strategy;
    private bool _isConnected;
    
    public DatabaseConnectionManager(
        EnvironmentConfig config,
        ConnectionStrategyFactory strategyFactory,
        ILogger<DatabaseConnectionManager> logger)
    {
        _config = config;
        _strategyFactory = strategyFactory;
        _logger = logger;
        _strategy = _strategyFactory.CreateStrategy();
        
        _logger.LogInformation("Database connection manager initialized");
    }
    
    public bool IsConnected => _isConnected && _client != null;
    
    public async Task<bool> ConnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (IsConnected)
            {
                _logger.LogDebug("Database already connected");
                return true;
            }
            
            _logger.LogInformation("Establishing database connection...");
            
            // Ensure strategy is initialized
            EnsureStrategyInitialized();
            
            // Create client using strategy
            _client = await _strategy!.CreateClientAsync();
            
            // Get database instance
            _database = _client.GetDatabase(_config.MongoDbDatabase);
            
            // Test connection
            var connectionOk = await _strategy.TestConnectionAsync(_client);
            if (!connectionOk)
            {
                await CleanupConnectionAsync();
                return false;
            }
            
            _isConnected = true;
            
            // Log connection info
            var connectionInfo = await GetConnectionInfoAsync();
            _logger.LogInformation("Database connection established: {@ConnectionInfo}", connectionInfo);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish database connection");
            await CleanupConnectionAsync();
            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }
    
    public async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (IsConnected)
            {
                _logger.LogInformation("Closing database connection...");
                await CleanupConnectionAsync();
                _logger.LogInformation("Database connection closed");
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }
    
    public async Task<bool> PingAsync()
    {
        if (!IsConnected)
            return false;
        
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _client!.GetDatabase("admin").RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cts.Token
            );
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database ping failed");
            _isConnected = false;
            return false;
        }
    }
    
    public IMongoDatabase GetDatabase()
    {
        EnsureConnected();
        return _database!;
    }
    
    public MongoClient GetClient()
    {
        EnsureConnected();
        return _client!;
    }
    
    public async Task<Dictionary<string, object>> GetConnectionInfoAsync()
    {
        var baseInfo = new Dictionary<string, object>
        {
            ["connected"] = _isConnected,
            ["database_name"] = _config.MongoDbDatabase
        };
        
        if (_strategy != null)
        {
            var strategyInfo = _strategy.GetConnectionInfo();
            foreach (var kvp in strategyInfo)
            {
                baseInfo[kvp.Key] = kvp.Value;
            }
        }
        
        if (IsConnected)
        {
            try
            {
                // Get server info
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var serverInfo = await _client!.GetDatabase("admin").RunCommandAsync<BsonDocument>(
                    new BsonDocument("ismaster", 1),
                    cancellationToken: cts.Token
                );
                
                baseInfo["server_version"] = serverInfo.GetValue("version", "unknown").ToString() ?? "unknown";
                baseInfo["max_wire_version"] = serverInfo.GetValue("maxWireVersion", 0);
                baseInfo["is_master"] = serverInfo.GetValue("ismaster", false);
                baseInfo["replica_set"] = serverInfo.GetValue("setName", "").ToString() ?? "";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get server info");
                baseInfo["server_info_error"] = ex.Message;
            }
        }
        
        return baseInfo;
    }
    
    public async Task EnsureConnectionAsync()
    {
        if (!IsConnected)
        {
            var success = await ConnectAsync();
            if (!success)
            {
                throw new ConnectionException("Could not establish database connection");
            }
        }
    }
    
    public async Task<bool> ReconnectAsync()
    {
        _logger.LogInformation("Attempting to reconnect to database...");
        await DisconnectAsync();
        return await ConnectAsync();
    }
    
    // IHealthChecker implementation
    
    public async Task<Dictionary<string, object>> CheckHealthAsync()
    {
        var healthStatus = new Dictionary<string, object>
        {
            ["status"] = "healthy",
            ["timestamp"] = DateTime.UtcNow,
            ["checks"] = new Dictionary<string, object>()
        };
        
        var checks = (Dictionary<string, object>)healthStatus["checks"];
        
        try
        {
            // Check connectivity
            var connectivityOk = await CheckConnectivityAsync();
            checks["connectivity"] = new Dictionary<string, object>
            {
                ["status"] = connectivityOk ? "healthy" : "unhealthy",
                ["message"] = connectivityOk ? "Database is reachable" : "Database is not reachable"
            };
            
            if (!connectivityOk)
            {
                healthStatus["status"] = "unhealthy";
                return healthStatus;
            }
            
            // Check server info
            try
            {
                var serverInfo = await GetServerInfoAsync();
                checks["server_info"] = new Dictionary<string, object>
                {
                    ["status"] = "healthy",
                    ["version"] = serverInfo.GetValueOrDefault("version", "unknown"),
                    ["uptime"] = serverInfo.GetValueOrDefault("uptime", 0)
                };
            }
            catch (Exception ex)
            {
                checks["server_info"] = new Dictionary<string, object>
                {
                    ["status"] = "degraded",
                    ["error"] = ex.Message
                };
                healthStatus["status"] = "degraded";
            }
            
            // Check database stats
            try
            {
                var dbStats = await GetDatabaseStatsAsync();
                checks["database_stats"] = new Dictionary<string, object>
                {
                    ["status"] = "healthy",
                    ["collections"] = dbStats.GetValueOrDefault("collections", 0),
                    ["data_size"] = dbStats.GetValueOrDefault("dataSize", 0)
                };
            }
            catch (Exception ex)
            {
                checks["database_stats"] = new Dictionary<string, object>
                {
                    ["status"] = "degraded",
                    ["error"] = ex.Message
                };
                if (healthStatus["status"].ToString() == "healthy")
                    healthStatus["status"] = "degraded";
            }
        }
        catch (Exception ex)
        {
            healthStatus["status"] = "unhealthy";
            healthStatus["error"] = ex.Message;
        }
        
        return healthStatus;
    }
    
    public async Task<bool> CheckConnectivityAsync()
    {
        if (!IsConnected)
            return false;
        
        return await PingAsync();
    }
    
    public async Task<Dictionary<string, object>> GetServerInfoAsync()
    {
        try
        {
            EnsureConnected();
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serverStatus = await _client!.GetDatabase("admin").RunCommandAsync<BsonDocument>(
                new BsonDocument("serverStatus", 1),
                cancellationToken: cts.Token
            );
            
            return new Dictionary<string, object>
            {
                ["version"] = serverStatus.GetValue("version", "unknown").ToString() ?? "unknown",
                ["uptime"] = serverStatus.GetValue("uptime", 0).ToInt64(),
                ["connections"] = serverStatus.GetValue("connections", new BsonDocument()).AsBsonDocument.ToDictionary(),
                ["network"] = serverStatus.GetValue("network", new BsonDocument()).AsBsonDocument.ToDictionary(),
                ["opcounters"] = serverStatus.GetValue("opcounters", new BsonDocument()).AsBsonDocument.ToDictionary()
            };
        }
        catch (Exception ex)
        {
            throw new HealthCheckException("Failed to get server info", "server_info", ex);
        }
    }
    
    public async Task<Dictionary<string, object>> GetDatabaseStatsAsync()
    {
        try
        {
            EnsureConnected();
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stats = await _database!.RunCommandAsync<BsonDocument>(
                new BsonDocument("dbStats", 1),
                cancellationToken: cts.Token
            );
            
            // Also get collection list
            var collections = await (await _database.ListCollectionNamesAsync(cancellationToken: cts.Token)).ToListAsync(cts.Token);
            
            return new Dictionary<string, object>
            {
                ["db"] = stats.GetValue("db", "").ToString() ?? "",
                ["collections"] = collections.Count,
                ["collection_names"] = collections,
                ["dataSize"] = stats.GetValue("dataSize", 0).ToInt64(),
                ["storageSize"] = stats.GetValue("storageSize", 0).ToInt64(),
                ["indexes"] = stats.GetValue("indexes", 0).ToInt32(),
                ["indexSize"] = stats.GetValue("indexSize", 0).ToInt64(),
                ["objects"] = stats.GetValue("objects", 0).ToInt64(),
                ["avgObjSize"] = stats.GetValue("avgObjSize", 0).ToDouble()
            };
        }
        catch (Exception ex)
        {
            throw new HealthCheckException("Failed to get database stats", "database_stats", ex);
        }
    }
    
    // Private methods
    
    private void EnsureStrategyInitialized()
    {
        if (_strategy == null)
            throw new ConnectionException("Connection strategy not initialized");
    }
    
    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new ConnectionException("Not connected to database");
    }
    
    private Task CleanupConnectionAsync()
    {
        _isConnected = false;
        
        if (_client != null)
        {
            try
            {
                // MongoDB driver doesn't have an explicit close method
                // The connection will be cleaned up when the client is disposed
                _client = null;
                _database = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during connection cleanup");
            }
        }
        
        return Task.CompletedTask;
    }
    
    // IDisposable support
    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _connectionLock.Dispose();
    }
}
