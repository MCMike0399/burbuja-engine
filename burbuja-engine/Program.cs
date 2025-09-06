using BurbujaEngine.Configuration;
using BurbujaEngine.Database.Extensions;
using BurbujaEngine.Engine.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS
builder.Services.AddCors();

// Register EnvironmentConfig
builder.Services.AddSingleton<EnvironmentConfig>();

// Add database services
builder.Services.AddBurbujaEngineDatabase();

// Add BurbujaEngine with configuration only (no modules for now)
builder.Services.AddBurbujaEngine(Guid.NewGuid(), engine =>
{
    engine.WithConfiguration(config =>
    {
        config.WithVersion("1.0.0")
              .WithModuleTimeout(TimeSpan.FromMinutes(2))
              .WithShutdownTimeout(TimeSpan.FromMinutes(1))
              .ContinueOnModuleFailure(false)
              .EnableParallelInitialization(true);
    });
});

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
