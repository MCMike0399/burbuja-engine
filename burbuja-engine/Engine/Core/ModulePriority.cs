namespace BurbujaEngine.Engine.Core;

/// <summary>
/// Semantic priority levels for engine modules.
/// Provides a clear, extensible system for module ordering that's easy to understand and maintain.
/// </summary>
public enum PriorityLevel
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
public static class PriorityLevelExtensions
{
    /// <summary>
    /// Convert priority to numeric value for ordering.
    /// </summary>
    public static int ToNumericValue(this PriorityLevel priority)
    {
        return (int)priority;
    }
    
    /// <summary>
    /// Get the priority category name.
    /// </summary>
    public static string GetCategoryName(this PriorityLevel priority)
    {
        return priority switch
        {
            PriorityLevel.Critical => "Critical System",
            PriorityLevel.Infrastructure => "Infrastructure",
            PriorityLevel.Core => "Core Business",
            PriorityLevel.Service => "Service Layer",
            PriorityLevel.Feature => "Feature",
            PriorityLevel.Extension => "Extension",
            PriorityLevel.Presentation => "Presentation",
            PriorityLevel.Background => "Background",
            PriorityLevel.Monitoring => "Monitoring",
            PriorityLevel.Development => "Development",
            _ => "Unknown"
        };
    }
    
    /// <summary>
    /// Get a description of what modules should be in this priority level.
    /// </summary>
    public static string GetDescription(this PriorityLevel priority)
    {
        return priority switch
        {
            PriorityLevel.Critical => "System-critical modules that must be initialized first",
            PriorityLevel.Infrastructure => "Infrastructure modules that other modules depend on",
            PriorityLevel.Core => "Core business logic modules",
            PriorityLevel.Service => "Service modules that provide specific functionality",
            PriorityLevel.Feature => "Feature modules that implement specific application features",
            PriorityLevel.Extension => "Extension modules that add optional functionality",
            PriorityLevel.Presentation => "UI/Presentation layer modules",
            PriorityLevel.Background => "Background job and processing modules",
            PriorityLevel.Monitoring => "Monitoring and observability modules",
            PriorityLevel.Development => "Testing and development modules",
            _ => "Unknown priority level"
        };
    }
    
    /// <summary>
    /// Check if this priority should be initialized before another priority.
    /// </summary>
    public static bool ShouldInitializeBefore(this PriorityLevel priority, PriorityLevel other)
    {
        return priority.ToNumericValue() < other.ToNumericValue();
    }
    
    /// <summary>
    /// Get all available priorities ordered by initialization order.
    /// </summary>
    public static IEnumerable<PriorityLevel> GetAllPrioritiesInOrder()
    {
        return Enum.GetValues<PriorityLevel>().OrderBy(p => p.ToNumericValue());
    }
    
    /// <summary>
    /// Create a custom priority offset within a category.
    /// Useful for fine-tuning order within the same priority level.
    /// </summary>
    public static int WithOffset(this PriorityLevel priority, int offset)
    {
        if (offset < 0 || offset > 99)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be between 0 and 99");
            
        return priority.ToNumericValue() + offset;
    }
}

/// <summary>
/// Consolidated priority class that provides all priority management functionality.
/// This single class handles semantic priorities, sub-priorities, context adjustments, 
/// and provides comprehensive priority management for engine modules.
/// </summary>
public class ModulePriority
{
    /// <summary>
    /// The semantic priority level for this module.
    /// </summary>
    public PriorityLevel Level { get; private set; } = PriorityLevel.Core;
    
    /// <summary>
    /// Sub-priority within the priority level (0-99).
    /// Lower values have higher priority within the same level.
    /// </summary>
    public int SubPriority { get; private set; } = 0;
    
    /// <summary>
    /// Whether this module can be initialized in parallel with other modules of the same effective priority.
    /// </summary>
    public bool CanParallelInitialize { get; private set; } = true;
    
    /// <summary>
    /// Context-specific priority adjustments.
    /// Key: Context name (e.g., "Production", "Development", "Testing")
    /// Value: Priority adjustment (-50 to +50)
    /// </summary>
    public Dictionary<string, int> ContextAdjustments { get; private set; } = new();
    
    /// <summary>
    /// Tags that describe the module's role for priority grouping.
    /// </summary>
    public HashSet<string> Tags { get; private set; } = new();
    
    /// <summary>
    /// Custom priority dependencies - modules that must be initialized before this one.
    /// </summary>
    public HashSet<string> DependsOn { get; private set; } = new();
    
    /// <summary>
    /// Weight factor for priority calculation (0.1 to 2.0, default 1.0).
    /// Higher weight increases effective priority within the same level.
    /// </summary>
    public double Weight { get; private set; } = 1.0;
    
    /// <summary>
    /// Additional metadata for priority configuration.
    /// </summary>
    public Dictionary<string, object> Metadata { get; private set; } = new();
    
    // Private constructor to enforce builder pattern
    private ModulePriority() { }
    
    /// <summary>
    /// Calculate the effective priority for a given context.
    /// </summary>
    public int GetEffectivePriority(string? context = null)
    {
        var basePriority = Level.ToNumericValue() + SubPriority;
        
        if (context != null && ContextAdjustments.TryGetValue(context, out var adjustment))
        {
            basePriority += adjustment;
        }
        
        // Apply weight factor
        basePriority = (int)(basePriority * Weight);
        
        return Math.Max(0, Math.Min(999, basePriority)); // Clamp to valid range
    }
    
    /// <summary>
    /// Get the priority category name.
    /// </summary>
    public string CategoryName => Level.GetCategoryName();
    
    /// <summary>
    /// Get the priority description.
    /// </summary>
    public string Description => Level.GetDescription();
    
    /// <summary>
    /// Check if this priority should be initialized before another priority.
    /// </summary>
    public bool ShouldInitializeBefore(ModulePriority other, string? context = null)
    {
        // Check explicit dependencies first
        if (other.DependsOn.Any(dep => Tags.Contains(dep)))
        {
            return true;
        }
        
        if (DependsOn.Any(dep => other.Tags.Contains(dep)))
        {
            return false;
        }
        
        // Compare effective priorities
        var thisEffective = GetEffectivePriority(context);
        var otherEffective = other.GetEffectivePriority(context);
        
        return thisEffective < otherEffective;
    }
    
    /// <summary>
    /// Create a detailed priority analysis report.
    /// </summary>
    public PriorityAnalysis Analyze(string? context = null)
    {
        return new PriorityAnalysis
        {
            Level = Level,
            SubPriority = SubPriority,
            EffectivePriority = GetEffectivePriority(context),
            Context = context,
            ContextAdjustment = context != null ? ContextAdjustments.GetValueOrDefault(context, 0) : 0,
            Weight = Weight,
            CanParallelInitialize = CanParallelInitialize,
            Tags = Tags.ToList(),
            Dependencies = DependsOn.ToList(),
            CategoryName = CategoryName,
            Description = Description,
            Metadata = new Dictionary<string, object>(Metadata)
        };
    }
    
    /// <summary>
    /// Create a new priority builder.
    /// </summary>
    public static PriorityBuilder Create(PriorityLevel level = PriorityLevel.Core)
    {
        return new PriorityBuilder(level);
    }
    
    /// <summary>
    /// Create a simple priority with default settings.
    /// </summary>
    public static ModulePriority Simple(PriorityLevel level)
    {
        return new ModulePriority { Level = level };
    }
    
    /// <summary>
    /// Create a priority with sub-priority.
    /// </summary>
    public static ModulePriority WithSub(PriorityLevel level, int subPriority)
    {
        return new ModulePriority 
        { 
            Level = level, 
            SubPriority = Math.Max(0, Math.Min(99, subPriority))
        };
    }
    
    /// <summary>
    /// Clone this priority configuration.
    /// </summary>
    public ModulePriority Clone()
    {
        return new ModulePriority
        {
            Level = Level,
            SubPriority = SubPriority,
            CanParallelInitialize = CanParallelInitialize,
            ContextAdjustments = new Dictionary<string, int>(ContextAdjustments),
            Tags = new HashSet<string>(Tags),
            DependsOn = new HashSet<string>(DependsOn),
            Weight = Weight,
            Metadata = new Dictionary<string, object>(Metadata)
        };
    }
    
    /// <summary>
    /// Builder class for creating consolidated priority configurations.
    /// </summary>
    public class PriorityBuilder
    {
        private readonly ModulePriority _priority;
        
        internal PriorityBuilder(PriorityLevel level)
        {
            _priority = new ModulePriority { Level = level };
        }
        
        public PriorityBuilder WithSubPriority(int subPriority)
        {
            _priority.SubPriority = Math.Max(0, Math.Min(99, subPriority));
            return this;
        }
        
        public PriorityBuilder CanParallelInitialize(bool canParallel = true)
        {
            _priority.CanParallelInitialize = canParallel;
            return this;
        }
        
        public PriorityBuilder WithContextAdjustment(string context, int adjustment)
        {
            _priority.ContextAdjustments[context] = Math.Max(-50, Math.Min(50, adjustment));
            return this;
        }
        
        public PriorityBuilder WithContextAdjustments(Dictionary<string, int> adjustments)
        {
            foreach (var kvp in adjustments)
            {
                WithContextAdjustment(kvp.Key, kvp.Value);
            }
            return this;
        }
        
        public PriorityBuilder WithTag(string tag)
        {
            _priority.Tags.Add(tag);
            return this;
        }
        
        public PriorityBuilder WithTags(params string[] tags)
        {
            foreach (var tag in tags)
            {
                _priority.Tags.Add(tag);
            }
            return this;
        }
        
        public PriorityBuilder DependsOn(string dependency)
        {
            _priority.DependsOn.Add(dependency);
            return this;
        }
        
        public PriorityBuilder DependsOn(params string[] dependencies)
        {
            foreach (var dependency in dependencies)
            {
                _priority.DependsOn.Add(dependency);
            }
            return this;
        }
        
        public PriorityBuilder WithWeight(double weight)
        {
            _priority.Weight = Math.Max(0.1, Math.Min(2.0, weight));
            return this;
        }
        
        public PriorityBuilder WithMetadata(string key, object value)
        {
            _priority.Metadata[key] = value;
            return this;
        }
        
        public PriorityBuilder ForCriticalSystem()
        {
            return WithTags("critical", "system", "foundation")
                  .CanParallelInitialize(false)
                  .WithWeight(0.8); // Slightly higher priority
        }
        
        public PriorityBuilder ForInfrastructure()
        {
            return WithTags("infrastructure", "dependency")
                  .CanParallelInitialize(true)
                  .WithContextAdjustment("Production", -5); // Higher priority in production
        }
        
        public PriorityBuilder ForCoreLogic()
        {
            return WithTags("core", "business", "logic")
                  .CanParallelInitialize(true);
        }
        
        public PriorityBuilder ForService()
        {
            return WithTags("service", "api")
                  .CanParallelInitialize(true)
                  .WithContextAdjustment("Development", 10); // Lower priority in development
        }
        
        public PriorityBuilder ForFeature()
        {
            return WithTags("feature", "optional")
                  .CanParallelInitialize(true)
                  .WithContextAdjustment("Testing", 5);
        }
        
        public PriorityBuilder ForMonitoring()
        {
            return WithTags("monitoring", "observability", "metrics")
                  .CanParallelInitialize(true)
                  .WithContextAdjustment("Production", -10) // Higher priority in production
                  .WithContextAdjustment("Development", 15); // Lower priority in development
        }
        
        public ModulePriority Build()
        {
            return _priority;
        }
    }
    
    /// <summary>
    /// Priority analysis result.
    /// </summary>
    public class PriorityAnalysis
    {
        public PriorityLevel Level { get; init; }
        public int SubPriority { get; init; }
        public int EffectivePriority { get; init; }
        public string? Context { get; init; }
        public int ContextAdjustment { get; init; }
        public double Weight { get; init; }
        public bool CanParallelInitialize { get; init; }
        public List<string> Tags { get; init; } = new();
        public List<string> Dependencies { get; init; } = new();
        public string CategoryName { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public Dictionary<string, object> Metadata { get; init; } = new();
    }
}

/// <summary>
/// Interface for modules that support the new priority system.
/// </summary>
public interface IModulePriorityModule : IEngineModule
{
    /// <summary>
    /// Priority configuration for this module.
    /// </summary>
    ModulePriority ModulePriority { get; }
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
        // Check if module implements new priority interface
        if (module is IModulePriorityModule priorityModule)
        {
            return priorityModule.ModulePriority.GetEffectivePriority(_context);
        }
        
        // Fall back to legacy Priority property
        return module.Priority;
    }
}

// Legacy compatibility types
#pragma warning disable CS0618

/// <summary>
/// Legacy enum for backward compatibility. Use PriorityLevel instead.
/// </summary>
[Obsolete("Use PriorityLevel instead. This enum will be removed in a future version.")]
public enum LegacyModulePriority
{
    Critical = 0,
    Infrastructure = 100,
    Core = 200,
    Service = 300,
    Feature = 400,
    Extension = 500,
    Presentation = 600,
    Background = 700,
    Monitoring = 800,
    Development = 900
}

/// <summary>
/// Legacy priority configuration for backward compatibility.
/// </summary>
[Obsolete("Use ModulePriority class instead.")]
public record ModulePriorityConfig
{
    public LegacyModulePriority BasePriority { get; init; } = LegacyModulePriority.Core;
    public int SubPriority { get; init; } = 0;
    public bool CanParallelInitialize { get; init; } = true;
    public Dictionary<string, int> ContextAdjustments { get; init; } = new();
    public HashSet<string> Tags { get; init; } = new();
    
    public int GetEffectivePriority(string? context = null)
    {
        var priority = ModulePriority.WithSub((PriorityLevel)(int)BasePriority, SubPriority);
        return priority.GetEffectivePriority(context);
    }
    
    public static ModulePriorityConfig Default(LegacyModulePriority priority = LegacyModulePriority.Core) =>
        new() { BasePriority = priority };
    
    public static ModulePriorityConfig WithSubPriority(LegacyModulePriority priority, int subPriority) =>
        new() { BasePriority = priority, SubPriority = subPriority };
    
    public static ModulePriorityConfig WithContext(LegacyModulePriority priority, 
        Dictionary<string, int> contextAdjustments) =>
        new() { BasePriority = priority, ContextAdjustments = contextAdjustments };
}

/// <summary>
/// Legacy interface for backward compatibility.
/// </summary>
[Obsolete("Use IModulePriorityModule instead.")]
public interface IAdvancedPriorityModule : IEngineModule
{
    ModulePriorityConfig PriorityConfig { get; }
}

#pragma warning restore CS0618
