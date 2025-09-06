using BurbujaEngine.Configuration;
using BurbujaEngine.Database.Core.Interfaces;
using BurbujaEngine.Database.Connection;
using BurbujaEngine.Database.Connection.Strategies;
using BurbujaEngine.Database.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BurbujaEngine.Database.Extensions;

/// <summary>
/// Extension methods for registering database services.
/// </summary>
public static class DatabaseServiceExtensions
{
    /// <summary>
    /// Add BurbujaEngine database services to the dependency injection container.
    /// </summary>
    public static IServiceCollection AddBurbujaEngineDatabase(this IServiceCollection services)
    {
        // Register configuration
        services.AddSingleton<EnvironmentConfig>();
        
        // Register connection strategy factory
        services.AddSingleton<ConnectionStrategyFactory>();
        
        // Register connection strategies
        services.AddTransient<LocalMongoDbStrategy>();
        services.AddTransient<DocumentDbStrategy>();
        
        // Register connection manager as singleton
        services.AddSingleton<IDatabaseConnection, DatabaseConnectionManager>();
        services.AddSingleton<IHealthChecker>(provider => 
            (IHealthChecker)provider.GetRequiredService<IDatabaseConnection>());
        
        // Register collection factory
        services.AddSingleton<ICollectionFactory, DocumentCollectionFactory>();
        
        return services;
    }
    
    /// <summary>
    /// Initialize the database connection.
    /// Call this method during application startup.
    /// </summary>
    public static async Task<IServiceProvider> InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<DatabaseConnectionManager>>();
        var config = serviceProvider.GetRequiredService<EnvironmentConfig>();
        
        logger.LogInformation("[{AppName}] Initializing database connection...", config.AppName);
        
        // Log environment detection
        logger.LogInformation("[{AppName}] Environment detection:", config.AppName);
        logger.LogInformation("  - ENVIRONMENT: {Environment}", System.Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "NOT_SET");
        logger.LogInformation("  - Detected: {DetectedEnvironment}", config.Environment);
        logger.LogInformation("  - Is local: {IsLocal}", config.IsLocal);
        logger.LogInformation("  - Is production: {IsProduction}", config.IsProduction);
        
        // Validate configuration
        var validation = config.ValidateConfiguration();
        logger.LogInformation("[{AppName}] Configuration validation:", config.AppName);
        logger.LogInformation("  - Valid: {Valid}", validation.Valid);
        
        if (validation.Issues.Any())
        {
            logger.LogError("[{AppName}] Configuration issues:", config.AppName);
            foreach (var issue in validation.Issues)
            {
                logger.LogError("  - {Issue}", issue);
            }
        }
        
        if (validation.Warnings.Any())
        {
            logger.LogWarning("[{AppName}] Configuration warnings:", config.AppName);
            foreach (var warning in validation.Warnings)
            {
                logger.LogWarning("  - {Warning}", warning);
            }
        }
        
        // Log system info
        var systemInfo = config.GetSystemInfo();
        logger.LogInformation("[{AppName}] System info:", config.AppName);
        foreach (var kvp in systemInfo)
        {
            logger.LogInformation("  {Key}: {Value}", kvp.Key, kvp.Value);
        }
        
        // Log database info
        var databaseInfo = config.GetDatabaseInfo();
        logger.LogInformation("[{AppName}] Database info:", config.AppName);
        foreach (var kvp in databaseInfo)
        {
            logger.LogInformation("  {Key}: {Value}", kvp.Key, kvp.Value);
        }
        
        // Log security info
        var securityInfo = config.GetSecurityInfo();
        logger.LogInformation("[{AppName}] Security info:", config.AppName);
        foreach (var kvp in securityInfo)
        {
            logger.LogInformation("  {Key}: {Value}", kvp.Key, kvp.Value);
        }
        
        // Initialize database connection
        var databaseConnection = serviceProvider.GetRequiredService<IDatabaseConnection>();
        var connected = await databaseConnection.ConnectAsync();
        
        if (!connected)
        {
            logger.LogError("[{AppName}] Failed to establish database connection", config.AppName);
            throw new InvalidOperationException("Failed to establish database connection");
        }
        
        logger.LogInformation("[{AppName}] Database initialized successfully (mode: {Environment})", 
            config.AppName, config.Environment);
        
        return serviceProvider;
    }
}

/// <summary>
/// Extension methods for registering repositories.
/// </summary>
public static class RepositoryServiceExtensions
{
    /// <summary>
    /// Register a repository for a document type.
    /// </summary>
    public static IServiceCollection AddRepository<TRepository, TDocument>(this IServiceCollection services)
        where TRepository : class, IRepository<TDocument>
        where TDocument : class, IDocumentModel
    {
        services.AddScoped<IRepository<TDocument>, TRepository>();
        services.AddScoped<TRepository>();
        return services;
    }
    
    /// <summary>
    /// Register a typed collection for a document type.
    /// </summary>
    public static IServiceCollection AddDocumentCollection<TDocument>(this IServiceCollection services, string collectionName)
        where TDocument : class, IDocumentModel
    {
        services.AddScoped<IDocumentCollection<TDocument>>(provider =>
        {
            var factory = provider.GetRequiredService<ICollectionFactory>();
            return factory.CreateCollection<TDocument>(collectionName);
        });
        
        return services;
    }
}
