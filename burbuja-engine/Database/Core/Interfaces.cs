using MongoDB.Driver;
using MongoDB.Bson;

namespace BurbujaEngine.Database.Core.Interfaces;

/// <summary>
/// Interface for document models. 
/// Represents the contract that all document models must fulfill.
/// </summary>
public interface IDocumentModel
{
    /// <summary>
    /// Get the document ID.
    /// </summary>
    Guid Id { get; set; }
    
    /// <summary>
    /// Validate the document according to business rules.
    /// </summary>
    bool Validate();
}

/// <summary>
/// Strategy interface for different MongoDB connection implementations.
/// Implements the Strategy pattern for handling local MongoDB vs DocumentDB.
/// </summary>
public interface IConnectionStrategy
{
    /// <summary>
    /// Get the name of this connection strategy.
    /// </summary>
    string StrategyName { get; }
    
    /// <summary>
    /// Create and configure a MongoDB client for this strategy.
    /// </summary>
    Task<MongoClient> CreateClientAsync();
    
    /// <summary>
    /// Test if the connection is working properly.
    /// </summary>
    Task<bool> TestConnectionAsync(MongoClient client);
    
    /// <summary>
    /// Get connection information for logging/debugging.
    /// </summary>
    Dictionary<string, object> GetConnectionInfo();
}

/// <summary>
/// Interface for database connection management.
/// Provides a clean abstraction over MongoDB operations.
/// </summary>
public interface IDatabaseConnection
{
    /// <summary>
    /// Establish database connection.
    /// </summary>
    Task<bool> ConnectAsync();
    
    /// <summary>
    /// Close database connection.
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Test database connectivity.
    /// </summary>
    Task<bool> PingAsync();
    
    /// <summary>
    /// Get the database instance.
    /// </summary>
    IMongoDatabase GetDatabase();
    
    /// <summary>
    /// Get the MongoDB client instance.
    /// </summary>
    MongoClient GetClient();
    
    /// <summary>
    /// Check if currently connected to database.
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Get detailed connection information.
    /// </summary>
    Task<Dictionary<string, object>> GetConnectionInfoAsync();
}

/// <summary>
/// Factory interface for creating type-safe MongoDB collections.
/// Implements the Abstract Factory pattern.
/// </summary>
public interface ICollectionFactory
{
    /// <summary>
    /// Create a type-safe collection for the given document type.
    /// </summary>
    IDocumentCollection<T> CreateCollection<T>(string name) where T : IDocumentModel;
    
    /// <summary>
    /// Ensure indexes exist on the collection.
    /// </summary>
    Task<bool> EnsureIndexesAsync(string collectionName, List<CreateIndexModel<object>> indexes);
    
    /// <summary>
    /// Check if a collection exists.
    /// </summary>
    Task<bool> CollectionExistsAsync(string name);
    
    /// <summary>
    /// Drop a collection (use with caution).
    /// </summary>
    Task<bool> DropCollectionAsync(string name);
}

/// <summary>
/// Interface for type-safe MongoDB collection operations.
/// Provides strongly-typed CRUD operations.
/// </summary>
public interface IDocumentCollection<T> where T : IDocumentModel
{
    /// <summary>
    /// Insert a single document.
    /// </summary>
    Task<string> InsertOneAsync(T document);
    
    /// <summary>
    /// Insert multiple documents.
    /// </summary>
    Task<List<string>> InsertManyAsync(IEnumerable<T> documents);
    
    /// <summary>
    /// Find a single document matching the filter.
    /// </summary>
    Task<T?> FindOneAsync(FilterDefinition<T> filter);
    
    /// <summary>
    /// Find a document by its ID.
    /// </summary>
    Task<T?> FindByIdAsync(Guid documentId);
    
    /// <summary>
    /// Find multiple documents matching the filter.
    /// </summary>
    Task<List<T>> FindManyAsync(
        FilterDefinition<T>? filter = null,
        int? limit = null,
        int? skip = null,
        SortDefinition<T>? sort = null);
    
    /// <summary>
    /// Update a single document.
    /// </summary>
    Task<UpdateResult> UpdateOneAsync(FilterDefinition<T> filter, UpdateDefinition<T> update, bool upsert = false);
    
    /// <summary>
    /// Update a document by its ID.
    /// </summary>
    Task<UpdateResult> UpdateByIdAsync(Guid documentId, UpdateDefinition<T> update);
    
    /// <summary>
    /// Update multiple documents.
    /// </summary>
    Task<UpdateResult> UpdateManyAsync(FilterDefinition<T> filter, UpdateDefinition<T> update);
    
    /// <summary>
    /// Delete a single document.
    /// </summary>
    Task<DeleteResult> DeleteOneAsync(FilterDefinition<T> filter);
    
    /// <summary>
    /// Delete a document by its ID.
    /// </summary>
    Task<DeleteResult> DeleteByIdAsync(Guid documentId);
    
    /// <summary>
    /// Delete multiple documents.
    /// </summary>
    Task<DeleteResult> DeleteManyAsync(FilterDefinition<T> filter);
    
    /// <summary>
    /// Count documents matching the filter.
    /// </summary>
    Task<long> CountDocumentsAsync(FilterDefinition<T>? filter = null);
    
    /// <summary>
    /// Execute an aggregation pipeline.
    /// </summary>
    Task<List<BsonDocument>> AggregateAsync(PipelineDefinition<T, BsonDocument> pipeline);
    
    /// <summary>
    /// Get the collection name.
    /// </summary>
    string CollectionName { get; }
    
    /// <summary>
    /// Get the underlying MongoDB collection.
    /// </summary>
    IMongoCollection<T> GetCollection();
}

/// <summary>
/// Repository interface for domain-specific database operations.
/// Implements the Repository pattern for clean domain layer separation.
/// </summary>
public interface IRepository<T> where T : IDocumentModel
{
    /// <summary>
    /// Create a new entity.
    /// </summary>
    Task<T> CreateAsync(T entity);
    
    /// <summary>
    /// Get an entity by its ID.
    /// </summary>
    Task<T?> GetByIdAsync(Guid entityId);
    
    /// <summary>
    /// Update an existing entity.
    /// </summary>
    Task<T> UpdateAsync(T entity);
    
    /// <summary>
    /// Delete an entity by its ID.
    /// </summary>
    Task<bool> DeleteAsync(Guid entityId);
    
    /// <summary>
    /// List all entities with optional pagination.
    /// </summary>
    Task<List<T>> ListAllAsync(int? limit = null, int? skip = null);
    
    /// <summary>
    /// Check if an entity exists by its ID.
    /// </summary>
    Task<bool> ExistsAsync(Guid entityId);
    
    /// <summary>
    /// Count total number of entities.
    /// </summary>
    Task<long> CountAsync();
}

/// <summary>
/// Interface for database health checking.
/// Provides observability into database state.
/// </summary>
public interface IHealthChecker
{
    /// <summary>
    /// Perform a comprehensive health check.
    /// </summary>
    Task<Dictionary<string, object>> CheckHealthAsync();
    
    /// <summary>
    /// Check basic connectivity.
    /// </summary>
    Task<bool> CheckConnectivityAsync();
    
    /// <summary>
    /// Get database server information.
    /// </summary>
    Task<Dictionary<string, object>> GetServerInfoAsync();
    
    /// <summary>
    /// Get database statistics.
    /// </summary>
    Task<Dictionary<string, object>> GetDatabaseStatsAsync();
}

/// <summary>
/// Interface for managing database indexes.
/// Provides index creation and management capabilities.
/// </summary>
public interface IIndexManager
{
    /// <summary>
    /// Create an index on a collection.
    /// </summary>
    Task<string> CreateIndexAsync<T>(string collectionName, IndexKeysDefinition<T> indexSpec, CreateIndexOptions? options = null);
    
    /// <summary>
    /// Create multiple indexes on a collection.
    /// </summary>
    Task<IEnumerable<string>> CreateIndexesAsync<T>(string collectionName, IEnumerable<CreateIndexModel<T>> indexes);
    
    /// <summary>
    /// Drop an index from a collection.
    /// </summary>
    Task<bool> DropIndexAsync(string collectionName, string indexName);
    
    /// <summary>
    /// List all indexes on a collection.
    /// </summary>
    Task<List<BsonDocument>> ListIndexesAsync(string collectionName);
    
    /// <summary>
    /// Ensure default indexes are created for all known collections.
    /// </summary>
    Task<Dictionary<string, List<string>>> EnsureDefaultIndexesAsync();
}

/// <summary>
/// Interface for database initialization and lifecycle management.
/// Provides centralized database setup and teardown operations.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Initialize the database system.
    /// </summary>
    Task<bool> InitializeAsync();
    
    /// <summary>
    /// Shutdown the database system gracefully.
    /// </summary>
    Task ShutdownAsync();
    
    /// <summary>
    /// Get initialization status.
    /// </summary>
    bool IsInitialized { get; }
    
    /// <summary>
    /// Get initialization information.
    /// </summary>
    Dictionary<string, object> GetInitializationInfo();
}
