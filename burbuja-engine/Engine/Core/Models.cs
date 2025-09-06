using System.Diagnostics;

namespace BurbujaEngine.Engine.Core;

/// <summary>
/// States that an engine module can be in.
/// </summary>
public enum ModuleState
{
    /// <summary>
    /// Module has been created but not yet initialized.
    /// </summary>
    Created,
    
    /// <summary>
    /// Module is currently being initialized.
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Module has been successfully initialized.
    /// </summary>
    Initialized,
    
    /// <summary>
    /// Module is currently starting up.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Module is running and operational.
    /// </summary>
    Running,
    
    /// <summary>
    /// Module is currently stopping.
    /// </summary>
    Stopping,
    
    /// <summary>
    /// Module has been stopped.
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Module is shutting down.
    /// </summary>
    ShuttingDown,
    
    /// <summary>
    /// Module has been shut down.
    /// </summary>
    Shutdown,
    
    /// <summary>
    /// Module is in an error state.
    /// </summary>
    Error,
    
    /// <summary>
    /// Module has been disposed.
    /// </summary>
    Disposed
}

/// <summary>
/// States that the engine can be in.
/// </summary>
public enum EngineState
{
    /// <summary>
    /// Engine has been created but not yet initialized.
    /// </summary>
    Created,
    
    /// <summary>
    /// Engine is currently initializing modules.
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Engine has been initialized but not started.
    /// </summary>
    Initialized,
    
    /// <summary>
    /// Engine is currently starting up.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Engine is running and operational.
    /// </summary>
    Running,
    
    /// <summary>
    /// Engine is currently stopping.
    /// </summary>
    Stopping,
    
    /// <summary>
    /// Engine has been stopped.
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Engine is shutting down.
    /// </summary>
    ShuttingDown,
    
    /// <summary>
    /// Engine has been shut down.
    /// </summary>
    Shutdown,
    
    /// <summary>
    /// Engine is in an error state.
    /// </summary>
    Error,
    
    /// <summary>
    /// Engine has been disposed.
    /// </summary>
    Disposed
}

/// <summary>
/// Result of a module operation.
/// </summary>
public record ModuleResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan Duration { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    public static ModuleResult Successful(string? message = null, TimeSpan duration = default) =>
        new() { Success = true, Message = message, Duration = duration };
    
    public static ModuleResult Failed(string message, Exception? exception = null, TimeSpan duration = default) =>
        new() { Success = false, Message = message, Exception = exception, Duration = duration };
    
    public static ModuleResult Failed(Exception exception, TimeSpan duration = default) =>
        new() { Success = false, Message = exception.Message, Exception = exception, Duration = duration };
}

/// <summary>
/// Result of an engine operation.
/// </summary>
public record EngineResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan Duration { get; init; }
    public Dictionary<Guid, ModuleResult> ModuleResults { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    public static EngineResult Successful(string? message = null, TimeSpan duration = default) =>
        new() { Success = true, Message = message, Duration = duration };
    
    public static EngineResult Failed(string message, Exception? exception = null, TimeSpan duration = default) =>
        new() { Success = false, Message = message, Exception = exception, Duration = duration };
    
    public static EngineResult Failed(Exception exception, TimeSpan duration = default) =>
        new() { Success = false, Message = exception.Message, Exception = exception, Duration = duration };
    
    public EngineResult WithModuleResults(Dictionary<Guid, ModuleResult> moduleResults)
    {
        foreach (var kvp in moduleResults)
        {
            ModuleResults[kvp.Key] = kvp.Value;
        }
        return this;
    }
}

/// <summary>
/// Health status levels.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// Healthy and operating normally.
    /// </summary>
    Healthy,
    
    /// <summary>
    /// Operating but with warnings.
    /// </summary>
    Warning,
    
    /// <summary>
    /// Critical issues but still operational.
    /// </summary>
    Critical,
    
    /// <summary>
    /// Not operational.
    /// </summary>
    Unhealthy,
    
    /// <summary>
    /// Health status unknown.
    /// </summary>
    Unknown
}

/// <summary>
/// Health information for a module.
/// </summary>
public record ModuleHealth
{
    public Guid ModuleId { get; init; } = Guid.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public HealthStatus Status { get; init; }
    public string? Message { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan ResponseTime { get; init; }
    public Dictionary<string, object> Details { get; init; } = new();
    public List<string> Issues { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    
    public static ModuleHealth Healthy(Guid moduleId, string moduleName, string? message = null) =>
        new() { ModuleId = moduleId, ModuleName = moduleName, Status = HealthStatus.Healthy, Message = message };
    
    public static ModuleHealth Warning(Guid moduleId, string moduleName, string message, IEnumerable<string>? warnings = null) =>
        new() 
        { 
            ModuleId = moduleId, 
            ModuleName = moduleName, 
            Status = HealthStatus.Warning, 
            Message = message,
            Warnings = warnings?.ToList() ?? new()
        };
    
    public static ModuleHealth Critical(Guid moduleId, string moduleName, string message, IEnumerable<string>? issues = null) =>
        new() 
        { 
            ModuleId = moduleId, 
            ModuleName = moduleName, 
            Status = HealthStatus.Critical, 
            Message = message,
            Issues = issues?.ToList() ?? new()
        };
    
    public static ModuleHealth Unhealthy(Guid moduleId, string moduleName, string message, IEnumerable<string>? issues = null) =>
        new() 
        { 
            ModuleId = moduleId, 
            ModuleName = moduleName, 
            Status = HealthStatus.Unhealthy, 
            Message = message,
            Issues = issues?.ToList() ?? new()
        };
}

/// <summary>
/// Health information for the engine.
/// </summary>
public record EngineHealth
{
    public Guid EngineId { get; init; } = Guid.Empty;
    public HealthStatus Status { get; init; }
    public string? Message { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan ResponseTime { get; init; }
    public Dictionary<Guid, ModuleHealth> ModuleHealth { get; init; } = new();
    public Dictionary<string, object> Details { get; init; } = new();
    
    public static EngineHealth FromModules(Guid engineId, IEnumerable<ModuleHealth> moduleHealths)
    {
        var moduleHealthDict = moduleHealths.ToDictionary(h => h.ModuleId, h => h);
        var overallStatus = DetermineOverallStatus(moduleHealths);
        
        return new EngineHealth
        {
            EngineId = engineId,
            Status = overallStatus,
            ModuleHealth = moduleHealthDict,
            Message = GenerateOverallMessage(overallStatus, moduleHealths)
        };
    }
    
    private static HealthStatus DetermineOverallStatus(IEnumerable<ModuleHealth> moduleHealths)
    {
        if (!moduleHealths.Any()) return HealthStatus.Unknown;
        
        var statuses = moduleHealths.Select(h => h.Status).ToList();
        
        if (statuses.Any(s => s == HealthStatus.Unhealthy)) return HealthStatus.Unhealthy;
        if (statuses.Any(s => s == HealthStatus.Critical)) return HealthStatus.Critical;
        if (statuses.Any(s => s == HealthStatus.Warning)) return HealthStatus.Warning;
        if (statuses.All(s => s == HealthStatus.Healthy)) return HealthStatus.Healthy;
        
        return HealthStatus.Unknown;
    }
    
    private static string GenerateOverallMessage(HealthStatus status, IEnumerable<ModuleHealth> moduleHealths)
    {
        var moduleCount = moduleHealths.Count();
        return status switch
        {
            HealthStatus.Healthy => $"All {moduleCount} modules are healthy",
            HealthStatus.Warning => $"Engine operational with warnings ({moduleCount} modules)",
            HealthStatus.Critical => $"Engine has critical issues ({moduleCount} modules)",
            HealthStatus.Unhealthy => $"Engine is unhealthy ({moduleCount} modules)",
            _ => $"Engine status unknown ({moduleCount} modules)"
        };
    }
}

/// <summary>
/// Diagnostic information for a module.
/// </summary>
public record ModuleDiagnostics
{
    public Guid ModuleId { get; init; } = Guid.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public ModuleState State { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? InitializedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public TimeSpan? Uptime { get; init; }
    public Dictionary<string, object> Performance { get; init; } = new();
    public Dictionary<string, object> Configuration { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
    public List<Guid> Dependencies { get; init; } = new();
    public List<LogEntry> RecentLogs { get; init; } = new();
}

/// <summary>
/// Diagnostic information for the engine.
/// </summary>
public record EngineDiagnostics
{
    public Guid EngineId { get; init; } = Guid.Empty;
    public string Version { get; init; } = string.Empty;
    public EngineState State { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? InitializedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public TimeSpan? Uptime { get; init; }
    public int ModuleCount { get; init; }
    public Dictionary<Guid, ModuleDiagnostics> ModuleDiagnostics { get; init; } = new();
    public Dictionary<string, object> Performance { get; init; } = new();
    public Dictionary<string, object> Configuration { get; init; } = new();
    public Dictionary<string, object> Environment { get; init; } = new();
    public Process? Process { get; init; }
}

/// <summary>
/// Log entry for diagnostics.
/// </summary>
public record LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Category { get; init; }
    public Exception? Exception { get; init; }
    public Dictionary<string, object> Properties { get; init; } = new();
}

/// <summary>
/// Engine configuration.
/// </summary>
public record EngineConfiguration : IEngineConfiguration
{
    public Guid EngineId { get; init; } = Guid.Empty;
    public string Version { get; init; } = "1.0.0";
    public IReadOnlyDictionary<string, object> Values { get; init; } = new Dictionary<string, object>();
    public TimeSpan ModuleTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public bool ContinueOnModuleFailure { get; init; } = false;
    public bool EnableParallelInitialization { get; init; } = true;
    
    public static EngineConfiguration Default(Guid engineId) =>
        new() { EngineId = engineId };
        
    public static EngineConfiguration Create(Guid engineId, Action<EngineConfigurationBuilder>? configure = null)
    {
        var builder = new EngineConfigurationBuilder(engineId);
        configure?.Invoke(builder);
        return builder.Build();
    }
}

/// <summary>
/// Builder for engine configuration.
/// </summary>
public class EngineConfigurationBuilder
{
    private readonly Guid _engineId;
    private string _version = "1.0.0";
    private readonly Dictionary<string, object> _values = new();
    private TimeSpan _moduleTimeout = TimeSpan.FromMinutes(5);
    private TimeSpan _shutdownTimeout = TimeSpan.FromMinutes(2);
    private bool _continueOnModuleFailure = false;
    private bool _enableParallelInitialization = true;
    
    public EngineConfigurationBuilder(Guid engineId)
    {
        _engineId = engineId;
    }
    
    public EngineConfigurationBuilder WithVersion(string version)
    {
        _version = version;
        return this;
    }
    
    public EngineConfigurationBuilder WithValue(string key, object value)
    {
        _values[key] = value;
        return this;
    }
    
    public EngineConfigurationBuilder WithModuleTimeout(TimeSpan timeout)
    {
        _moduleTimeout = timeout;
        return this;
    }
    
    public EngineConfigurationBuilder WithShutdownTimeout(TimeSpan timeout)
    {
        _shutdownTimeout = timeout;
        return this;
    }
    
    public EngineConfigurationBuilder ContinueOnModuleFailure(bool continueOnFailure = true)
    {
        _continueOnModuleFailure = continueOnFailure;
        return this;
    }
    
    public EngineConfigurationBuilder EnableParallelInitialization(bool enable = true)
    {
        _enableParallelInitialization = enable;
        return this;
    }
    
    public EngineConfiguration Build() =>
        new()
        {
            EngineId = _engineId,
            Version = _version,
            Values = _values.AsReadOnly(),
            ModuleTimeout = _moduleTimeout,
            ShutdownTimeout = _shutdownTimeout,
            ContinueOnModuleFailure = _continueOnModuleFailure,
            EnableParallelInitialization = _enableParallelInitialization
        };
}

/// <summary>
/// Event arguments for module state changes.
/// </summary>
public class ModuleStateChangedEventArgs : EventArgs
{
    public Guid ModuleId { get; }
    public string ModuleName { get; }
    public ModuleState PreviousState { get; }
    public ModuleState NewState { get; }
    public DateTime Timestamp { get; }
    public string? Message { get; }
    
    public ModuleStateChangedEventArgs(Guid moduleId, string moduleName, ModuleState previousState, ModuleState newState, string? message = null)
    {
        ModuleId = moduleId;
        ModuleName = moduleName;
        PreviousState = previousState;
        NewState = newState;
        Timestamp = DateTime.UtcNow;
        Message = message;
    }
}

/// <summary>
/// Event arguments for engine state changes.
/// </summary>
public class EngineStateChangedEventArgs : EventArgs
{
    public Guid EngineId { get; }
    public EngineState PreviousState { get; }
    public EngineState NewState { get; }
    public DateTime Timestamp { get; }
    public string? Message { get; }
    
    public EngineStateChangedEventArgs(Guid engineId, EngineState previousState, EngineState newState, string? message = null)
    {
        EngineId = engineId;
        PreviousState = previousState;
        NewState = newState;
        Timestamp = DateTime.UtcNow;
        Message = message;
    }
}

/// <summary>
/// Extension methods for dictionaries to make them read-only.
/// </summary>
public static class DictionaryExtensions
{
    public static IReadOnlyDictionary<TKey, TValue> AsReadOnly<TKey, TValue>(this Dictionary<TKey, TValue> dictionary)
        where TKey : notnull =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>(dictionary);
}
