using Microsoft.AspNetCore.Mvc;
using BurbujaEngine.Engine.Core;
using BurbujaEngine.Testing.StressTest;
using BurbujaEngine.Testing.SystemTest;

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
    /// Get an overview of all modules and their priorities.
    /// </summary>
    [HttpGet("overview")]
    public IActionResult GetPriorityOverview()
    {
        try
        {
            var priorities = PriorityLevelExtensions.GetAllPrioritiesInOrder()
                .Select(p => new
                {
                    level = p.ToString(),
                    value = p.ToNumericValue(),
                    category = p.GetCategoryName(),
                    description = p.GetDescription()
                });

            var modules = _engine.Modules;
            var moduleInfo = modules.Select(m => new
            {
                module_id = m.ModuleId,
                module_name = m.ModuleName,
                version = m.Version,
                state = m.State.ToString(),
                priority = m.Priority,
                is_priority_module = m is IModulePriorityModule,
                priority_level = m is IModulePriorityModule pm ? pm.ModulePriority.Level.ToString() : "Legacy"
            });

            return Ok(new
            {
                total_modules = modules.Count(),
                priority_levels = priorities,
                modules = moduleInfo
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting priority overview");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get detailed priority configuration for modules that support unified priority.
    /// </summary>
    [HttpGet("detailed")]
    public IActionResult GetDetailedPriorityInfo([FromQuery] string? context = null)
    {
        try
        {
            var modules = _engine.Modules;
            var priorityModules = modules
                .OfType<IModulePriorityModule>()
                .Select(m => new
                {
                    module_id = m.ModuleId,
                    module_name = m.ModuleName,
                    base_priority = m.ModulePriority.Level.ToString(),
                    base_value = m.ModulePriority.Level.ToNumericValue(),
                    sub_priority = m.ModulePriority.SubPriority,
                    effective_priority = m.ModulePriority.GetEffectivePriority(context),
                    context_adjustment = m.ModulePriority.ContextAdjustments.GetValueOrDefault(context, 0),
                    can_parallel = m.ModulePriority.CanParallelInitialize,
                    weight = m.ModulePriority.Weight,
                    tags = m.ModulePriority.Tags,
                    dependencies = m.ModulePriority.DependsOn
                })
                .OrderBy(m => m.effective_priority);

            return Ok(new
            {
                context = context ?? "default",
                priority_modules_count = priorityModules.Count(),
                modules = priorityModules
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting detailed priority info");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get all available priority levels and their descriptions.
    /// </summary>
    [HttpGet("levels")]
    public IActionResult GetPriorityLevels()
    {
        try
        {
            var levels = new
            {
                priority_levels = PriorityLevelExtensions.GetAllPrioritiesInOrder()
                    .Select(p => new
                    {
                        name = p.ToString(),
                        value = p.ToNumericValue(),
                        category = p.GetCategoryName(),
                        description = p.GetDescription(),
                        range = new
                        {
                            min = p.ToNumericValue(),
                            max = p.ToNumericValue() + 99
                        }
                    }),
                total_levels = Enum.GetValues<PriorityLevel>().Length,
                ordering_note = "Lower numeric values have higher priority (initialize first)"
            };

            return Ok(levels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting priority levels");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Run a priority-focused stress test.
    /// </summary>
    [HttpPost("stress-test")]
    public async Task<IActionResult> RunPriorityStressTest()
    {
        try
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var stressTestLogger = loggerFactory.CreateLogger<PriorityStressTest>();
            var stressTest = new PriorityStressTest(stressTestLogger);

            _logger.LogInformation("Starting priority stress test...");

            var result = await stressTest.RunStressTestAsync();

            return Ok(new
            {
                test_summary = new
                {
                    success = result.IsSuccessful,
                    duration = result.TotalDuration,
                    started_at = result.StartTime,
                    completed_at = result.EndTime,
                    total_tests = result.TestResults.Count,
                    error_message = result.ErrorMessage
                },
                system_metrics = new
                {
                    initial = result.InitialSystemMetrics,
                    final = result.FinalSystemMetrics,
                    delta = result.SystemMetricsDelta
                },
                test_results = result.TestResults.Select(t => new
                {
                    test_name = t.TestName,
                    success = t.IsSuccessful,
                    duration = t.Duration,
                    message = t.Message,
                    error = t.ErrorMessage,
                    metrics = t.Metrics,
                    system_impact = t.SystemMetrics
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running priority stress test");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Run the complete system test suite.
    /// </summary>
    [HttpPost("system-test")]
    public async Task<IActionResult> RunSystemTest()
    {
        try
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var systemTestLogger = loggerFactory.CreateLogger<EngineSystemTestRunner>();
            
            var testRunner = new EngineSystemTestRunner(systemTestLogger, _engine);
            var report = await testRunner.RunCompleteTestSuiteAsync();

            return Ok(new
            {
                test_summary = new
                {
                    total_tests = report.TestResults.Count,
                    passed = report.TestResults.Count(t => t.IsSuccessful),
                    failed = report.TestResults.Count(t => !t.IsSuccessful),
                    duration = report.TotalDuration,
                    started_at = report.StartTime,
                    completed_at = report.EndTime,
                    overall_success = report.IsSuccessful,
                    error_message = report.ErrorMessage
                },
                system_info = report.SystemInfo,
                metrics = new
                {
                    baseline = report.BaselineMetrics,
                    final = report.FinalMetrics
                },
                test_results = report.TestResults.Select(t => new
                {
                    test_name = t.TestName,
                    success = t.IsSuccessful,
                    duration = t.Duration,
                    message = t.Message,
                    error = t.ErrorMessage,
                    metrics = t.Metrics
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running system test");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get priority configuration for a specific module.
    /// </summary>
    [HttpGet("module/{moduleId}/priority")]
    public IActionResult GetModulePriorityConfig(Guid moduleId, [FromQuery] string? context = null)
    {
        try
        {
            var modules = _engine.Modules;
            var module = modules.FirstOrDefault(m => m.ModuleId == moduleId);

            if (module == null)
            {
                return NotFound(new { error = "Module not found" });
            }

            return Ok(GetModulePriorityInfo(module, context));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting module priority config for {ModuleId}", moduleId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Analyze priority relationships between modules.
    /// </summary>
    [HttpGet("analysis")]
    public IActionResult AnalyzePriorityRelationships([FromQuery] string? context = null)
    {
        try
        {
            var modules = _engine.Modules;
            var priorityModules = modules.OfType<IModulePriorityModule>().ToList();

            var relationships = new List<object>();

            for (int i = 0; i < priorityModules.Count; i++)
            {
                for (int j = i + 1; j < priorityModules.Count; j++)
                {
                    var moduleA = priorityModules[i];
                    var moduleB = priorityModules[j];

                    var shouldABeforeB = moduleA.ModulePriority.ShouldInitializeBefore(moduleB.ModulePriority, context);

                    relationships.Add(new
                    {
                        module_a = moduleA.ModuleName,
                        module_b = moduleB.ModuleName,
                        a_before_b = shouldABeforeB,
                        a_priority = moduleA.ModulePriority.GetEffectivePriority(context),
                        b_priority = moduleB.ModulePriority.GetEffectivePriority(context),
                        reason = shouldABeforeB ? "A has higher priority" : "B has higher priority"
                    });
                }
            }

            return Ok(new
            {
                context = context ?? "default",
                total_relationships = relationships.Count,
                relationships = relationships.OrderBy(r => ((dynamic)r).a_priority)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing priority relationships");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private object GetModulePriorityInfo(IEngineModule module, string? context)
    {
        if (module is IModulePriorityModule priorityModule)
        {
            var analysis = priorityModule.ModulePriority.Analyze(context);
            
            return new
            {
                has_config = true,
                config = new
                {
                    level = analysis.Level.ToString(),
                    level_value = analysis.Level.ToNumericValue(),
                    sub_priority = analysis.SubPriority,
                    can_parallel = analysis.CanParallelInitialize,
                    weight = analysis.Weight,
                    tags = analysis.Tags,
                    dependencies = analysis.Dependencies,
                    context_adjustments = priorityModule.ModulePriority.ContextAdjustments,
                    metadata = priorityModule.ModulePriority.Metadata,
                    effective_priority = analysis.EffectivePriority,
                    context_adjustment = analysis.ContextAdjustment,
                    category_name = analysis.CategoryName,
                    description = analysis.Description
                }
            };
        }

        return new
        {
            has_config = false,
            legacy_priority = module.Priority,
            note = "Module uses legacy priority system"
        };
    }
}
