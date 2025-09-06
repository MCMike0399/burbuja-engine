using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BurbujaEngine.Engine.Core;

/// <summary>
/// MICROKERNEL PATTERN: Automatic Module Discovery
/// 
/// This service implements the microkernel principle of automatic module discovery,
/// removing the need for explicit module registration in application code.
/// 
/// DISCOVERY STRATEGIES:
/// - Assembly Scanning: Find modules in current and referenced assemblies
/// - Attribute-based: Use attributes to mark discoverable modules
/// - Convention-based: Use naming conventions to identify modules
/// - Configuration-driven: Discover modules from configuration files
/// </summary>
public interface IModuleDiscovery
{
    /// <summary>
    /// Discover all available modules in the application domain.
    /// </summary>
    Task<IEnumerable<ModuleDescriptor>> DiscoverModulesAsync();
    
    /// <summary>
    /// Discover modules from specific assemblies.
    /// </summary>
    Task<IEnumerable<ModuleDescriptor>> DiscoverModulesAsync(IEnumerable<Assembly> assemblies);
    
    /// <summary>
    /// Discover modules by convention (e.g., classes ending with "Module").
    /// </summary>
    Task<IEnumerable<ModuleDescriptor>> DiscoverModulesByConventionAsync();
    
    /// <summary>
    /// Discover modules from configuration.
    /// </summary>
    Task<IEnumerable<ModuleDescriptor>> DiscoverModulesFromConfigurationAsync();
}

/// <summary>
/// Describes a discovered module with its metadata.
/// </summary>
public class ModuleDescriptor
{
    public Type ModuleType { get; init; } = null!;
    public string ModuleName { get; init; } = null!;
    public string Version { get; init; } = "1.0.0";
    public ModulePriority Priority { get; init; } = ModulePriority.Simple(0);
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
    public bool IsEnabled { get; init; } = true;
    public string? Environment { get; init; }
    public IReadOnlyList<Type> Dependencies { get; init; } = Array.Empty<Type>();
}

/// <summary>
/// Attribute to mark modules as auto-discoverable.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class DiscoverableModuleAttribute : Attribute
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string[]? Tags { get; set; }
    public string? Environment { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 0;
}

/// <summary>
/// Default implementation of module discovery.
/// </summary>
public class DefaultModuleDiscovery : IModuleDiscovery
{
    private readonly ILogger<DefaultModuleDiscovery> _logger;
    private readonly ModuleDiscoveryOptions _options;

    public DefaultModuleDiscovery(ILogger<DefaultModuleDiscovery> logger, ModuleDiscoveryOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new ModuleDiscoveryOptions();
    }

    public async Task<IEnumerable<ModuleDescriptor>> DiscoverModulesAsync()
    {
        var allModules = new List<ModuleDescriptor>();

        // Discover from assemblies
        var assemblyModules = await DiscoverModulesAsync(GetAssembliesToScan());
        allModules.AddRange(assemblyModules);

        // Discover by convention
        if (_options.EnableConventionBasedDiscovery)
        {
            var conventionModules = await DiscoverModulesByConventionAsync();
            allModules.AddRange(conventionModules);
        }

        // Discover from configuration
        if (_options.EnableConfigurationBasedDiscovery)
        {
            var configModules = await DiscoverModulesFromConfigurationAsync();
            allModules.AddRange(configModules);
        }

        // Remove duplicates and filter
        return allModules
            .GroupBy(m => m.ModuleType)
            .Select(g => g.First())
            .Where(m => m.IsEnabled)
            .Where(m => string.IsNullOrEmpty(m.Environment) || m.Environment == _options.CurrentEnvironment);
    }

    public Task<IEnumerable<ModuleDescriptor>> DiscoverModulesAsync(IEnumerable<Assembly> assemblies)
    {
        var modules = new List<ModuleDescriptor>();

        foreach (var assembly in assemblies)
        {
            try
            {
                var moduleTypes = assembly.GetTypes()
                    .Where(type => !type.IsAbstract && !type.IsInterface)
                    .Where(type => typeof(IEngineModule).IsAssignableFrom(type))
                    .Where(type => type.GetCustomAttribute<DiscoverableModuleAttribute>() != null);

                foreach (var moduleType in moduleTypes)
                {
                    var attribute = moduleType.GetCustomAttribute<DiscoverableModuleAttribute>()!;
                    var descriptor = CreateModuleDescriptor(moduleType, attribute);
                    modules.Add(descriptor);
                    
                    _logger.LogDebug("Discovered module: {ModuleName} ({ModuleType})", 
                        descriptor.ModuleName, moduleType.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan assembly {AssemblyName} for modules", assembly.FullName);
            }
        }

        return Task.FromResult<IEnumerable<ModuleDescriptor>>(modules);
    }

    public Task<IEnumerable<ModuleDescriptor>> DiscoverModulesByConventionAsync()
    {
        var modules = new List<ModuleDescriptor>();
        
        // Convention: Classes that implement IEngineModule and end with "Module"
        var assemblies = GetAssembliesToScan();
        
        foreach (var assembly in assemblies)
        {
            try
            {
                var moduleTypes = assembly.GetTypes()
                    .Where(type => !type.IsAbstract && !type.IsInterface)
                    .Where(type => typeof(IEngineModule).IsAssignableFrom(type))
                    .Where(type => type.Name.EndsWith("Module", StringComparison.OrdinalIgnoreCase))
                    .Where(type => type.GetCustomAttribute<DiscoverableModuleAttribute>() == null); // Not already discovered

                foreach (var moduleType in moduleTypes)
                {
                    var descriptor = CreateModuleDescriptorByConvention(moduleType);
                    modules.Add(descriptor);
                    
                    _logger.LogDebug("Discovered module by convention: {ModuleName} ({ModuleType})", 
                        descriptor.ModuleName, moduleType.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan assembly {AssemblyName} for convention-based modules", 
                    assembly.FullName);
            }
        }

        return Task.FromResult<IEnumerable<ModuleDescriptor>>(modules);
    }

    public async Task<IEnumerable<ModuleDescriptor>> DiscoverModulesFromConfigurationAsync()
    {
        // This would read from appsettings.json or other configuration sources
        // For now, return empty as this is an advanced feature
        await Task.CompletedTask;
        return Array.Empty<ModuleDescriptor>();
    }

    private IEnumerable<Assembly> GetAssembliesToScan()
    {
        var assemblies = new List<Assembly>();
        
        // Current assembly
        assemblies.Add(Assembly.GetExecutingAssembly());
        
        // Entry assembly
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            assemblies.Add(entryAssembly);
        }
        
        // Load referenced assemblies that might contain modules
        foreach (var assembly in assemblies.ToList())
        {
            try
            {
                var referencedAssemblies = assembly.GetReferencedAssemblies()
                    .Where(name => _options.AssemblyFilter(name))
                    .Select(Assembly.Load)
                    .Where(a => a != null);
                    
                assemblies.AddRange(referencedAssemblies);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load referenced assemblies for {AssemblyName}", 
                    assembly.FullName);
            }
        }
        
        return assemblies.Distinct();
    }

    private ModuleDescriptor CreateModuleDescriptor(Type moduleType, DiscoverableModuleAttribute attribute)
    {
        return new ModuleDescriptor
        {
            ModuleType = moduleType,
            ModuleName = attribute.Name ?? moduleType.Name,
            Version = attribute.Version ?? "1.0.0",
            Priority = ModulePriority.Simple(PriorityLevel.Infrastructure),
            Tags = attribute.Tags?.ToList().AsReadOnly() ?? new List<string>().AsReadOnly(),
            IsEnabled = attribute.Enabled,
            Environment = attribute.Environment,
            Dependencies = ExtractDependencies(moduleType),
            Metadata = ExtractMetadata(moduleType)
        };
    }

    private ModuleDescriptor CreateModuleDescriptorByConvention(Type moduleType)
    {
        return new ModuleDescriptor
        {
            ModuleType = moduleType,
            ModuleName = moduleType.Name,
            Version = "1.0.0",
            Priority = ModulePriority.Simple(0),
            Tags = Array.Empty<string>(),
            IsEnabled = true,
            Dependencies = ExtractDependencies(moduleType),
            Metadata = ExtractMetadata(moduleType)
        };
    }

    private IReadOnlyList<Type> ExtractDependencies(Type moduleType)
    {
        // Extract dependencies from constructor parameters, interfaces, etc.
        // This is a simplified implementation
        return Array.Empty<Type>();
    }

    private IReadOnlyDictionary<string, object> ExtractMetadata(Type moduleType)
    {
        var metadata = new Dictionary<string, object>
        {
            ["assembly"] = moduleType.Assembly.FullName ?? "Unknown",
            ["namespace"] = moduleType.Namespace ?? "Unknown"
        };

        return metadata.AsReadOnly();
    }
}

/// <summary>
/// Configuration options for module discovery.
/// </summary>
public class ModuleDiscoveryOptions
{
    public bool EnableConventionBasedDiscovery { get; set; } = true;
    public bool EnableConfigurationBasedDiscovery { get; set; } = false;
    public string CurrentEnvironment { get; set; } = "Production";
    public Func<AssemblyName, bool> AssemblyFilter { get; set; } = name => 
        name.Name?.Contains("BurbujaEngine", StringComparison.OrdinalIgnoreCase) == true ||
        name.Name?.Contains("Module", StringComparison.OrdinalIgnoreCase) == true;
}
