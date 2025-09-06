using Microsoft.AspNetCore.Mvc;
using BurbujaEngine.Monitor.Core;

namespace BurbujaEngine.Monitor.Endpoints;

/// <summary>
/// Extension methods for configuring Monitor module endpoints.
/// Following clean architecture principles for route registration.
/// </summary>
public static class MonitorEndpoints
{
    /// <summary>
    /// Map all monitor endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapMonitorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Monitor dashboard route
        endpoints.MapGet("/monitor", () => Results.Redirect("/monitor/dashboard"));
        
        // Monitor dashboard page
        endpoints.MapGet("/monitor/dashboard", (HttpContext context) =>
        {
            var html = GetMonitorDashboardHtml();
            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(html);
        });

        // Monitor status endpoint for health checks
        endpoints.MapGet("/api/monitor/status", async (IMonitorService monitorService) =>
        {
            try
            {
                var health = await monitorService.GetEngineHealthAsync();
                return Results.Ok(new
                {
                    IsAvailable = true,
                    LastUpdate = DateTime.UtcNow,
                    EngineHealth = health
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });

        return endpoints;
    }

    private static string GetMonitorDashboardHtml()
    {
        return """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Burbuja Engine Monitor</title>
    <link href="/css/monitor.css" rel="stylesheet" />
    <script src="_framework/blazor.server.js"></script>
</head>
<body>
    <div id="app">
        <component type="typeof(BurbujaEngine.Monitor.Components.MonitorDashboard)" render-mode="ServerPrerendered" />
    </div>

    <div id="blazor-error-ui">
        An error has occurred. This application may no longer respond until reloaded.
        <a href="" class="reload">Reload</a>
        <a class="dismiss">🗙</a>
    </div>

    <style>
        #blazor-error-ui {
            background: lightyellow;
            bottom: 0;
            box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.2);
            display: none;
            left: 0;
            padding: 0.6rem 1.25rem 0.7rem 1.25rem;
            position: fixed;
            width: 100%;
            z-index: 1000;
        }

        #blazor-error-ui .dismiss {
            cursor: pointer;
            position: absolute;
            right: 0.75rem;
            top: 0.5rem;
        }
    </style>
</body>
</html>
""";
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
