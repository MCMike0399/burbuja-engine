using BurbujaEngine.Database.Core.Interfaces;
using BurbujaEngine.Database.Core.Exceptions;
using BurbujaEngine.Database.Collections;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace BurbujaEngine.Database.Repositories;

/// <summary>
/// Base repository implementation using the Template Method pattern.
/// Provides common CRUD operations and delegates domain-specific logic
/// to concrete implementations.
/// </summary>
public abstract class BaseRepository<T> : IRepository<T> where T : class, IDocumentModel
{
    protected readonly IDocumentCollection<T> _collection;
    protected readonly ILogger<BaseRepository<T>> _logger;
    protected readonly string _collectionName;
    
    protected BaseRepository(
        IDocumentCollection<T> collection,
        ILogger<BaseRepository<T>> logger)
    {
        _collection = collection;
        _logger = logger;
        _collectionName = collection.CollectionName;
        
        _logger.LogDebug("Created repository for {DocumentType} using collection '{CollectionName}'", 
            typeof(T).Name, _collectionName);
    }
    
    public virtual async Task<T> CreateAsync(T entity)
    {
        try
        {
            // Validate entity before creation
            if (!entity.Validate())
            {
                throw new ValidationException(
                    "Entity validation failed",
                    documentType: typeof(T).Name
                );
            }
            
            // Perform pre-creation operations
            await BeforeCreateAsync(entity);
            
            // Insert into database
            await _collection.InsertOneAsync(entity);
            
            // Perform post-creation operations
            await AfterCreateAsync(entity);
            
            _logger.LogInformation("Created {DocumentType} with ID: {EntityId}", typeof(T).Name, entity.Id);
            return entity;
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create {DocumentType}", typeof(T).Name);
            throw new DocumentException(
                $"Failed to create {typeof(T).Name}",
                entity.Id.ToString(),
                typeof(T).Name,
                ex
            );
        }
    }
    
    public virtual async Task<T?> GetByIdAsync(Guid entityId)
    {
        try
        {
            var entity = await _collection.FindByIdAsync(entityId);
            
            if (entity != null)
            {
                // Perform post-retrieval operations
                await AfterRetrieveAsync(entity);
            }
            
            return entity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get {DocumentType} by ID '{EntityId}'", typeof(T).Name, entityId);
            throw new DocumentException(
                $"Failed to get {typeof(T).Name} by ID",
                entityId.ToString(),
                typeof(T).Name,
                ex
            );
        }
    }
    
    public virtual async Task<T> UpdateAsync(T entity)
    {
        try
        {
            if (entity.Id == Guid.Empty)
            {
                throw new ValidationException(
                    "Cannot update entity without ID",
                    documentType: typeof(T).Name
                );
            }
            
            // Validate entity before update
            if (!entity.Validate())
            {
                throw new ValidationException(
                    "Entity validation failed",
                    documentType: typeof(T).Name
                );
            }
            
            // Perform pre-update operations
            await BeforeUpdateAsync(entity);
            
            // Update in database using replace
            var filter = Builders<T>.Filter.Eq(e => e.Id, entity.Id);
            var result = await _collection.UpdateOneAsync(filter, Builders<T>.Update.Set("_id", entity.Id));
            
            if (result.MatchedCount == 0)
            {
                throw new DocumentNotFoundException(
                    entity.Id.ToString(),
                    typeof(T).Name,
                    _collectionName
                );
            }
            
            // Perform post-update operations
            await AfterUpdateAsync(entity);
            
            _logger.LogInformation("Updated {DocumentType} with ID: {EntityId}", typeof(T).Name, entity.Id);
            return entity;
        }
        catch (DocumentNotFoundException)
        {
            throw;
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update {DocumentType}", typeof(T).Name);
            throw new DocumentException(
                $"Failed to update {typeof(T).Name}",
                entity.Id.ToString(),
                typeof(T).Name,
                ex
            );
        }
    }
    
    public virtual async Task<bool> DeleteAsync(Guid entityId)
    {
        try
        {
            // Get entity for pre-deletion operations
            var entity = await GetByIdAsync(entityId);
            if (entity == null)
                return false;
            
            // Perform pre-deletion operations
            await BeforeDeleteAsync(entity);
            
            // Delete from database
            var result = await _collection.DeleteByIdAsync(entityId);
            
            var success = result.DeletedCount > 0;
            
            if (success)
            {
                // Perform post-deletion operations
                await AfterDeleteAsync(entity);
                _logger.LogInformation("Deleted {DocumentType} with ID: {EntityId}", typeof(T).Name, entityId);
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {DocumentType} with ID '{EntityId}'", typeof(T).Name, entityId);
            throw new DocumentException(
                $"Failed to delete {typeof(T).Name}",
                entityId.ToString(),
                typeof(T).Name,
                ex
            );
        }
    }
    
    public virtual async Task<List<T>> ListAllAsync(int? limit = null, int? skip = null)
    {
        try
        {
            // Apply default ordering
            var sort = GetDefaultSort();
            
            var entities = await _collection.FindManyAsync(
                filter: null,
                limit: limit,
                skip: skip,
                sort: sort
            );
            
            // Perform post-retrieval operations for each entity
            foreach (var entity in entities)
            {
                await AfterRetrieveAsync(entity);
            }
            
            return entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list {DocumentType} entities", typeof(T).Name);
            throw new DocumentException(
                $"Failed to list {typeof(T).Name} entities",
                documentType: typeof(T).Name,
                originalException: ex
            );
        }
    }
    
    public virtual async Task<bool> ExistsAsync(Guid entityId)
    {
        try
        {
            var entity = await GetByIdAsync(entityId);
            return entity != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of {DocumentType} with ID '{EntityId}'", typeof(T).Name, entityId);
            throw new DocumentException(
                $"Failed to check existence of {typeof(T).Name}",
                entityId.ToString(),
                typeof(T).Name,
                ex
            );
        }
    }
    
    public virtual async Task<long> CountAsync()
    {
        try
        {
            return await _collection.CountDocumentsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count {DocumentType} entities", typeof(T).Name);
            throw new DocumentException(
                $"Failed to count {typeof(T).Name} entities",
                documentType: typeof(T).Name,
                originalException: ex
            );
        }
    }
    
    public virtual async Task<List<T>> FindByCriteriaAsync(
        FilterDefinition<T> criteria,
        int? limit = null,
        int? skip = null,
        SortDefinition<T>? sort = null)
    {
        try
        {
            // Use provided sort or default
            sort ??= GetDefaultSort();
            
            var entities = await _collection.FindManyAsync(
                filter: criteria,
                limit: limit,
                skip: skip,
                sort: sort
            );
            
            // Perform post-retrieval operations for each entity
            foreach (var entity in entities)
            {
                await AfterRetrieveAsync(entity);
            }
            
            return entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find {DocumentType} entities by criteria", typeof(T).Name);
            throw new DocumentException(
                $"Failed to find {typeof(T).Name} entities by criteria",
                documentType: typeof(T).Name,
                originalException: ex
            );
        }
    }
    
    // Template methods for subclasses to override
    
    protected virtual Task BeforeCreateAsync(T entity)
    {
        return Task.CompletedTask;
    }
    
    protected virtual Task AfterCreateAsync(T entity)
    {
        return Task.CompletedTask;
    }
    
    protected virtual Task BeforeUpdateAsync(T entity)
    {
        return Task.CompletedTask;
    }
    
    protected virtual Task AfterUpdateAsync(T entity)
    {
        return Task.CompletedTask;
    }
    
    protected virtual Task BeforeDeleteAsync(T entity)
    {
        return Task.CompletedTask;
    }
    
    protected virtual Task AfterDeleteAsync(T entity)
    {
        return Task.CompletedTask;
    }
    
    protected virtual Task AfterRetrieveAsync(T entity)
    {
        return Task.CompletedTask;
    }
    
    protected virtual SortDefinition<T>? GetDefaultSort()
    {
        // Override in subclasses to provide domain-specific sorting
        return null;
    }
    
    // Properties for introspection
    
    public string CollectionName => _collectionName;
    public Type DocumentType => typeof(T);
}
