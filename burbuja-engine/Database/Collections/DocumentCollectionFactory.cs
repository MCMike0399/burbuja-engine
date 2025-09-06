using BurbujaEngine.Database.Core.Interfaces;
using BurbujaEngine.Database.Core.Exceptions;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace BurbujaEngine.Database.Collections;

/// <summary>
/// Extensible factory for creating type-safe document collections.
/// 
/// Purpose: Provides developers with a flexible factory to create collections for their domain models.
/// The factory abstracts MongoDB complexity and provides type-safe collections with:
/// - Automatic validation
/// - Error handling and logging
/// - CRUD operations abstraction
/// - Index management
/// 
/// Usage Example:
/// 
/// // Define your domain model
/// public class User : DocumentModel 
/// {
///     public string Name { get; set; } = string.Empty;
///     public string Email { get; set; } = string.Empty;
///     
///     public override bool Validate() => !string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Email);
/// }
/// 
/// // Create a collection for your model
/// var userCollection = collectionFactory.CreateCollection<User>("users");
/// 
/// // Use the collection with full type safety
/// var user = new User { Name = "John", Email = "john@example.com" };
/// await userCollection.InsertOneAsync(user);
/// 
/// This design allows developers to focus on their business logic while the factory handles:
/// - MongoDB connection management
/// - Type safety and serialization
/// - Error handling and validation
/// - Performance optimizations
/// </summary>
public class DocumentCollectionFactory : ICollectionFactory
{
    private readonly IDatabaseConnection _databaseConnection;
    private readonly ILogger<DocumentCollectionFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, object> _collections = new();
    
    public DocumentCollectionFactory(
        IDatabaseConnection databaseConnection, 
        ILogger<DocumentCollectionFactory> logger,
        ILoggerFactory loggerFactory)
    {
        _databaseConnection = databaseConnection;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }
    
    /// <summary>
    /// Create a type-safe collection for any domain model implementing IDocumentModel.
    /// This method provides full extensibility for developers to create collections for their business needs.
    /// 
    /// Features:
    /// - Type safety: Operations are strongly typed to your domain model
    /// - Automatic validation: Documents are validated before database operations
    /// - Error handling: Comprehensive exception handling with detailed error messages
    /// - Performance: Collections are cached and reused for efficiency
    /// - Logging: Full operation logging for debugging and monitoring
    /// </summary>
    /// <typeparam name="T">Your domain model type that implements IDocumentModel</typeparam>
    /// <param name="name">The MongoDB collection name (can be different from your class name)</param>
    /// <returns>A type-safe collection wrapper for CRUD operations</returns>
    public IDocumentCollection<T> CreateCollection<T>(string name) where T : IDocumentModel
    {
        var collectionKey = $"{name}:{typeof(T).Name}";
        
        if (!_collections.ContainsKey(collectionKey))
        {
            _logger.LogInformation("Creating new document collection: {CollectionName} for {DocumentType}", 
                name, typeof(T).Name);
            
            var database = _databaseConnection.GetDatabase();
            var mongoCollection = database.GetCollection<T>(name);
            var collectionLogger = _loggerFactory.CreateLogger<DocumentCollection<T>>();
            var collection = new DocumentCollection<T>(mongoCollection, collectionLogger);
            
            _collections[collectionKey] = collection;
        }
        
        return (IDocumentCollection<T>)_collections[collectionKey];
    }
    
    public async Task<bool> EnsureIndexesAsync(string collectionName, List<CreateIndexModel<object>> indexes)
    {
        try
        {
            var database = _databaseConnection.GetDatabase();
            var collection = database.GetCollection<object>(collectionName);
            
            if (indexes.Any())
            {
                var result = await collection.Indexes.CreateManyAsync(indexes);
                _logger.LogInformation("Created {IndexCount} indexes on collection '{CollectionName}'", 
                    result.Count(), collectionName);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            throw new IndexException(
                $"Failed to create indexes on collection '{collectionName}'",
                collectionName,
                originalException: ex
            );
        }
    }
    
    public async Task<bool> CollectionExistsAsync(string name)
    {
        try
        {
            var database = _databaseConnection.GetDatabase();
            var collections = await (await database.ListCollectionNamesAsync()).ToListAsync();
            return collections.Contains(name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if collection '{CollectionName}' exists", name);
            return false;
        }
    }
    
    public async Task<bool> DropCollectionAsync(string name)
    {
        try
        {
            var database = _databaseConnection.GetDatabase();
            await database.DropCollectionAsync(name);
            _logger.LogWarning("Dropped collection '{CollectionName}'", name);
            return true;
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to drop collection '{name}'",
                name,
                "drop",
                ex
            );
        }
    }
}
