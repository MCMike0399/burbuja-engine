namespace BurbujaEngine.Database.Core.Exceptions;

/// <summary>
/// Base exception for all database-related errors.
/// Provides a common interface for all database exceptions.
/// </summary>
public class DatabaseException : Exception
{
    public Dictionary<string, object> Details { get; }
    public Exception? OriginalException { get; }
    
    public DatabaseException(
        string message, 
        Dictionary<string, object>? details = null,
        Exception? originalException = null
    ) : base(message)
    {
        Details = details ?? new Dictionary<string, object>();
        OriginalException = originalException;
    }
    
    public Dictionary<string, object> GetErrorInfo()
    {
        return new Dictionary<string, object>
        {
            ["error_type"] = GetType().Name,
            ["message"] = Message,
            ["details"] = Details,
            ["original_error"] = OriginalException?.Message ?? "N/A"
        };
    }
}

/// <summary>
/// Exception raised when database connection fails.
/// Indicates issues with establishing or maintaining database connectivity.
/// </summary>
public class ConnectionException : DatabaseException
{
    public ConnectionException(
        string message = "Database connection failed",
        string? host = null,
        int? port = null,
        string? database = null,
        Exception? originalException = null
    ) : base(message, BuildDetails(host, port, database), originalException)
    {
    }
    
    private static Dictionary<string, object> BuildDetails(string? host, int? port, string? database)
    {
        var details = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(host))
            details["host"] = host;
        if (port.HasValue)
            details["port"] = port.Value;
        if (!string.IsNullOrEmpty(database))
            details["database"] = database;
        return details;
    }
}

/// <summary>
/// Exception raised when database connection times out.
/// Specific type of connection error for timeout scenarios.
/// </summary>
public class ConnectionTimeoutException : ConnectionException
{
    public ConnectionTimeoutException(
        double? timeoutSeconds = null,
        string? host = null,
        int? port = null,
        Exception? originalException = null
    ) : base(
        timeoutSeconds.HasValue ? $"Database connection timed out after {timeoutSeconds} seconds" : "Database connection timed out",
        host,
        port,
        null,
        originalException
    )
    {
        if (timeoutSeconds.HasValue)
            Details["timeout_seconds"] = timeoutSeconds.Value;
    }
}

/// <summary>
/// Exception raised when database authentication fails.
/// Specific type of connection error for authentication issues.
/// </summary>
public class AuthenticationException : ConnectionException
{
    public AuthenticationException(
        string? username = null,
        string? database = null,
        Exception? originalException = null
    ) : base(
        BuildMessage(username, database),
        null,
        null,
        database,
        originalException
    )
    {
    }
    
    private static string BuildMessage(string? username, string? database)
    {
        var message = "Database authentication failed";
        if (!string.IsNullOrEmpty(username))
            message += $" for user '{username}'";
        if (!string.IsNullOrEmpty(database))
            message += $" on database '{database}'";
        return message;
    }
}

/// <summary>
/// Exception raised for collection-related errors.
/// Indicates issues with collection operations or configuration.
/// </summary>
public class CollectionException : DatabaseException
{
    public CollectionException(
        string message,
        string? collectionName = null,
        string? operation = null,
        Exception? originalException = null
    ) : base(message, BuildDetails(collectionName, operation), originalException)
    {
    }
    
    private static Dictionary<string, object> BuildDetails(string? collectionName, string? operation)
    {
        var details = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(collectionName))
            details["collection"] = collectionName;
        if (!string.IsNullOrEmpty(operation))
            details["operation"] = operation;
        return details;
    }
}

/// <summary>
/// Base exception for document-related errors.
/// Indicates issues with document operations or validation.
/// </summary>
public class DocumentException : DatabaseException
{
    public DocumentException(
        string message,
        string? documentId = null,
        string? documentType = null,
        Exception? originalException = null
    ) : base(message, BuildDetails(documentId, documentType), originalException)
    {
    }
    
    private static Dictionary<string, object> BuildDetails(string? documentId, string? documentType)
    {
        var details = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(documentId))
            details["document_id"] = documentId;
        if (!string.IsNullOrEmpty(documentType))
            details["document_type"] = documentType;
        return details;
    }
}

/// <summary>
/// Exception raised when a requested document is not found.
/// Specific type of document error for missing documents.
/// </summary>
public class DocumentNotFoundException : DocumentException
{
    public DocumentNotFoundException(
        string? documentId = null,
        string? documentType = null,
        string? collectionName = null
    ) : base(
        BuildMessage(documentId, documentType, collectionName),
        documentId,
        documentType
    )
    {
        if (!string.IsNullOrEmpty(collectionName))
            Details["collection"] = collectionName;
    }
    
    private static string BuildMessage(string? documentId, string? documentType, string? collectionName)
    {
        string message;
        if (!string.IsNullOrEmpty(documentId) && !string.IsNullOrEmpty(documentType))
            message = $"{documentType} with ID '{documentId}' not found";
        else if (!string.IsNullOrEmpty(documentId))
            message = $"Document with ID '{documentId}' not found";
        else if (!string.IsNullOrEmpty(documentType))
            message = $"{documentType} not found";
        else
            message = "Document not found";
            
        if (!string.IsNullOrEmpty(collectionName))
            message += $" in collection '{collectionName}'";
            
        return message;
    }
}

/// <summary>
/// Exception raised when attempting to create a duplicate document.
/// Specific type of document error for uniqueness constraint violations.
/// </summary>
public class DuplicateDocumentException : DocumentException
{
    public DuplicateDocumentException(
        string? fieldName = null,
        object? fieldValue = null,
        string? documentType = null,
        string? collectionName = null,
        Exception? originalException = null
    ) : base(
        BuildMessage(fieldName, fieldValue, documentType, collectionName),
        null,
        documentType,
        originalException
    )
    {
        if (!string.IsNullOrEmpty(fieldName))
            Details["field_name"] = fieldName;
        if (fieldValue != null)
            Details["field_value"] = fieldValue.ToString() ?? "";
        if (!string.IsNullOrEmpty(collectionName))
            Details["collection"] = collectionName;
    }
    
    private static string BuildMessage(string? fieldName, object? fieldValue, string? documentType, string? collectionName)
    {
        string message;
        if (!string.IsNullOrEmpty(fieldName) && fieldValue != null)
            message = $"Document with {fieldName}='{fieldValue}' already exists";
        else if (!string.IsNullOrEmpty(documentType))
            message = $"Duplicate {documentType} document";
        else
            message = "Duplicate document";
            
        if (!string.IsNullOrEmpty(collectionName))
            message += $" in collection '{collectionName}'";
            
        return message;
    }
}

/// <summary>
/// Exception raised when document validation fails.
/// Specific type of document error for validation issues.
/// </summary>
public class ValidationException : DocumentException
{
    public Dictionary<string, string> ValidationErrors { get; }
    
    public ValidationException(
        string message = "Document validation failed",
        Dictionary<string, string>? validationErrors = null,
        string? documentType = null,
        Exception? originalException = null
    ) : base(message, null, documentType, originalException)
    {
        ValidationErrors = validationErrors ?? new Dictionary<string, string>();
        if (ValidationErrors.Count > 0)
            Details["validation_errors"] = ValidationErrors;
    }
}

/// <summary>
/// Exception raised for index-related errors.
/// Specific type of collection error for index operations.
/// </summary>
public class IndexException : CollectionException
{
    public IndexException(
        string message,
        string? collectionName = null,
        string? indexName = null,
        Dictionary<string, object>? indexSpec = null,
        Exception? originalException = null
    ) : base(message, collectionName, "index_operation", originalException)
    {
        if (!string.IsNullOrEmpty(indexName))
            Details["index_name"] = indexName;
        if (indexSpec != null)
            Details["index_spec"] = indexSpec;
    }
}

/// <summary>
/// Exception raised for query-related errors.
/// Indicates issues with database query execution or syntax.
/// </summary>
public class QueryException : DatabaseException
{
    public QueryException(
        string message = "Database query failed",
        Dictionary<string, object>? query = null,
        string? collectionName = null,
        string? operationType = null,
        Exception? originalException = null
    ) : base(message, BuildDetails(query, collectionName, operationType), originalException)
    {
    }
    
    private static Dictionary<string, object> BuildDetails(
        Dictionary<string, object>? query, 
        string? collectionName, 
        string? operationType)
    {
        var details = new Dictionary<string, object>();
        if (query != null)
            details["query"] = query;
        if (!string.IsNullOrEmpty(collectionName))
            details["collection"] = collectionName;
        if (!string.IsNullOrEmpty(operationType))
            details["operation_type"] = operationType;
        return details;
    }
}

/// <summary>
/// Exception raised for document serialization/deserialization errors.
/// Indicates issues with converting between C# objects and database documents.
/// </summary>
public class SerializationException : DatabaseException
{
    public SerializationException(
        string message = "Document serialization failed",
        string? documentType = null,
        string? fieldName = null,
        string operation = "serialization",
        Exception? originalException = null
    ) : base(message, BuildDetails(documentType, fieldName, operation), originalException)
    {
    }
    
    private static Dictionary<string, object> BuildDetails(string? documentType, string? fieldName, string operation)
    {
        var details = new Dictionary<string, object>
        {
            ["operation"] = operation
        };
        if (!string.IsNullOrEmpty(documentType))
            details["document_type"] = documentType;
        if (!string.IsNullOrEmpty(fieldName))
            details["field_name"] = fieldName;
        return details;
    }
}

/// <summary>
/// Exception raised during health check operations.
/// Indicates issues with database health monitoring.
/// </summary>
public class HealthCheckException : DatabaseException
{
    public HealthCheckException(
        string message = "Database health check failed",
        string? checkType = null,
        Exception? originalException = null
    ) : base(message, BuildDetails(checkType), originalException)
    {
    }
    
    private static Dictionary<string, object> BuildDetails(string? checkType)
    {
        var details = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(checkType))
            details["check_type"] = checkType;
        return details;
    }
}
