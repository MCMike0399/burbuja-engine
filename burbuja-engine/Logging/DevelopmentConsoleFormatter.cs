using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System.IO;
using System.Text;

namespace BurbujaEngine.Logging;

/// <summary>
/// Custom console formatter optimized for development with hot reload.
/// Makes logs more readable and easier to scan during development.
/// </summary>
public sealed class DevelopmentConsoleFormatter : ConsoleFormatter, IDisposable
{
    private readonly IDisposable? _optionsReloadToken;
    private DevelopmentConsoleFormatterOptions _formatterOptions;

    private static readonly string[] LogLevelColors = new[]
    {
        "\x1B[37m", // Trace - White  
        "\x1B[37m", // Debug - White
        "\x1B[32m", // Information - Green
        "\x1B[33m", // Warning - Yellow
        "\x1B[31m", // Error - Red
        "\x1B[35m", // Critical - Magenta
        "\x1B[37m"  // None - White
    };

    private static readonly string[] ComponentColors = new[]
    {
        "\x1B[36m", // Cyan for Engine
        "\x1B[94m", // Light Blue for Modules  
        "\x1B[95m", // Light Magenta for Drivers
        "\x1B[93m", // Light Yellow for Monitor
        "\x1B[92m", // Light Green for Database
        "\x1B[96m"  // Light Cyan for General
    };

    private const string ResetColor = "\x1B[39m\x1B[22m";
    private const string BoldColor = "\x1B[1m";

    public DevelopmentConsoleFormatter(IOptionsMonitor<DevelopmentConsoleFormatterOptions> options)
        : base("customFormatter")
    {
        (_optionsReloadToken, _formatterOptions) = 
            (options.OnChange(ReloadLoggerOptions), options.CurrentValue);
    }

    private void ReloadLoggerOptions(DevelopmentConsoleFormatterOptions options) =>
        _formatterOptions = options;

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        if (logEntry.State == null)
            return;

        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message))
            return;

        var sb = new StringBuilder();

        // Add timestamp with subtle styling
        if (_formatterOptions.TimestampFormat != null)
        {
            var timestamp = _formatterOptions.UseUtcTimestamp 
                ? DateTime.UtcNow.ToString(_formatterOptions.TimestampFormat)
                : DateTime.Now.ToString(_formatterOptions.TimestampFormat);
            
            sb.Append($"\x1B[90m{timestamp}\x1B[39m"); // Dark gray timestamp
        }

        // Add log level with color and compact format
        var logLevelColor = LogLevelColors[(int)logEntry.LogLevel];
        var logLevelText = GetCompactLogLevel(logEntry.LogLevel);
        sb.Append($"{logLevelColor}{BoldColor}[{logLevelText}]{ResetColor} ");

        // Determine component and add colored component badge
        var (componentName, componentColor) = DetermineComponent(logEntry.Category, message);
        if (!string.IsNullOrEmpty(componentName))
        {
            sb.Append($"{componentColor}{BoldColor}[{componentName}]{ResetColor} ");
        }

        // Process the message for better readability
        var processedMessage = ProcessMessage(message, logEntry.LogLevel);
        sb.Append(processedMessage);

        // Add exception details if present
        if (logEntry.Exception != null)
        {
            sb.AppendLine();
            AppendException(sb, logEntry.Exception);
        }

        sb.AppendLine();
        textWriter.Write(sb.ToString());
    }

    private static string GetCompactLogLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG", 
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    private static (string componentName, string color) DetermineComponent(string category, string message)
    {
        // Engine components
        if (category.Contains("BurbujaEngine.Engine.Core"))
            return ("ENGINE", ComponentColors[0]);
        
        if (category.Contains("BurbujaEngine.Engine.Modules") || message.Contains("Module"))
            return ("MODULE", ComponentColors[1]);
            
        if (category.Contains("BurbujaEngine.Engine.Drivers") || category.Contains("Driver"))
            return ("DRIVER", ComponentColors[2]);
            
        if (category.Contains("Monitor"))
            return ("MONITOR", ComponentColors[3]);
            
        if (category.Contains("Database"))
            return ("DATABASE", ComponentColors[4]);
            
        if (category.Contains("BurbujaEngine"))
            return ("BURBUJA", ComponentColors[5]);

        // ASP.NET Core components
        if (category.Contains("Microsoft.AspNetCore.Hosting"))
            return ("HOSTING", "\x1B[90m"); // Gray
            
        if (category.Contains("Microsoft.AspNetCore"))
            return ("ASPNET", "\x1B[90m"); // Gray

        return ("", "");
    }

    private static string ProcessMessage(string message, LogLevel logLevel)
    {
        // Remove redundant bracket patterns and clean up the message
        var processed = message;

        // Clean up engine/module IDs to make them more readable
        processed = System.Text.RegularExpressions.Regex.Replace(
            processed,
            @"\[([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})\]",
            match => $"\x1B[90m[{match.Groups[1].Value[..8]}]\x1B[39m" // Show only first 8 chars of GUID
        );

        // Clean up friendly IDs
        processed = System.Text.RegularExpressions.Regex.Replace(
            processed,
            @"\[([A-Za-z]+)_([a-f0-9]{8})\]",
            match => $"\x1B[90m[{match.Groups[1].Value}]\x1B[39m"
        );

        // Highlight important keywords based on log level
        if (logLevel >= LogLevel.Warning)
        {
            processed = HighlightKeywords(processed, new[] { "failed", "error", "exception", "timeout" }, "\x1B[31m"); // Red
        }

        if (logLevel == LogLevel.Information)
        {
            processed = HighlightKeywords(processed, new[] { "started", "initialized", "completed", "success" }, "\x1B[32m"); // Green
            processed = HighlightKeywords(processed, new[] { "stopping", "shutting down" }, "\x1B[33m"); // Yellow
        }

        // Highlight performance metrics
        processed = System.Text.RegularExpressions.Regex.Replace(
            processed,
            @"(\d+\.\d+ms|\d+ms)",
            match => $"\x1B[96m{match.Value}\x1B[39m" // Cyan for timing
        );

        // Highlight module counts
        processed = System.Text.RegularExpressions.Regex.Replace(
            processed,
            @"(\d+/\d+)\s+(modules?|drivers?)",
            match => $"\x1B[95m{match.Groups[1].Value}\x1B[39m {match.Groups[2].Value}"
        );

        return processed;
    }

    private static string HighlightKeywords(string text, string[] keywords, string color)
    {
        foreach (var keyword in keywords)
        {
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                $@"\b{keyword}\b",
                $"{color}{keyword}{ResetColor}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        }
        return text;
    }

    private static void AppendException(StringBuilder sb, Exception exception)
    {
        sb.Append("\x1B[31m"); // Red color for exceptions
        sb.AppendLine($"Exception: {exception.GetType().Name}: {exception.Message}");
        
        if (exception.StackTrace != null)
        {
            var lines = exception.StackTrace.Split('\n');
            foreach (var line in lines.Take(5)) // Show only first 5 stack trace lines
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine($"  {line.Trim()}");
                }
            }
            
            if (lines.Length > 5)
            {
                sb.AppendLine("  ... (additional stack trace lines omitted)");
            }
        }
        
        sb.Append(ResetColor);
    }

    public void Dispose() => _optionsReloadToken?.Dispose();
}

/// <summary>
/// Options for the development console formatter.
/// </summary>
public class DevelopmentConsoleFormatterOptions : ConsoleFormatterOptions
{
    public new bool UseUtcTimestamp { get; set; } = false;
    public new string? TimestampFormat { get; set; } = "HH:mm:ss.fff ";
    public bool EnableColors { get; set; } = true;
    public bool CompactFormat { get; set; } = true;
}
