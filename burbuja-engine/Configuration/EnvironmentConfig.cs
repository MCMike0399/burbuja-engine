using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BurbujaEngine.Configuration;

/// <summary>
/// Environment configuration manager for BurbujaEngine.
/// Provides centralized configuration management with essential settings only.
/// </summary>
public class EnvironmentConfig
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EnvironmentConfig> _logger;
    
    public EnvironmentConfig(IConfiguration configuration, ILogger<EnvironmentConfig> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _logger.LogInformation("[ENV_CONFIG] Loading configuration for BurbujaEngine...");
    }

    // ============= Basic Application Config =============

    public int AppPort => GetInt("APP_PORT", 8000);
    public string AppHost => GetString("APP_HOST", "0.0.0.0");
    public string AppName => GetString("APP_NAME", "BurbujaEngine API");
    public string AppVersion => GetString("APP_VERSION", "1.0.0");
    public string LogLevel => GetString("LOG_LEVEL", "Information");

    // ============= Environment Detection =============

    public string Environment
    {
        get
        {
            var explicitEnv = GetString("ENVIRONMENT");
            if (!string.IsNullOrEmpty(explicitEnv))
                return explicitEnv.ToLowerInvariant();

            // Auto-detect based on environment indicators
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("ECS_TASK_ARN")))
                return "production";
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AWS_EXECUTION_ENV")))
                return "production";
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AWS_REGION")))
                return "production";
            
            return "local";
        }
    }

    public bool IsLocal => Environment.ToLowerInvariant() is "local" or "development" or "dev";
    public bool IsProduction => Environment.ToLowerInvariant() is "production" or "prod";

    // ============= MongoDB Configuration =============

    public string MongoDbHost => GetString("MONGODB_HOST", IsLocal ? "localhost" : "mongodb");
    public int MongoDbPort => GetInt("MONGODB_PORT", 27017);
    public string MongoDbDatabase => GetString("MONGODB_DATABASE", "burbuja_engine");
    public string MongoDbUsername => GetString("MONGODB_USERNAME", "");
    public string MongoDbPassword => GetString("MONGODB_PASSWORD", "");
    public string MongoDbType => GetString("MONGODB_TYPE", "mongodb");
    public int MongoDbConnectionTimeout => GetInt("MONGODB_CONNECTION_TIMEOUT", 5000);
    public int MongoDbServerSelectionTimeout => GetInt("MONGODB_SERVER_SELECTION_TIMEOUT", 5000);
    public bool MongoDbSslEnabled => GetBool("MONGODB_SSL_ENABLED", MongoDbType == "documentdb");
    public string MongoDbReplicaSet => GetString("MONGODB_REPLICA_SET", "");
    public int MongoDbMaxPoolSize => GetInt("MONGODB_MAX_POOL_SIZE", IsLocal ? 20 : 100);
    public int MongoDbMinPoolSize => GetInt("MONGODB_MIN_POOL_SIZE", IsLocal ? 5 : 10);
    public string MongoDbSslCaFile => GetString("MONGODB_SSL_CA_FILE", "");

    public string MongoDbUrl
    {
        get
        {
            // Check if a full URL is provided
            var url = GetString("MONGODB_URL");
            if (!string.IsNullOrEmpty(url))
                return url;

            // Build URL from components
            var auth = "";
            if (!string.IsNullOrEmpty(MongoDbUsername) && !string.IsNullOrEmpty(MongoDbPassword))
                auth = $"{MongoDbUsername}:{MongoDbPassword}@";

            var baseUrl = $"mongodb://{auth}{MongoDbHost}:{MongoDbPort}/{MongoDbDatabase}";

            // Add query parameters
            var parameters = new List<string>();

            if (MongoDbSslEnabled)
                parameters.Add("ssl=true");

            if (!string.IsNullOrEmpty(MongoDbReplicaSet))
                parameters.Add($"replicaSet={MongoDbReplicaSet}");

            parameters.Add($"connectTimeoutMS={MongoDbConnectionTimeout}");
            parameters.Add($"serverSelectionTimeoutMS={MongoDbServerSelectionTimeout}");

            if (parameters.Count > 0)
                baseUrl += "?" + string.Join("&", parameters);

            return baseUrl;
        }
    }

    // ============= CORS Configuration =============

    public string[] CorsOrigins
    {
        get
        {
            var originsStr = GetString("CORS_ORIGINS");
            if (string.IsNullOrEmpty(originsStr))
            {
                if (IsLocal)
                {
                    return new[]
                    {
                        "http://localhost:3000",
                        "http://localhost:8080",
                        "http://127.0.0.1:3000",
                        "http://127.0.0.1:8080"
                    };
                }
                return Array.Empty<string>();
            }
            
            return originsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                           .Select(o => o.Trim())
                           .Where(o => !string.IsNullOrEmpty(o))
                           .ToArray();
        }
    }

    public bool CorsAllowCredentials => GetBool("CORS_ALLOW_CREDENTIALS", true);
    public string[] CorsAllowMethods => GetStringArray("CORS_ALLOW_METHODS", new[] { "*" });
    public string[] CorsAllowHeaders => GetStringArray("CORS_ALLOW_HEADERS", new[] { "*" });

    // ============= Utility Methods =============

    public Dictionary<string, object> GetSystemInfo()
    {
        return new Dictionary<string, object>
        {
            ["app_name"] = AppName,
            ["app_version"] = AppVersion,
            ["environment"] = Environment,
            ["is_local"] = IsLocal,
            ["is_production"] = IsProduction,
            ["host"] = AppHost,
            ["port"] = AppPort,
            ["log_level"] = LogLevel
        };
    }

    public Dictionary<string, object> GetDatabaseInfo()
    {
        return new Dictionary<string, object>
        {
            ["type"] = MongoDbType,
            ["host"] = MongoDbHost,
            ["port"] = MongoDbPort,
            ["database"] = MongoDbDatabase,
            ["ssl_enabled"] = MongoDbSslEnabled,
            ["replica_set"] = MongoDbReplicaSet,
            ["connection_timeout"] = MongoDbConnectionTimeout
        };
    }

    public Dictionary<string, object> GetSecurityInfo()
    {
        return new Dictionary<string, object>
        {
            ["cors_enabled"] = CorsOrigins.Length > 0,
            ["cors_origins_count"] = CorsOrigins.Length,
            ["cors_allow_credentials"] = CorsAllowCredentials,
            ["ssl_enabled"] = MongoDbSslEnabled
        };
    }

    public (bool Valid, List<string> Issues, List<string> Warnings) ValidateConfiguration()
    {
        var issues = new List<string>();
        var warnings = new List<string>();

        // Critical validations
        if (string.IsNullOrEmpty(MongoDbHost))
            issues.Add("MongoDB host is required");

        if (MongoDbPort <= 0 || MongoDbPort > 65535)
            issues.Add("MongoDB port must be between 1 and 65535");

        if (string.IsNullOrEmpty(MongoDbDatabase))
            issues.Add("MongoDB database name is required");

        // Warnings
        if (IsProduction && !MongoDbSslEnabled)
            warnings.Add("SSL is recommended for production environments");

        if (IsLocal && MongoDbSslEnabled && string.IsNullOrEmpty(MongoDbSslCaFile))
            warnings.Add("SSL is enabled but no CA file specified");

        return (issues.Count == 0, issues, warnings);
    }

    // Helper methods for configuration access
    private string GetString(string key, string defaultValue = "")
    {
        return _configuration[key] ?? defaultValue;
    }

    private int GetInt(string key, int defaultValue = 0)
    {
        var value = _configuration[key];
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    private bool GetBool(string key, bool defaultValue = false)
    {
        var value = _configuration[key];
        if (string.IsNullOrEmpty(value))
            return defaultValue;
        
        return value.ToLowerInvariant() is "true" or "1" or "yes" or "on" or "enabled";
    }

    private string[] GetStringArray(string key, string[] defaultValue)
    {
        var value = _configuration[key];
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .Where(s => !string.IsNullOrEmpty(s))
                   .ToArray();
    }
}
