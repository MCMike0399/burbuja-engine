using BurbujaEngine.Database.Core.Interfaces;
using BurbujaEngine.Database.Core.Exceptions;
using MongoDB.Driver;
using MongoDB.Bson;
using Microsoft.Extensions.Logging;

namespace BurbujaEngine.Database.Collections;

/// <summary>
/// Type-safe MongoDB document collection wrapper.
/// Provides strongly-typed CRUD operations with automatic validation and error handling.
/// This class abstracts MongoDB operations for domain models implementing IDocumentModel.
/// 
/// Key Features:
/// - Type safety: All operations are strongly typed to your domain model
/// - Automatic validation: Documents are validated before database operations
/// - Error handling: Comprehensive exception handling with detailed error messages
/// - Logging: Full operation logging for debugging and monitoring
/// - Performance: Efficient MongoDB operations with proper indexing support
/// 
/// Usage Example:
/// ```csharp
/// // Your domain model
/// public class User : DocumentModel 
/// {
///     public string Name { get; set; } = string.Empty;
///     public string Email { get; set; } = string.Empty;
///     
///     public override bool Validate() => !string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Email);
/// }
/// 
/// // Use the collection
/// var user = new User { Name = "John", Email = "john@example.com" };
/// await userCollection.InsertOneAsync(user);
/// var foundUser = await userCollection.FindByIdAsync(user.Id);
/// ```
/// </summary>
public class DocumentCollection<T> : IDocumentCollection<T> where T : IDocumentModel
{
    private readonly IMongoCollection<T> _mongoCollection;
    private readonly ILogger<DocumentCollection<T>> _logger;
    
    public DocumentCollection(IMongoCollection<T> mongoCollection, ILogger<DocumentCollection<T>> logger)
    {
        _mongoCollection = mongoCollection;
        _logger = logger;
        _logger.LogDebug("Created document collection '{CollectionName}' for {DocumentType}", 
            mongoCollection.CollectionNamespace.CollectionName, typeof(T).Name);
    }
    
    public string CollectionName => _mongoCollection.CollectionNamespace.CollectionName;
    
    public IMongoCollection<T> GetCollection() => _mongoCollection;
    
    public async Task<string> InsertOneAsync(T document)
    {
        try
        {
            // Validate document
            if (!document.Validate())
            {
                throw new ValidationException(
                    "Document validation failed",
                    documentType: typeof(T).Name
                );
            }
            
            await _mongoCollection.InsertOneAsync(document);
            
            _logger.LogDebug("Inserted document in '{CollectionName}': {DocumentId}", CollectionName, document.Id);
            return document.Id.ToString();
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to insert document in '{CollectionName}'",
                CollectionName,
                "insert",
                ex
            );
        }
    }
    
    public async Task<List<string>> InsertManyAsync(IEnumerable<T> documents)
    {
        try
        {
            var documentList = documents.ToList();
            if (!documentList.Any())
                return new List<string>();
            
            // Validate all documents first
            for (int i = 0; i < documentList.Count; i++)
            {
                if (!documentList[i].Validate())
                {
                    throw new ValidationException(
                        $"Document validation failed at index {i}",
                        documentType: typeof(T).Name
                    );
                }
            }
            
            await _mongoCollection.InsertManyAsync(documentList);
            
            var insertedIds = documentList.Select(d => d.Id.ToString()).ToList();
            _logger.LogDebug("Inserted {Count} documents in '{CollectionName}'", insertedIds.Count, CollectionName);
            return insertedIds;
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to insert multiple documents in '{CollectionName}'",
                CollectionName,
                "insert_many",
                ex
            );
        }
    }
    
    public async Task<T?> FindOneAsync(FilterDefinition<T> filter)
    {
        try
        {
            var result = await _mongoCollection.FindAsync(filter);
            return await result.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to find document in '{CollectionName}'",
                CollectionName,
                "find_one",
                ex
            );
        }
    }
    
    public async Task<T?> FindByIdAsync(Guid documentId)
    {
        try
        {
            var filter = Builders<T>.Filter.Eq("_id", documentId);
            return await FindOneAsync(filter);
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to find document by ID '{documentId}' in '{CollectionName}'",
                CollectionName,
                "find_by_id",
                ex
            );
        }
    }
    
    public async Task<List<T>> FindManyAsync(
        FilterDefinition<T>? filter = null, 
        int? limit = null, 
        int? skip = null, 
        SortDefinition<T>? sort = null)
    {
        try
        {
            filter ??= Builders<T>.Filter.Empty;
            
            var findOptions = new FindOptions<T>();
            if (limit.HasValue)
                findOptions.Limit = limit.Value;
            if (skip.HasValue)
                findOptions.Skip = skip.Value;
            if (sort != null)
                findOptions.Sort = sort;
            
            var result = await _mongoCollection.FindAsync(filter, findOptions);
            return await result.ToListAsync();
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to find documents in '{CollectionName}'",
                CollectionName,
                "find_many",
                ex
            );
        }
    }
    
    public async Task<UpdateResult> UpdateOneAsync(FilterDefinition<T> filter, UpdateDefinition<T> update, bool upsert = false)
    {
        try
        {
            var options = new UpdateOptions { IsUpsert = upsert };
            var result = await _mongoCollection.UpdateOneAsync(filter, update, options);
            _logger.LogDebug("Updated document in '{CollectionName}': matched={MatchedCount}, modified={ModifiedCount}", 
                CollectionName, result.MatchedCount, result.ModifiedCount);
            return result;
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to update document in '{CollectionName}'",
                CollectionName,
                "update_one",
                ex
            );
        }
    }
    
    public async Task<UpdateResult> UpdateByIdAsync(Guid documentId, UpdateDefinition<T> update)
    {
        try
        {
            var filter = Builders<T>.Filter.Eq("_id", documentId);
            var result = await UpdateOneAsync(filter, update);
            
            if (result.MatchedCount == 0)
            {
                throw new DocumentNotFoundException(
                    documentId.ToString(),
                    typeof(T).Name,
                    CollectionName
                );
            }
            
            return result;
        }
        catch (DocumentNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to update document by ID '{documentId}' in '{CollectionName}'",
                CollectionName,
                "update_by_id",
                ex
            );
        }
    }
    
    public async Task<UpdateResult> UpdateManyAsync(FilterDefinition<T> filter, UpdateDefinition<T> update)
    {
        try
        {
            var result = await _mongoCollection.UpdateManyAsync(filter, update);
            _logger.LogDebug("Updated documents in '{CollectionName}': matched={MatchedCount}, modified={ModifiedCount}", 
                CollectionName, result.MatchedCount, result.ModifiedCount);
            return result;
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to update documents in '{CollectionName}'",
                CollectionName,
                "update_many",
                ex
            );
        }
    }
    
    public async Task<DeleteResult> DeleteOneAsync(FilterDefinition<T> filter)
    {
        try
        {
            var result = await _mongoCollection.DeleteOneAsync(filter);
            _logger.LogDebug("Deleted document in '{CollectionName}': count={DeletedCount}", 
                CollectionName, result.DeletedCount);
            return result;
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to delete document in '{CollectionName}'",
                CollectionName,
                "delete_one",
                ex
            );
        }
    }
    
    public async Task<DeleteResult> DeleteByIdAsync(Guid documentId)
    {
        try
        {
            var filter = Builders<T>.Filter.Eq("_id", documentId);
            var result = await DeleteOneAsync(filter);
            
            if (result.DeletedCount == 0)
            {
                throw new DocumentNotFoundException(
                    documentId.ToString(),
                    typeof(T).Name,
                    CollectionName
                );
            }
            
            return result;
        }
        catch (DocumentNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to delete document by ID '{documentId}' in '{CollectionName}'",
                CollectionName,
                "delete_by_id",
                ex
            );
        }
    }
    
    public async Task<DeleteResult> DeleteManyAsync(FilterDefinition<T> filter)
    {
        try
        {
            var result = await _mongoCollection.DeleteManyAsync(filter);
            _logger.LogDebug("Deleted documents in '{CollectionName}': count={DeletedCount}", 
                CollectionName, result.DeletedCount);
            return result;
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to delete documents in '{CollectionName}'",
                CollectionName,
                "delete_many",
                ex
            );
        }
    }
    
    public async Task<long> CountDocumentsAsync(FilterDefinition<T>? filter = null)
    {
        try
        {
            filter ??= Builders<T>.Filter.Empty;
            return await _mongoCollection.CountDocumentsAsync(filter);
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to count documents in '{CollectionName}'",
                CollectionName,
                "count",
                ex
            );
        }
    }
    
    public async Task<List<BsonDocument>> AggregateAsync(PipelineDefinition<T, BsonDocument> pipeline)
    {
        try
        {
            var result = await _mongoCollection.AggregateAsync(pipeline);
            return await result.ToListAsync();
        }
        catch (Exception ex)
        {
            throw new CollectionException(
                $"Failed to execute aggregation in '{CollectionName}'",
                CollectionName,
                "aggregate",
                ex
            );
        }
    }
}
