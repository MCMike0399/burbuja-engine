using BurbujaEngine.Configuration;
using BurbujaEngine.Database.Core.Interfaces;
using BurbujaEngine.Database.Core.Exceptions;
using MongoDB.Driver;
using MongoDB.Bson;

namespace BurbujaEngine.Database.Connection.Strategies;

/// <summary>
/// Abstract base class for connection strategies.
/// Implements common connection logic and the Template Method pattern.
/// </summary>
public abstract class BaseConnectionStrategy : IConnectionStrategy
{
    protected readonly EnvironmentConfig _config;
    protected readonly ILogger _logger;
    
    protected BaseConnectionStrategy(EnvironmentConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }
    
    public abstract string StrategyName { get; }
    
    public async Task<MongoClient> CreateClientAsync()
    {
        try
        {
            _logger.LogInformation("Creating MongoDB client using {StrategyName} strategy", StrategyName);
            
            var settings = BuildMongoClientSettings();
            var client = new MongoClient(settings);
            
            // Test the connection
            await PerformInitialConnectionTestAsync(client);
            
            _logger.LogInformation("Successfully created MongoDB client using {StrategyName} strategy", StrategyName);
            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MongoDB client using {StrategyName} strategy", StrategyName);
            throw new ConnectionException(
                $"{StrategyName} client creation failed: {ex.Message}",
                _config.MongoDbHost,
                _config.MongoDbPort,
                _config.MongoDbDatabase,
                ex
            );
        }
    }
    
    public async Task<bool> TestConnectionAsync(MongoClient client)
    {
        try
        {
            // Perform ping with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(GetTestTimeoutSeconds()));
            await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
                new MongoDB.Bson.BsonDocument("ping", 1),
                cancellationToken: cts.Token
            );
            
            // Additional strategy-specific tests
            await PerformStrategySpecificTestsAsync(client);
            
            _logger.LogDebug("{StrategyName} connection test successful", StrategyName);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("{StrategyName} connection test timed out", StrategyName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{StrategyName} connection test failed", StrategyName);
            return false;
        }
    }
    
    public Dictionary<string, object> GetConnectionInfo()
    {
        var info = new Dictionary<string, object>
        {
            ["strategy"] = StrategyName,
            ["host"] = _config.MongoDbHost,
            ["port"] = _config.MongoDbPort,
            ["database"] = _config.MongoDbDatabase,
            ["ssl_enabled"] = _config.MongoDbSslEnabled,
            ["connection_timeout"] = _config.MongoDbConnectionTimeout,
            ["server_selection_timeout"] = _config.MongoDbServerSelectionTimeout
        };
        
        // Add strategy-specific info
        var specificInfo = GetStrategySpecificInfo();
        foreach (var kvp in specificInfo)
        {
            info[kvp.Key] = kvp.Value;
        }
        
        return info;
    }
    
    protected abstract MongoClientSettings BuildMongoClientSettings();
    
    protected virtual double GetTestTimeoutSeconds() => 10.0;
    
    protected virtual async Task PerformInitialConnectionTestAsync(MongoClient client)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(GetTestTimeoutSeconds()));
            await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
                new MongoDB.Bson.BsonDocument("ping", 1),
                cancellationToken: cts.Token
            );
        }
        catch (OperationCanceledException ex)
        {
            throw new ConnectionTimeoutException(GetTestTimeoutSeconds(), _config.MongoDbHost, _config.MongoDbPort, ex);
        }
        catch (Exception ex)
        {
            throw new ConnectionException($"{StrategyName} initial connection test failed", _config.MongoDbHost, _config.MongoDbPort, _config.MongoDbDatabase, ex);
        }
    }
    
    protected virtual Task PerformStrategySpecificTestsAsync(MongoClient client)
    {
        // Default implementation does nothing
        // Override in concrete strategies for specific tests
        return Task.CompletedTask;
    }
    
    protected virtual Dictionary<string, object> GetStrategySpecificInfo()
    {
        // Default implementation returns empty dict
        // Override in concrete strategies for specific info
        return new Dictionary<string, object>();
    }
}

/// <summary>
/// Connection strategy for local MongoDB instances.
/// Optimized for development and local Docker containers.
/// </summary>
public class LocalMongoDbStrategy : BaseConnectionStrategy
{
    public LocalMongoDbStrategy(EnvironmentConfig config, ILogger<LocalMongoDbStrategy> logger) 
        : base(config, logger)
    {
    }
    
    public override string StrategyName => "LocalMongoDB";
    
    protected override MongoClientSettings BuildMongoClientSettings()
    {
        var settings = MongoClientSettings.FromConnectionString(_config.MongoDbUrl);
        
        // Local development optimizations
        settings.ConnectTimeout = TimeSpan.FromMilliseconds(_config.MongoDbConnectionTimeout);
        settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(_config.MongoDbServerSelectionTimeout);
        settings.MaxConnectionPoolSize = _config.MongoDbMaxPoolSize;
        settings.MinConnectionPoolSize = _config.MongoDbMinPoolSize;
        settings.MaxConnectionIdleTime = TimeSpan.FromSeconds(30);
        settings.HeartbeatInterval = TimeSpan.FromSeconds(10);
        settings.RetryWrites = true;
        settings.RetryReads = true;
        
        return settings;
    }
    
    protected override double GetTestTimeoutSeconds() => 5.0; // Local connections should be fast
    
    protected override async Task PerformStrategySpecificTestsAsync(MongoClient client)
    {
        try
        {
            // Test database access
            var database = client.GetDatabase(_config.MongoDbDatabase);
            
            // Test a simple operation
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var collections = await (await database.ListCollectionNamesAsync(cancellationToken: cts.Token)).ToListAsync(cts.Token);
            _logger.LogDebug("Local MongoDB has {CollectionCount} collections", collections.Count);
            
            // Test write operations
            var testCollection = database.GetCollection<MongoDB.Bson.BsonDocument>("_connection_test");
            await testCollection.InsertOneAsync(new MongoDB.Bson.BsonDocument 
            { 
                { "test", true }, 
                { "timestamp", DateTime.UtcNow } 
            }, cancellationToken: cts.Token);
            await testCollection.DeleteOneAsync(new MongoDB.Bson.BsonDocument("test", true), cancellationToken: cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local MongoDB specific tests failed");
            // Don't throw - these are additional tests
        }
    }
    
    protected override Dictionary<string, object> GetStrategySpecificInfo()
    {
        return new Dictionary<string, object>
        {
            ["replica_set"] = _config.MongoDbReplicaSet,
            ["auth_enabled"] = !string.IsNullOrEmpty(_config.MongoDbUsername) && !string.IsNullOrEmpty(_config.MongoDbPassword),
            ["optimization"] = "local_development"
        };
    }
}

/// <summary>
/// Connection strategy for AWS DocumentDB.
/// Optimized for cloud deployment and DocumentDB-specific requirements.
/// </summary>
public class DocumentDbStrategy : BaseConnectionStrategy
{
    public DocumentDbStrategy(EnvironmentConfig config, ILogger<DocumentDbStrategy> logger) 
        : base(config, logger)
    {
    }
    
    public override string StrategyName => "DocumentDB";
    
    protected override MongoClientSettings BuildMongoClientSettings()
    {
        var settings = MongoClientSettings.FromConnectionString(_config.MongoDbUrl);
        
        // DocumentDB specific optimizations
        settings.ConnectTimeout = TimeSpan.FromMilliseconds(_config.MongoDbConnectionTimeout);
        settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(_config.MongoDbServerSelectionTimeout);
        settings.MaxConnectionPoolSize = _config.MongoDbMaxPoolSize;
        settings.MinConnectionPoolSize = _config.MongoDbMinPoolSize;
        settings.MaxConnectionIdleTime = TimeSpan.FromSeconds(60); // Longer for cloud connections
        settings.HeartbeatInterval = TimeSpan.FromSeconds(30); // Longer for cloud
        settings.SocketTimeout = TimeSpan.FromSeconds(60);
        
        // DocumentDB doesn't support retryable writes
        settings.RetryWrites = false;
        settings.ReadPreference = ReadPreference.SecondaryPreferred;
        
        // SSL configuration for DocumentDB
        if (_config.MongoDbSslEnabled)
        {
            settings.UseTls = true;
            settings.AllowInsecureTls = false;
            
            if (!string.IsNullOrEmpty(_config.MongoDbSslCaFile))
            {
                // Configure SSL certificate if provided
                var sslSettings = new SslSettings
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
                };
                settings.SslSettings = sslSettings;
            }
        }
        
        return settings;
    }
    
    protected override double GetTestTimeoutSeconds() => 15.0; // Cloud connections may be slower
    
    protected override async Task PerformStrategySpecificTestsAsync(MongoClient client)
    {
        try
        {
            // Test replica set status
            var adminDb = client.GetDatabase("admin");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            
            var rsStatus = await adminDb.RunCommandAsync<MongoDB.Bson.BsonDocument>(
                new MongoDB.Bson.BsonDocument("replSetGetStatus", 1),
                cancellationToken: cts.Token
            );
            _logger.LogDebug("DocumentDB replica set status: {Status}", rsStatus.GetValue("ok", 0));
            
            // Test database access with read preference
            var database = client.GetDatabase(_config.MongoDbDatabase);
            var collections = await (await database.ListCollectionNamesAsync(cancellationToken: cts.Token)).ToListAsync(cts.Token);
            _logger.LogDebug("DocumentDB has {CollectionCount} collections", collections.Count);
            
            // Test server status for DocumentDB specific info
            var serverStatus = await adminDb.RunCommandAsync<MongoDB.Bson.BsonDocument>(
                new MongoDB.Bson.BsonDocument("serverStatus", 1),
                cancellationToken: cts.Token
            );
            var version = serverStatus.GetValue("version", "unknown").ToString();
            _logger.LogDebug("DocumentDB version: {Version}", version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DocumentDB specific tests failed");
            // Don't throw - these are additional tests
        }
    }
    
    protected override Dictionary<string, object> GetStrategySpecificInfo()
    {
        return new Dictionary<string, object>
        {
            ["ssl_enabled"] = _config.MongoDbSslEnabled,
            ["ssl_ca_file"] = _config.MongoDbSslCaFile,
            ["replica_set"] = _config.MongoDbReplicaSet,
            ["read_preference"] = "secondaryPreferred",
            ["retry_writes"] = false,
            ["optimization"] = "aws_documentdb"
        };
    }
}

/// <summary>
/// Factory for creating connection strategies.
/// Implements the Factory pattern for strategy creation.
/// </summary>
public class ConnectionStrategyFactory
{
    private readonly EnvironmentConfig _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConnectionStrategyFactory> _logger;
    
    private static readonly Dictionary<string, Type> StrategyTypes = new()
    {
        { "mongodb", typeof(LocalMongoDbStrategy) },
        { "local", typeof(LocalMongoDbStrategy) },
        { "documentdb", typeof(DocumentDbStrategy) },
        { "aws", typeof(DocumentDbStrategy) }
    };
    
    public ConnectionStrategyFactory(
        EnvironmentConfig config, 
        IServiceProvider serviceProvider,
        ILogger<ConnectionStrategyFactory> logger)
    {
        _config = config;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    public IConnectionStrategy CreateStrategy(string? strategyType = null)
    {
        strategyType ??= DetectStrategyFromConfig();
        strategyType = strategyType.ToLowerInvariant();
        
        if (!StrategyTypes.TryGetValue(strategyType, out var strategyTypeClass))
        {
            var available = string.Join(", ", StrategyTypes.Keys);
            throw new ArgumentException(
                $"Unknown connection strategy: {strategyType}. Available strategies: {available}"
            );
        }
        
        var strategy = (IConnectionStrategy)ActivatorUtilities.CreateInstance(_serviceProvider, strategyTypeClass);
        _logger.LogInformation("Created connection strategy: {StrategyName}", strategy.StrategyName);
        return strategy;
    }
    
    private string DetectStrategyFromConfig()
    {
        // Check explicit configuration
        var mongoDbType = _config.MongoDbType.ToLowerInvariant();
        if (StrategyTypes.ContainsKey(mongoDbType))
        {
            _logger.LogDebug("Strategy detected from MONGODB_TYPE: {MongoDbType}", mongoDbType);
            return mongoDbType;
        }
        
        // Check environment indicators
        if (_config.IsProduction)
        {
            // In production, prefer DocumentDB
            if (_config.MongoDbSslEnabled || !string.IsNullOrEmpty(_config.MongoDbReplicaSet))
            {
                _logger.LogDebug("Strategy detected: DocumentDB (production with SSL/replica set)");
                return "documentdb";
            }
        }
        
        // Check URL patterns
        var url = _config.MongoDbUrl.ToLowerInvariant();
        if (url.Contains("documentdb") || url.Contains("docdb"))
        {
            _logger.LogDebug("Strategy detected: DocumentDB (URL pattern)");
            return "documentdb";
        }
        
        // Default to local MongoDB
        _logger.LogDebug("Strategy detected: LocalMongoDB (default)");
        return "mongodb";
    }
    
    public static Dictionary<string, string> GetAvailableStrategies()
    {
        return StrategyTypes.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Name
        );
    }
}
