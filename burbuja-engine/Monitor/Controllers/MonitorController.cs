using Microsoft.AspNetCore.Mvc;
using BurbujaEngine.Monitor.Services;
using BurbujaEngine.Monitor.Core;
using BurbujaEngine.Engine.Core;

namespace BurbujaEngine.Monitor.Controllers;

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
    private readonly IMonitorService _monitorService;
    private readonly ILogger<MonitorController> _logger;

    public MonitorController(
        IMonitorService monitorService,
        ILogger<MonitorController> logger)
    {
        _monitorService = monitorService;
        _logger = logger;
    }

    /// <summary>
    /// Get engine health summary.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetEngineHealth()
    {
        try
        {
            var health = await _monitorService.GetEngineHealthAsync();
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
        try
        {
            var modules = await _monitorService.GetModuleHealthAsync();
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
    public async Task<IActionResult> GetSystemResources()
    {
        try
        {
            var resources = await _monitorService.GetSystemMetricsAsync();
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
    public async Task<IActionResult> GetMetrics()
    {
        try
        {
            var metrics = await _monitorService.GetSystemMetricsAsync();
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
    public async Task<IActionResult> GetRecentEvents([FromQuery] int count = 50)
    {
        try
        {
            var events = await _monitorService.GetRecentEventsAsync(count);
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
        try
        {
            var health = await _monitorService.GetEngineHealthAsync();
            
            var status = new
            {
                IsAvailable = true,
                LastUpdate = DateTime.UtcNow,
                EngineHealth = health
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
        try
        {
            var dashboardData = await _monitorService.GetDashboardDataAsync();
            return Ok(dashboardData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get dashboard data");
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}
