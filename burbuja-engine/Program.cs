using BurbujaEngine.Configuration;
using BurbujaEngine.Engine.Extensions;
using BurbujaEngine.Engine.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS
builder.Services.AddCors();

// Register EnvironmentConfig
builder.Services.AddSingleton<EnvironmentConfig>();

// Add BurbujaEngine with module registration 
builder.Services.AddBurbujaEngine(Guid.NewGuid())
    .WithConfiguration(config =>
    {
        config.WithVersion("1.0.0")
              .WithModuleTimeout(TimeSpan.FromMinutes(2))
              .WithShutdownTimeout(TimeSpan.FromMinutes(1))
              .ContinueOnModuleFailure(false)
              .EnableParallelInitialization(true);
    })
    .AddDatabaseModule()  
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
app.UseAuthorization();
app.MapControllers();

// Map engine endpoints for monitoring and diagnostics
app.MapEngineEndpoints("/engine");

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
