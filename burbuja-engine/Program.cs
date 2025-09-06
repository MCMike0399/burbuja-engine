using BurbujaEngine.Configuration;
using BurbujaEngine.Engine.Core;
using BurbujaEngine.Engine.Extensions;
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

// MICROKERNEL APPROACH: Let the engine handle module discovery and lifecycle
await builder.Services.AddBurbujaEngine()
    .WithConfiguration(config =>
    {
        config.WithVersion("1.0.0")
              .WithModuleTimeout(TimeSpan.FromMinutes(2))
              .WithShutdownTimeout(TimeSpan.FromMinutes(1))
              .ContinueOnModuleFailure(true)  // Engine continues when modules fail
              .EnableParallelInitialization(true);
    })
    .WithModuleDiscovery(discovery =>
    {
        // Configure auto-discovery for the current environment
        discovery.EnableConventionBasedDiscovery = true;
        discovery.CurrentEnvironment = builder.Environment.EnvironmentName;
        
        // Filter assemblies to scan for modules
        discovery.AssemblyFilter = name => 
            name.Name?.Contains("BurbujaEngine", StringComparison.OrdinalIgnoreCase) == true;
    })
    .BuildEngineAsync(); // Use async build for auto-discovery

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

app.Run();
