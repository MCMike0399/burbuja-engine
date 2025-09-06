namespace BurbujaEngine.Engine.Core;

/// <summary>
/// Semantic priority levels for engine modules.
/// Provides a clear, extensible system for module ordering that's easy to understand and maintain.
/// </summary>
public enum ModulePriority
{
    /// <summary>
    /// System-critical modules that must be initialized first.
    /// Examples: Logging, Configuration, Security Foundation
    /// Numeric Value: 0-99
    /// </summary>
    Critical = 0,
    
    /// <summary>
    /// Infrastructure modules that other modules depend on.
    /// Examples: Database, Message Queue, Cache, Authentication
    /// Numeric Value: 100-199
    /// </summary>
    Infrastructure = 100,
    
    /// <summary>
    /// Core business logic modules.
    /// Examples: User Management, Business Rules, Core APIs
    /// Numeric Value: 200-299
    /// </summary>
    Core = 200,
    
    /// <summary>
    /// Service modules that provide specific functionality.
    /// Examples: Email Service, File Upload, Payment Processing
    /// Numeric Value: 300-399
    /// </summary>
    Service = 300,
    
    /// <summary>
    /// Feature modules that implement specific application features.
    /// Examples: Reporting, Analytics, Notifications
    /// Numeric Value: 400-499
    /// </summary>
    Feature = 400,
    
    /// <summary>
    /// Extension modules that add optional functionality.
    /// Examples: Plugins, Third-party Integrations, Experimental Features
    /// Numeric Value: 500-599
    /// </summary>
    Extension = 500,
    
    /// <summary>
    /// UI/Presentation layer modules.
    /// Examples: Web UI, API Documentation, Dashboard
    /// Numeric Value: 600-699
    /// </summary>
    Presentation = 600,
    
    /// <summary>
    /// Background job and processing modules.
    /// Examples: Schedulers, Background Workers, Data Processing
    /// Numeric Value: 700-799
    /// </summary>
    Background = 700,
    
    /// <summary>
    /// Monitoring and observability modules.
    /// Examples: Health Checks, Metrics, Tracing, Diagnostics
    /// Numeric Value: 800-899
    /// </summary>
    Monitoring = 800,
    
    /// <summary>
    /// Testing and development modules.
    /// Examples: Test Data Generators, Mock Services, Development Tools
    /// Numeric Value: 900-999
    /// </summary>
    Development = 900
}

/// <summary>
/// Extensions for working with module priorities.
/// Provides utility methods for priority manipulation and querying.
/// </summary>
public static class ModulePriorityExtensions
{
    /// <summary>
    /// Convert priority to numeric value for ordering.
    /// </summary>
    public static int ToNumericValue(this ModulePriority priority)
    {
        return (int)priority;
    }
    
    /// <summary>
    /// Get the priority category name.
    /// </summary>
    public static string GetCategoryName(this ModulePriority priority)
    {
        return priority switch
        {
            ModulePriority.Critical => "Critical System",
            ModulePriority.Infrastructure => "Infrastructure",
            ModulePriority.Core => "Core Business",
            ModulePriority.Service => "Service Layer",
            ModulePriority.Feature => "Feature",
            ModulePriority.Extension => "Extension",
            ModulePriority.Presentation => "Presentation",
            ModulePriority.Background => "Background",
            ModulePriority.Monitoring => "Monitoring",
            ModulePriority.Development => "Development",
            _ => "Unknown"
        };
    }
    
    /// <summary>
    /// Get a description of what modules should be in this priority level.
    /// </summary>
    public static string GetDescription(this ModulePriority priority)
    {
        return priority switch
        {
            ModulePriority.Critical => "System-critical modules that must be initialized first",
            ModulePriority.Infrastructure => "Infrastructure modules that other modules depend on",
            ModulePriority.Core => "Core business logic modules",
            ModulePriority.Service => "Service modules that provide specific functionality",
            ModulePriority.Feature => "Feature modules that implement specific application features",
            ModulePriority.Extension => "Extension modules that add optional functionality",
            ModulePriority.Presentation => "UI/Presentation layer modules",
            ModulePriority.Background => "Background job and processing modules",
            ModulePriority.Monitoring => "Monitoring and observability modules",
            ModulePriority.Development => "Testing and development modules",
            _ => "Unknown priority level"
        };
    }
    
    /// <summary>
    /// Check if this priority should be initialized before another priority.
    /// </summary>
    public static bool ShouldInitializeBefore(this ModulePriority priority, ModulePriority other)
    {
        return priority.ToNumericValue() < other.ToNumericValue();
    }
    
    /// <summary>
    /// Get all available priorities ordered by initialization order.
    /// </summary>
    public static IEnumerable<ModulePriority> GetAllPrioritiesInOrder()
    {
        return Enum.GetValues<ModulePriority>().OrderBy(p => p.ToNumericValue());
    }
    
    /// <summary>
    /// Create a custom priority offset within a category.
    /// Useful for fine-tuning order within the same priority level.
    /// </summary>
    public static int WithOffset(this ModulePriority priority, int offset)
    {
        if (offset < 0 || offset > 99)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be between 0 and 99");
            
        return priority.ToNumericValue() + offset;
    }
}

/// <summary>
/// Advanced priority configuration for modules that need more complex priority logic.
/// Allows for sub-priorities and contextual priority adjustments.
/// </summary>
public record ModulePriorityConfig
{
    /// <summary>
    /// The base priority level for this module.
    /// </summary>
    public ModulePriority BasePriority { get; init; } = ModulePriority.Core;
    
    /// <summary>
    /// Sub-priority within the base priority level (0-99).
    /// Lower values have higher priority within the same base priority.
    /// </summary>
    public int SubPriority { get; init; } = 0;
    
    /// <summary>
    /// Whether this module can be initialized in parallel with other modules of the same effective priority.
    /// </summary>
    public bool CanParallelInitialize { get; init; } = true;
    
    /// <summary>
    /// Context-specific priority adjustments.
    /// Key: Context name (e.g., "Production", "Development", "Testing")
    /// Value: Priority adjustment (-50 to +50)
    /// </summary>
    public Dictionary<string, int> ContextAdjustments { get; init; } = new();
    
    /// <summary>
    /// Tags that describe the module's role for priority grouping.
    /// </summary>
    public HashSet<string> Tags { get; init; } = new();
    
    /// <summary>
    /// Calculate the effective priority for a given context.
    /// </summary>
    public int GetEffectivePriority(string? context = null)
    {
        var basePriority = BasePriority.ToNumericValue() + SubPriority;
        
        if (context != null && ContextAdjustments.TryGetValue(context, out var adjustment))
        {
            basePriority += adjustment;
        }
        
        return Math.Max(0, Math.Min(999, basePriority)); // Clamp to valid range
    }
    
    /// <summary>
    /// Create a default priority configuration.
    /// </summary>
    public static ModulePriorityConfig Default(ModulePriority priority = ModulePriority.Core) =>
        new() { BasePriority = priority };
    
    /// <summary>
    /// Create a priority configuration with sub-priority.
    /// </summary>
    public static ModulePriorityConfig WithSubPriority(ModulePriority priority, int subPriority) =>
        new() { BasePriority = priority, SubPriority = subPriority };
    
    /// <summary>
    /// Create a priority configuration with context adjustments.
    /// </summary>
    public static ModulePriorityConfig WithContext(ModulePriority priority, 
        Dictionary<string, int> contextAdjustments) =>
        new() { BasePriority = priority, ContextAdjustments = contextAdjustments };
}

/// <summary>
/// Priority comparison helper for sorting modules.
/// </summary>
public class ModulePriorityComparer : IComparer<IEngineModule>
{
    private readonly string? _context;
    
    public ModulePriorityComparer(string? context = null)
    {
        _context = context;
    }
    
    public int Compare(IEngineModule? x, IEngineModule? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return 1;
        if (y == null) return -1;
        
        var xPriority = GetEffectivePriority(x);
        var yPriority = GetEffectivePriority(y);
        
        // First compare by effective priority
        var priorityComparison = xPriority.CompareTo(yPriority);
        if (priorityComparison != 0) return priorityComparison;
        
        // If priorities are equal, compare by module name for deterministic ordering
        return string.Compare(x.ModuleName, y.ModuleName, StringComparison.OrdinalIgnoreCase);
    }
    
    private int GetEffectivePriority(IEngineModule module)
    {
        // Check if module implements advanced priority interface
        if (module is IAdvancedPriorityModule advancedModule)
        {
            return advancedModule.PriorityConfig.GetEffectivePriority(_context);
        }
        
        // Fall back to legacy Priority property
        return module.Priority;
    }
}

/// <summary>
/// Interface for modules that support advanced priority configuration.
/// </summary>
public interface IAdvancedPriorityModule : IEngineModule
{
    /// <summary>
    /// Advanced priority configuration for this module.
    /// </summary>
    ModulePriorityConfig PriorityConfig { get; }
}
