using BurbujaEngine.Configuration;
using BurbujaEngine.Engine.Extensions;
using BurbujaEngine.Engine.Core;
using BurbujaEngine.Monitor.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS
builder.Services.AddCors();

// Register EnvironmentConfig
builder.Services.AddSingleton<EnvironmentConfig>();

// Add Monitor Blazor services
builder.Services.AddMonitorBlazorServices();

// Add BurbujaEngine with standardized module registration 
// MICROKERNEL PRINCIPLE: Modules manage their own service registration
builder.Services.AddBurbujaEngine(Guid.NewGuid())
    .WithConfiguration(config =>
    {
        config.WithVersion("1.0.0")
              .WithModuleTimeout(TimeSpan.FromMinutes(2))
              .WithShutdownTimeout(TimeSpan.FromMinutes(1))
              .ContinueOnModuleFailure(true)  // Engine continues when modules fail
              .EnableParallelInitialization(true);
    })
    .AddDatabaseModule()    // Module handles its own service registration
    .AddMonitorModule()     // Module handles its own service registration
    .BuildEngine();         // Build the engine with all configured modules

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
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

// Configure engine endpoints for monitoring and diagnostics
app.MapEngineEndpoints("/engine");

// Configure monitor dashboard and endpoints
app.UseMonitorDashboard();

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
            status_endpoint = "/engine/status",
            monitor_dashboard = "/monitor"
        }
    });
});

app.Run();
