using BurbujaEngine.Configuration;
using BurbujaEngine.Engine.Extensions;
using BurbujaEngine.Engine.Core;
using BurbujaEngine.Engine.Modules;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Blazor services for real-time monitoring
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add CORS
builder.Services.AddCors();

// Register EnvironmentConfig
builder.Services.AddSingleton<EnvironmentConfig>();

// Add BurbujaEngine with module registration 
// MICROKERNEL PRINCIPLE: Fault Isolation - Engine continues running even if modules fail
builder.Services.AddBurbujaEngine(Guid.NewGuid())
    .WithConfiguration(config =>
    {
        config.WithVersion("1.0.0")
              .WithModuleTimeout(TimeSpan.FromMinutes(2))
              .WithShutdownTimeout(TimeSpan.FromMinutes(1))
              .ContinueOnModuleFailure(true)  
              .EnableParallelInitialization(true);
    })
    .AddDatabaseModule()
    .AddMonitorModule()   // Add the new Monitor module
    .BuildEngine();       // Build the engine with all configured modules

var app = builder.Build();

// Get configuration for CORS setup
var config = app.Services.GetRequiredService<EnvironmentConfig>();

// Configure CORS
app.UseCors(corsBuilder =>
{
    corsBuilder
        .WithOrigins(config.CorsOrigins)
        .WithMethods(config.CorsAllowMethods)
        .WithHeaders(config.CorsAllowHeaders);
    
    if (config.CorsAllowCredentials)
        corsBuilder.AllowCredentials();
});

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // Add static files support for CSS
app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapBlazorHub(); // Add Blazor hub for real-time updates

// Map engine endpoints for monitoring and diagnostics
app.MapEngineEndpoints("/engine");

// Monitor dashboard route
app.MapGet("/monitor", () => Results.Redirect("/monitor/dashboard"));
app.MapGet("/monitor/dashboard", (HttpContext context) =>
{
    // Serve the monitor dashboard HTML directly
    var html = """
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
        <component type="typeof(BurbujaEngine.Components.Monitor.MonitorDashboard)" render-mode="ServerPrerendered" />
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
    context.Response.ContentType = "text/html";
    return context.Response.WriteAsync(html);
});

// Monitor API endpoints for real-time data
app.MapGet("/api/monitor/status", (IServiceProvider serviceProvider) =>
{
    try
    {
        var monitorModule = serviceProvider.GetService<MonitorModule>();
        if (monitorModule == null)
        {
            return Results.NotFound("Monitor module not available");
        }

        return Results.Ok(new
        {
            ModuleId = monitorModule.ModuleId,
            ModuleName = monitorModule.ModuleName,
            State = monitorModule.State.ToString(),
            IsAvailable = true,
            LastUpdate = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

// Stress test endpoint
app.MapPost("/engine/stress-test", async (IServiceProvider serviceProvider) =>
{
    try
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<BurbujaEngine.Testing.StressTest.PriorityStressTest>();
        var stressTest = new BurbujaEngine.Testing.StressTest.PriorityStressTest(logger);
        
        logger.LogInformation("Starting priority system stress test via API...");
        
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var report = await stressTest.RunStressTestAsync(cts.Token);
        
        return Results.Ok(new
        {
            message = "Stress test completed",
            success = report.IsSuccessful,
            duration_ms = report.TotalDuration.TotalMilliseconds,
            start_time = report.StartTime,
            end_time = report.EndTime,
            error_message = report.ErrorMessage,
            test_results = report.TestResults.Select(t => new
            {
                test_name = t.TestName,
                success = t.IsSuccessful,
                duration_ms = t.Duration.TotalMilliseconds,
                message = t.Message,
                error = t.ErrorMessage,
                metrics = t.Metrics
            }),
            summary = new
            {
                total_tests = report.TestResults.Count,
                passed_tests = report.TestResults.Count(t => t.IsSuccessful),
                failed_tests = report.TestResults.Count(t => !t.IsSuccessful)
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Stress Test Failed",
            detail: ex.Message,
            statusCode: 500);
    }
})
.WithName("RunStressTest");

// Priority system demonstration endpoint
app.MapGet("/engine/priorities", (IBurbujaEngine engine) =>
{
    var priorities = BurbujaEngine.Engine.Core.PriorityLevelExtensions.GetAllPrioritiesInOrder()
        .Select(p => new
        {
            name = p.ToString(),
            value = p.ToNumericValue(),
            category = p.GetCategoryName(),
            description = p.GetDescription()
        });
    
    var moduleInfo = engine.Modules.Select(m => new
    {
        module_id = m.ModuleId,
        friendly_id = m.FriendlyId,
        module_name = m.ModuleName,
        legacy_priority = m.Priority,
        semantic_priority = m is BurbujaEngine.Engine.Core.BaseEngineModule baseModule 
            ? baseModule.ModulePriorityLevel.ToString() 
            : "Unknown",
        state = m.State.ToString()
    });
    
    return Results.Ok(new
    {
        available_priorities = priorities,
        registered_modules = moduleInfo,
        priority_system_info = new
        {
            version = "2.0.0",
            features = new[]
            {
                "Semantic priority levels",
                "Context-aware adjustments", 
                "Sub-priority support",
                "Parallel initialization control",
                "Backward compatibility"
            }
        }
    });
})
.WithName("PrioritySystemInfo");

// Basic info endpoint
app.MapGet("/", (EnvironmentConfig config) =>
{
    var systemInfo = config.GetSystemInfo();
    return Results.Ok(new
    {
        message = "BurbujaEngine API",
        version = systemInfo["app_version"],
        environment = systemInfo["environment"],
        status = "running",
        engine = new
        {
            health_endpoint = "/engine/health",
            diagnostics_endpoint = "/engine/diagnostics",
            status_endpoint = "/engine/status"
        }
    });
});

app.Run();
