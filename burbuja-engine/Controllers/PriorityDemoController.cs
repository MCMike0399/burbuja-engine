using Microsoft.AspNetCore.Mvc;
using BurbujaEngine.Engine.Core;
using BurbujaEngine.Testing.StressTest;

namespace BurbujaEngine.Controllers;

/// <summary>
/// Controller for demonstrating and testing the BurbujaEngine priority system.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PriorityDemoController : ControllerBase
{
    private readonly IBurbujaEngine _engine;
    private readonly ILogger<PriorityDemoController> _logger;

    public PriorityDemoController(IBurbujaEngine engine, ILogger<PriorityDemoController> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get detailed information about the priority system.
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetPrioritySystemInfo()
    {
        var priorities = ModulePriorityExtensions.GetAllPrioritiesInOrder()
            .Select(p => new
            {
                name = p.ToString(),
                value = p.ToNumericValue(),
                category = p.GetCategoryName(),
                description = p.GetDescription()
            });

        var moduleInfo = _engine.Modules.Select(m => new
        {
            module_id = m.ModuleId,
            module_name = m.ModuleName,
            version = m.Version,
            state = m.State.ToString(),
            legacy_priority = m.Priority,
            semantic_priority = GetSemanticPriority(m),
            is_advanced = m is IAdvancedPriorityModule,
            priority_details = GetPriorityDetails(m)
        });

        return Ok(new
        {
            system_info = new
            {
                version = "2.0.0",
                description = "Semantic Priority System for BurbujaEngine",
                features = new[]
                {
                    "Semantic priority levels with clear meanings",
                    "Context-aware priority adjustments",
                    "Sub-priority support for fine-tuning",
                    "Parallel initialization control",
                    "Backward compatibility with legacy priorities",
                    "Comprehensive diagnostics and monitoring"
                }
            },
            available_priorities = priorities,
            registered_modules = moduleInfo,
            initialization_order = _engine.Modules.Select((m, index) => new
            {
                order = index + 1,
                module_name = m.ModuleName,
                effective_priority = m.Priority
            })
        });
    }

    /// <summary>
    /// Demonstrate priority behavior in different contexts.
    /// </summary>
    [HttpGet("context-demo")]
    public IActionResult DemonstratePriorityContexts()
    {
        var contexts = new[] { "Development", "Testing", "Production" };
        var contextDemo = new Dictionary<string, object>();

        foreach (var context in contexts)
        {
            var modules = _engine.Modules
                .OfType<IAdvancedPriorityModule>()
                .Select(m => new
                {
                    module_name = m.ModuleName,
                    base_priority = m.PriorityConfig.BasePriority.ToString(),
                    base_value = m.PriorityConfig.BasePriority.ToNumericValue(),
                    sub_priority = m.PriorityConfig.SubPriority,
                    effective_priority = m.PriorityConfig.GetEffectivePriority(context),
                    context_adjustment = m.PriorityConfig.ContextAdjustments.GetValueOrDefault(context, 0)
                })
                .OrderBy(m => m.effective_priority)
                .ToList();

            contextDemo[context] = modules;
        }

        return Ok(new
        {
            description = "Priority behavior demonstration across different execution contexts",
            contexts = contextDemo,
            explanation = new
            {
                context_adjustments = "Modules can have different priorities based on execution context",
                effective_priority = "Base priority + sub-priority + context adjustment",
                use_cases = new[]
                {
                    "Lower monitoring priority in development",
                    "Higher database priority in production",
                    "Delayed non-critical services in testing"
                }
            }
        });
    }

    /// <summary>
    /// Compare old vs new priority system.
    /// </summary>
    [HttpGet("comparison")]
    public IActionResult ComparePrioritySystems()
    {
        return Ok(new
        {
            old_system = new
            {
                description = "Legacy integer-based priority system",
                example_values = new[] { 100, 1000, 500, 750 },
                problems = new[]
                {
                    "Arbitrary numeric values without clear meaning",
                    "Difficult to determine appropriate priority for new modules",
                    "No context awareness",
                    "Hard to maintain and understand",
                    "No semantic relationships between priorities"
                }
            },
            new_system = new
            {
                description = "Semantic priority system with context awareness",
                priority_levels = ModulePriorityExtensions.GetAllPrioritiesInOrder()
                    .Select(p => new { name = p.ToString(), value = p.ToNumericValue(), description = p.GetDescription() }),
                advantages = new[]
                {
                    "Clear semantic meaning for each priority level",
                    "Context-aware priority adjustments",
                    "Sub-priority support for fine-tuning",
                    "Self-documenting code",
                    "Extensible design for future requirements",
                    "Backward compatibility with existing code"
                }
            },
            migration_guide = new
            {
                step1 = "Inherit from AdvancedBaseEngineModule instead of BaseEngineModule",
                step2 = "Override ConfigurePriority() method",
                step3 = "Use semantic priority levels (ModulePriority enum)",
                step4 = "Add context adjustments if needed",
                step5 = "Set sub-priorities for fine-tuning within same level"
            }
        });
    }

    /// <summary>
    /// Run a lightweight stress test demonstration.
    /// </summary>
    [HttpPost("demo-test")]
    public async Task<IActionResult> RunDemoTest()
    {
        try
        {
            _logger.LogInformation("Starting priority system demonstration test...");

            // Create a simple demonstration of the priority system
            var demoResult = await CreateDemoEngine();

            return Ok(new
            {
                message = "Priority system demonstration completed successfully",
                timestamp = DateTime.UtcNow,
                demo_result = demoResult
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo test failed: {Message}", ex.Message);
            return StatusCode(500, new
            {
                error = "Demo test failed",
                message = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }

    private string GetSemanticPriority(IEngineModule module)
    {
        return module switch
        {
            IAdvancedPriorityModule advanced => advanced.PriorityConfig.BasePriority.ToString(),
            _ => "Legacy"
        };
    }

    private object GetPriorityDetails(IEngineModule module)
    {
        if (module is IAdvancedPriorityModule advanced)
        {
            return new
            {
                base_priority = advanced.PriorityConfig.BasePriority.ToString(),
                sub_priority = advanced.PriorityConfig.SubPriority,
                can_parallel_init = advanced.PriorityConfig.CanParallelInitialize,
                context_adjustments = advanced.PriorityConfig.ContextAdjustments,
                tags = advanced.PriorityConfig.Tags.ToList()
            };
        }

        return new
        {
            type = "legacy",
            priority_value = module.Priority
        };
    }

    private async Task<object> CreateDemoEngine()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        var engineId = Guid.NewGuid();
        services.AddBurbujaEngine(engineId, engine =>
        {
            engine.WithConfiguration(config =>
            {
                config.WithVersion("1.0.0-demo")
                      .WithValue("ExecutionContext", "Development")
                      .EnableParallelInitialization(true);
            });

            // Add a few mock modules for demonstration
            engine.AddModule<MockConfigurationModule>();
            engine.AddModule<MockCacheModule>();
            engine.AddModule<MockEmailServiceModule>();
        });

        var serviceProvider = services.BuildServiceProvider();

        try
        {
            var demoEngine = serviceProvider.GetRequiredService<IBurbujaEngine>();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var initResult = await demoEngine.InitializeAsync();
            var startResult = await demoEngine.StartAsync();
            stopwatch.Stop();

            var result = new
            {
                engine_id = engineId,
                initialization_success = initResult.Success,
                start_success = startResult.Success,
                total_time_ms = stopwatch.ElapsedMilliseconds,
                module_count = demoEngine.Modules.Count,
                initialization_order = demoEngine.Modules.Select((m, index) => new
                {
                    order = index + 1,
                    module_name = m.ModuleName,
                    priority = m.Priority,
                    semantic_priority = GetSemanticPriority(m)
                }).ToList()
            };

            await demoEngine.ShutdownAsync();
            return result;
        }
        finally
        {
            serviceProvider.Dispose();
        }
    }
}
