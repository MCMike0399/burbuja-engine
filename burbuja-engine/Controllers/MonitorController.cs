using Microsoft.AspNetCore.Mvc;
using BurbujaEngine.Engine.Modules;
using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Controllers;

/// <summary>
/// Controller for the Monitor module web interface.
/// 
/// MICROKERNEL PATTERN: User-Space Service Exposure
/// 
/// This controller demonstrates how user-space services (like the Monitor module)
/// can expose their functionality through web APIs without affecting the microkernel:
/// 
/// - Clean separation: Web layer doesn't couple with microkernel internals
/// - Service discovery: Gets monitor module through dependency injection
/// - RESTful API: Provides standard HTTP endpoints for monitoring data
/// - Real-time support: Enables SignalR for live updates if needed
/// 
/// This exemplifies how the microkernel architecture enables flexible
/// service exposure patterns while maintaining architectural boundaries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MonitorController : ControllerBase
{
    private readonly MonitorModule? _monitorModule;
    private readonly IBurbujaEngine? _engine;
    private readonly ILogger<MonitorController> _logger;

    public MonitorController(
        IServiceProvider serviceProvider,
        ILogger<MonitorController> logger)
    {
        _logger = logger;
        
        // Try to get monitor module from DI first
        _monitorModule = serviceProvider.GetService<MonitorModule>();
        
        // If not found, try to get it from the engine
        if (_monitorModule == null)
        {
            _engine = serviceProvider.GetService<IBurbujaEngine>();
            _monitorModule = _engine?.GetModule<MonitorModule>();
        }
        
        if (_monitorModule == null)
        {
            _logger.LogWarning("Monitor module not available in controller");
        }
    }

    /// <summary>
    /// Get engine health summary.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetEngineHealth()
    {
        if (_monitorModule == null)
        {
            return NotFound("Monitor module not available");
        }

        try
        {
            var health = await _monitorModule.GetEngineHealthSummary();
            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get engine health");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Get all modules information.
    /// </summary>
    [HttpGet("modules")]
    public async Task<IActionResult> GetModules()
    {
        if (_monitorModule == null)
        {
            return NotFound("Monitor module not available");
        }

        try
        {
            var modules = await _monitorModule.GetModuleInformation();
            return Ok(modules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get modules information");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Get system resource usage.
    /// </summary>
    [HttpGet("resources")]
    public IActionResult GetSystemResources()
    {
        if (_monitorModule == null)
        {
            return NotFound("Monitor module not available");
        }

        try
        {
            var resources = _monitorModule.GetSystemResourceUsage();
            return Ok(resources);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system resources");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Get all current metrics.
    /// </summary>
    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        if (_monitorModule == null)
        {
            return NotFound("Monitor module not available");
        }

        try
        {
            var metrics = _monitorModule.GetMetrics();
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Get recent monitor events.
    /// </summary>
    [HttpGet("events")]
    public IActionResult GetRecentEvents([FromQuery] int count = 50)
    {
        if (_monitorModule == null)
        {
            return NotFound("Monitor module not available");
        }

        try
        {
            var events = _monitorModule.GetRecentEvents(count);
            return Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent events");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Get monitor module status.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetMonitorStatus()
    {
        if (_monitorModule == null)
        {
            return NotFound("Monitor module not available");
        }

        try
        {
            var health = await _monitorModule.GetHealthAsync();
            var diagnostics = await _monitorModule.GetDiagnosticsAsync();
            
            var status = new
            {
                ModuleId = _monitorModule.ModuleId,
                ModuleName = _monitorModule.ModuleName,
                Version = _monitorModule.Version,
                State = _monitorModule.State.ToString(),
                Health = health,
                Diagnostics = diagnostics,
                TotalMetricsCollected = _monitorModule.GetMetric<long?>("TotalMetricsCollected"),
                TotalHealthChecks = _monitorModule.GetMetric<long?>("TotalHealthChecks"),
                TotalErrors = _monitorModule.GetMetric<long?>("TotalErrors")
            };

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get monitor status");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Get comprehensive dashboard data.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardData()
    {
        if (_monitorModule == null)
        {
            return NotFound("Monitor module not available");
        }

        try
        {
            // Collect all dashboard data in parallel
            var healthTask = _monitorModule.GetEngineHealthSummary();
            var modulesTask = _monitorModule.GetModuleInformation();

            await Task.WhenAll(healthTask, modulesTask);

            var dashboardData = new
            {
                EngineHealth = await healthTask,
                Modules = await modulesTask,
                SystemResources = _monitorModule.GetSystemResourceUsage(),
                Metrics = _monitorModule.GetMetrics(),
                RecentEvents = _monitorModule.GetRecentEvents(20),
                LastUpdated = DateTime.UtcNow
            };

            return Ok(dashboardData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get dashboard data");
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}

/// <summary>
/// Controller for serving the Blazor monitor page.
/// </summary>
[Route("monitor")]
public class MonitorPageController : Controller
{
    /// <summary>
    /// Serve the monitor dashboard page.
    /// </summary>
    public IActionResult Index()
    {
        return View("~/Views/Monitor/Index.cshtml");
    }
}
