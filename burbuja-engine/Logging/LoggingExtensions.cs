using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace BurbujaEngine.Logging;

/// <summary>
/// Extensions for enhancing logging configuration, especially for development scenarios.
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Configures enhanced logging for development with better readability and hot reload support.
    /// </summary>
    public static ILoggingBuilder AddEnhancedDevelopmentLogging(this ILoggingBuilder builder, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            builder.ClearProviders();
            
            builder.AddConsole(options =>
            {
                options.FormatterName = "customFormatter";
            });
            
            builder.AddConsoleFormatter<DevelopmentConsoleFormatter, DevelopmentConsoleFormatterOptions>(options =>
            {
                options.TimestampFormat = "HH:mm:ss.fff ";
                options.UseUtcTimestamp = false;
                options.IncludeScopes = true;
                options.EnableColors = true;
                options.CompactFormat = true;
            });

            // Configure log levels for better development experience
            builder.SetMinimumLevel(LogLevel.Debug);
            
            // Filter out noisy logs during development
            builder.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.StaticFiles", LogLevel.Warning);
            builder.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Warning);
            builder.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.Warning);
            
            // Show detailed logs for BurbujaEngine components
            builder.AddFilter("BurbujaEngine", LogLevel.Debug);
        }
        else
        {
            // Production logging - more structured and less verbose
            builder.ClearProviders();
            builder.AddJsonConsole(options =>
            {
                options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
                options.UseUtcTimestamp = true;
            });
        }

        return builder;
    }

    /// <summary>
    /// Adds performance tracking and metrics logging for development.
    /// </summary>
    public static ILoggingBuilder AddPerformanceLogging(this ILoggingBuilder builder)
    {
        // This can be expanded to add custom performance loggers
        return builder;
    }

    /// <summary>
    /// Configures logging scopes for better context tracking.
    /// </summary>
    public static IDisposable BeginEngineScope(this ILogger logger, string engineId, string operation)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["EngineId"] = engineId,
            ["Operation"] = operation,
            ["Timestamp"] = DateTime.UtcNow
        }) ?? throw new InvalidOperationException("Failed to create logging scope");
    }

    /// <summary>
    /// Configures logging scopes for module operations.
    /// </summary>
    public static IDisposable BeginModuleScope(this ILogger logger, string moduleName, string moduleId, string operation)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["ModuleName"] = moduleName,
            ["ModuleId"] = moduleId,
            ["Operation"] = operation,
            ["Timestamp"] = DateTime.UtcNow
        }) ?? throw new InvalidOperationException("Failed to create logging scope");
    }

    /// <summary>
    /// Logs structured engine events with consistent formatting.
    /// </summary>
    public static void LogEngineEvent(this ILogger logger, LogLevel level, string engineId, string eventName, object? data = null)
    {
        using var scope = logger.BeginEngineScope(engineId, eventName);
        
        if (data != null)
        {
            logger.Log(level, "🔧 Engine Event: {EventName} | Data: {@Data}", eventName, data);
        }
        else
        {
            logger.Log(level, "🔧 Engine Event: {EventName}", eventName);
        }
    }

    /// <summary>
    /// Logs structured module events with consistent formatting.
    /// </summary>
    public static void LogModuleEvent(this ILogger logger, LogLevel level, string moduleName, string moduleId, string eventName, object? data = null)
    {
        using var scope = logger.BeginModuleScope(moduleName, moduleId, eventName);
        
        if (data != null)
        {
            logger.Log(level, "📦 Module Event: {EventName} | Data: {@Data}", eventName, data);
        }
        else
        {
            logger.Log(level, "📦 Module Event: {EventName}", eventName);
        }
    }

    /// <summary>
    /// Logs performance metrics with timing information.
    /// </summary>
    public static void LogPerformanceMetric(this ILogger logger, string operation, TimeSpan duration, object? metadata = null)
    {
        if (metadata != null)
        {
            logger.LogInformation("⚡ Performance: {Operation} completed in {Duration:F2}ms | {@Metadata}", 
                operation, duration.TotalMilliseconds, metadata);
        }
        else
        {
            logger.LogInformation("⚡ Performance: {Operation} completed in {Duration:F2}ms", 
                operation, duration.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Logs state transitions with clear visual indicators.
    /// </summary>
    public static void LogStateTransition(this ILogger logger, string component, string from, string to, string? reason = null)
    {
        var arrow = "→";
        var message = reason != null 
            ? $"🔄 State: {component} | {from} {arrow} {to} | Reason: {reason}"
            : $"🔄 State: {component} | {from} {arrow} {to}";
            
        logger.LogInformation(message);
    }
}
