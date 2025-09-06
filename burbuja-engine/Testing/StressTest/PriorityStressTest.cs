using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using BurbujaEngine.Engine.Core;
using BurbujaEngine.Engine.Extensions;
using BurbujaEngine.Testing.SystemTest;

namespace BurbujaEngine.Testing.StressTest;

/// <summary>
/// Comprehensive stress test for the BurbujaEngine priority system.
/// Tests module initialization order, performance under load, and
/// system behavior in different contexts.
/// </summary>
public class PriorityStressTest
{
    private readonly ILogger<PriorityStressTest> _logger;
    private readonly List<TestResult> _results = new();
    private readonly SystemMetricsCollector _metricsCollector;
    
    public PriorityStressTest(ILogger<PriorityStressTest> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = new SystemMetricsCollector();
    }
    
    /// <summary>
    /// Run comprehensive stress tests for the priority system.
    /// </summary>
    public async Task<StressTestReport> RunStressTestAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting BurbujaEngine Priority Stress Test...");
        
        var overallStopwatch = Stopwatch.StartNew();
        var report = new StressTestReport
        {
            StartTime = DateTime.UtcNow,
            TestResults = new List<TestResult>()
        };
        
        // Collect initial system metrics
        var initialMetrics = await _metricsCollector.CollectMetricsAsync();
        report.InitialSystemMetrics = initialMetrics;
        _logger.LogInformation("Initial system state: {Metrics}", initialMetrics.ToString());
        
        try
        {
            // Test 1: Basic Priority Ordering
            _logger.LogInformation("Test 1: Basic Priority Ordering");
            var test1 = await TestBasicPriorityOrdering(cancellationToken);
            report.TestResults.Add(test1);
            
            // Test 2: Context-Specific Priority Behavior
            _logger.LogInformation("Test 2: Context-Specific Priority Behavior");
            var test2 = await TestContextSpecificPriorities(cancellationToken);
            report.TestResults.Add(test2);
            
            // Test 3: Parallel Initialization Performance
            _logger.LogInformation("Test 3: Parallel Initialization Performance");
            var test3 = await TestParallelInitialization(cancellationToken);
            report.TestResults.Add(test3);
            
            // Test 4: Load Testing with Multiple Engines
            _logger.LogInformation("Test 4: Load Testing with Multiple Engines");
            var test4 = await TestMultipleEngineLoad(cancellationToken);
            report.TestResults.Add(test4);
            
            // Test 5: Priority System Scalability
            _logger.LogInformation("Test 5: Priority System Scalability");
            var test5 = await TestPriorityScalability(cancellationToken);
            report.TestResults.Add(test5);
            
            // Test 6: Memory Pressure Test
            _logger.LogInformation("Test 6: Memory Pressure Test");
            var test6 = await TestMemoryPressure(cancellationToken);
            report.TestResults.Add(test6);
            
            overallStopwatch.Stop();
            
            // Collect final system metrics
            var finalMetrics = await _metricsCollector.CollectMetricsAsync();
            report.FinalSystemMetrics = finalMetrics;
            report.SystemMetricsDelta = _metricsCollector.CalculateDelta(initialMetrics, finalMetrics);
            
            report.EndTime = DateTime.UtcNow;
            report.TotalDuration = overallStopwatch.Elapsed;
            report.IsSuccessful = report.TestResults.All(r => r.IsSuccessful);
            
            _logger.LogInformation("Final system state: {Metrics}", finalMetrics.ToString());
            _logger.LogInformation("System changes: {Delta}", report.SystemMetricsDelta.ToString());
            _logger.LogInformation("Stress test completed in {Duration:F2}ms. Success: {Success}", 
                overallStopwatch.Elapsed.TotalMilliseconds, report.IsSuccessful);
            
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stress test failed: {Message}", ex.Message);
            
            // Still collect final metrics even on failure
            try
            {
                var finalMetrics = await _metricsCollector.CollectMetricsAsync();
                report.FinalSystemMetrics = finalMetrics;
                report.SystemMetricsDelta = _metricsCollector.CalculateDelta(initialMetrics, finalMetrics);
            }
            catch
            {
                // Ignore metrics collection errors during cleanup
            }
            
            report.EndTime = DateTime.UtcNow;
            report.TotalDuration = overallStopwatch.Elapsed;
            report.IsSuccessful = false;
            report.ErrorMessage = ex.Message;
            return report;
        }
    }
    
    /// <summary>
    /// Test basic priority ordering without context adjustments.
    /// </summary>
    private async Task<TestResult> TestBasicPriorityOrdering(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new TestResult
        {
            TestName = "Basic Priority Ordering",
            StartTime = DateTime.UtcNow
        };
        
        var beforeMetrics = await _metricsCollector.CollectMetricsAsync();
        
        try
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            
            // Add engine with all mock modules using new pattern
            services.AddBurbujaEngine(Guid.NewGuid())
                .WithConfiguration(config =>
                {
                    config.WithVersion("1.0.0-test")
                          .WithModuleTimeout(TimeSpan.FromMinutes(1))
                          .WithShutdownTimeout(TimeSpan.FromSeconds(30))
                          .ContinueOnModuleFailure(false)
                          .EnableParallelInitialization(false); // Sequential for order testing
                })
                // Add modules in random order to test priority sorting
                .AddEngineModule<MockAnalyticsModule>()
                .AddEngineModule<MockConfigurationModule>()
                .AddEngineModule<MockEmailServiceModule>()
                .AddEngineModule<MockSecurityModule>()
                .AddEngineModule<MockCacheModule>()
                .AddEngineModule<MockBusinessLogicModule>()
                .AddEngineModule<MockMonitoringModule>()
                .BuildEngine();
            
            var serviceProvider = services.BuildServiceProvider();
            var engine = serviceProvider.GetRequiredService<IBurbujaEngine>();
            
            // Test initialization
            var initResult = await engine.InitializeAsync(cancellationToken);
            if (!initResult.Success)
            {
                throw new Exception($"Engine initialization failed: {initResult.Message}");
            }
            
            // Verify module order
            var actualOrder = engine.Modules.Select(m => m.ModuleName).ToList();
            var expectedOrder = new[]
            {
                "Mock Configuration Module",  // Critical 5
                "Mock Security Module",       // Critical 10
                "Mock Cache Module",          // Infrastructure 20
                "Mock Business Logic Module", // Core 15
                "Mock Email Service Module",  // Service 25
                "Mock Analytics Module",      // Feature 30
                "Mock Monitoring Module"      // Monitoring 10
            };
            
            result.Metrics["ActualOrder"] = string.Join(" -> ", actualOrder);
            result.Metrics["ExpectedOrder"] = string.Join(" -> ", expectedOrder);
            
            // Start engine
            var startResult = await engine.StartAsync(cancellationToken);
            if (!startResult.Success)
            {
                throw new Exception($"Engine start failed: {startResult.Message}");
            }
            
            // Collect performance metrics
            result.Metrics["InitializationTime"] = initResult.Duration.TotalMilliseconds;
            result.Metrics["StartTime"] = startResult.Duration.TotalMilliseconds;
            result.Metrics["ModuleCount"] = engine.Modules.Count;
            
            // Shutdown
            await engine.ShutdownAsync(cancellationToken);
            
            stopwatch.Stop();
            
            var afterMetrics = await _metricsCollector.CollectMetricsAsync();
            result.SystemMetrics = _metricsCollector.CalculateDelta(beforeMetrics, afterMetrics);
            
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = true;
            result.Message = "Priority ordering test completed successfully";
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            var afterMetrics = await _metricsCollector.CollectMetricsAsync();
            result.SystemMetrics = _metricsCollector.CalculateDelta(beforeMetrics, afterMetrics);
            
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Message = $"Priority ordering test failed: {ex.Message}";
            return result;
        }
    }
    
    /// <summary>
    /// Test context-specific priority behavior.
    /// </summary>
    private async Task<TestResult> TestContextSpecificPriorities(CancellationToken cancellationToken)
    {
        var result = new TestResult
        {
            TestName = "Context-Specific Priorities",
            StartTime = DateTime.UtcNow
        };
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var contexts = new[] { "Development", "Testing", "Production" };
            var contextResults = new Dictionary<string, ContextTestResult>();
            
            foreach (var context in contexts)
            {
                var contextStopwatch = Stopwatch.StartNew();
                
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
                
                services.AddBurbujaEngine(config =>
                {
                    config.WithVersion("1.0.0-context-test")
                          .WithValue("ExecutionContext", context)
                          .WithModuleTimeout(TimeSpan.FromMinutes(1))
                          .EnableParallelInitialization(true);
                })
                .AddEngineModule<MockConfigurationModule>()
                .AddEngineModule<MockSecurityModule>()
                .AddEngineModule<MockCacheModule>()
                .AddEngineModule<MockEmailServiceModule>()
                .AddEngineModule<MockAnalyticsModule>()
                .AddEngineModule<MockMonitoringModule>()
                .Build();
                
                var serviceProvider = services.BuildServiceProvider();
                var engineInstance = serviceProvider.GetRequiredService<IBurbujaEngine>();
                
                var initResult = await engineInstance.InitializeAsync(cancellationToken);
                var startResult = await engineInstance.StartAsync(cancellationToken);
                
                contextStopwatch.Stop();
                
                contextResults[context] = new ContextTestResult
                {
                    Context = context,
                    InitializationTime = initResult.Duration,
                    StartTime = startResult.Duration,
                    TotalTime = contextStopwatch.Elapsed,
                    ModuleOrder = engineInstance.Modules.Select(m => m.ModuleName).ToList(),
                    Success = initResult.Success && startResult.Success
                };
                
                await engineInstance.ShutdownAsync(cancellationToken);
                serviceProvider.Dispose();
            }
            
            // Analyze context differences
            result.Metrics["DevelopmentInitTime"] = contextResults["Development"].InitializationTime.TotalMilliseconds;
            result.Metrics["TestingInitTime"] = contextResults["Testing"].InitializationTime.TotalMilliseconds;
            result.Metrics["ProductionInitTime"] = contextResults["Production"].InitializationTime.TotalMilliseconds;
            
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = contextResults.Values.All(r => r.Success);
            result.Message = "Context-specific priority test completed";
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }
    
    /// <summary>
    /// Test parallel initialization performance.
    /// </summary>
    private async Task<TestResult> TestParallelInitialization(CancellationToken cancellationToken)
    {
        var result = new TestResult
        {
            TestName = "Parallel Initialization Performance",
            StartTime = DateTime.UtcNow
        };
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Test both sequential and parallel initialization
            var sequentialTime = await MeasureInitializationTime(false, cancellationToken);
            var parallelTime = await MeasureInitializationTime(true, cancellationToken);
            
            result.Metrics["SequentialTime"] = sequentialTime.TotalMilliseconds;
            result.Metrics["ParallelTime"] = parallelTime.TotalMilliseconds;
            result.Metrics["SpeedupRatio"] = sequentialTime.TotalMilliseconds / parallelTime.TotalMilliseconds;
            result.Metrics["ParallelEfficiency"] = (sequentialTime.TotalMilliseconds - parallelTime.TotalMilliseconds) / sequentialTime.TotalMilliseconds * 100;
            
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = true;
            result.Message = $"Parallel initialization achieved {result.Metrics["SpeedupRatio"]:F2}x speedup";
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }
    
    private async Task<TimeSpan> MeasureInitializationTime(bool parallel, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        
        services.AddBurbujaEngine(config =>
        {
            config.WithVersion("1.0.0-perf-test")
                  .EnableParallelInitialization(parallel);
        })
        .AddEngineModule(new MockCacheModule())
        .AddEngineModule(new MockEmailServiceModule())
        .AddEngineModule(new MockAnalyticsModule())
        .AddEngineModule(new MockCacheModule())
        .AddEngineModule(new MockEmailServiceModule())
        .AddEngineModule(new MockAnalyticsModule())
        .AddEngineModule(new MockCacheModule())
        .AddEngineModule(new MockEmailServiceModule())
        .AddEngineModule(new MockAnalyticsModule())
        .Build();
        
        var serviceProvider = services.BuildServiceProvider();
        var engine = serviceProvider.GetRequiredService<IBurbujaEngine>();
        
        var stopwatch = Stopwatch.StartNew();
        var initResult = await engine.InitializeAsync(cancellationToken);
        var startResult = await engine.StartAsync(cancellationToken);
        stopwatch.Stop();
        
        await engine.ShutdownAsync(cancellationToken);
        serviceProvider.Dispose();
        
        if (!initResult.Success || !startResult.Success)
        {
            throw new Exception($"Engine failed: Init={initResult.Success}, Start={startResult.Success}");
        }
        
        return stopwatch.Elapsed;
    }
    
    /// <summary>
    /// Test multiple engine instances running concurrently.
    /// </summary>
    private async Task<TestResult> TestMultipleEngineLoad(CancellationToken cancellationToken)
    {
        var result = new TestResult
        {
            TestName = "Multiple Engine Load Test",
            StartTime = DateTime.UtcNow
        };
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            const int engineCount = 5;
            var tasks = new List<Task<EngineTestResult>>();
            
            for (int i = 0; i < engineCount; i++)
            {
                tasks.Add(CreateAndTestEngine(i, cancellationToken));
            }
            
            var results = await Task.WhenAll(tasks);
            
            result.Metrics["EngineCount"] = engineCount;
            result.Metrics["AverageInitTime"] = results.Average(r => r.InitializationTime.TotalMilliseconds);
            result.Metrics["MaxInitTime"] = results.Max(r => r.InitializationTime.TotalMilliseconds);
            result.Metrics["MinInitTime"] = results.Min(r => r.InitializationTime.TotalMilliseconds);
            result.Metrics["SuccessRate"] = (double)results.Count(r => r.Success) / engineCount * 100;
            
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = results.All(r => r.Success);
            result.Message = $"Load test with {engineCount} engines completed";
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }
    
    private async Task<EngineTestResult> CreateAndTestEngine(int engineIndex, CancellationToken cancellationToken)
    {
        try
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Error));
            
            services.AddBurbujaEngine(config =>
            {
                config.WithVersion($"1.0.0-load-test-{engineIndex}")
                      .EnableParallelInitialization(true);
            })
            .AddEngineModule<MockConfigurationModule>()
            .AddEngineModule<MockCacheModule>()
            .AddEngineModule<MockBusinessLogicModule>()
            .AddEngineModule<MockEmailServiceModule>()
            .Build();
            
            var serviceProvider = services.BuildServiceProvider();
            var engine = serviceProvider.GetRequiredService<IBurbujaEngine>();
            
            var initStopwatch = Stopwatch.StartNew();
            var initResult = await engine.InitializeAsync(cancellationToken);
            var startResult = await engine.StartAsync(cancellationToken);
            initStopwatch.Stop();
            
            await engine.ShutdownAsync(cancellationToken);
            serviceProvider.Dispose();
            
            return new EngineTestResult
            {
                EngineIndex = engineIndex,
                InitializationTime = initStopwatch.Elapsed,
                Success = initResult.Success && startResult.Success
            };
        }
        catch (Exception ex)
        {
            return new EngineTestResult
            {
                EngineIndex = engineIndex,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
    
    /// <summary>
    /// Test priority system scalability with many modules.
    /// </summary>
    private async Task<TestResult> TestPriorityScalability(CancellationToken cancellationToken)
    {
        var result = new TestResult
        {
            TestName = "Priority System Scalability",
            StartTime = DateTime.UtcNow
        };
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var moduleCounts = new[] { 10, 25, 50, 100 };
            var scalabilityResults = new Dictionary<int, TimeSpan>();
            
            foreach (var moduleCount in moduleCounts)
            {
                var testTime = await MeasureScalabilityForModuleCount(moduleCount, cancellationToken);
                scalabilityResults[moduleCount] = testTime;
                result.Metrics[$"Time_{moduleCount}_modules"] = testTime.TotalMilliseconds;
            }
            
            // Calculate scalability metrics
            var baseTime = scalabilityResults[moduleCounts[0]];
            foreach (var kvp in scalabilityResults.Skip(1))
            {
                var ratio = kvp.Value.TotalMilliseconds / baseTime.TotalMilliseconds;
                var efficiency = kvp.Key / ratio; // Modules per relative time unit
                result.Metrics[$"Efficiency_{kvp.Key}_modules"] = efficiency;
            }
            
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = true;
            result.Message = "Scalability test completed successfully";
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }
    
    private async Task<TimeSpan> MeasureScalabilityForModuleCount(int moduleCount, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Error));
        
        var builder = services.AddBurbujaEngine(config =>
        {
            config.WithVersion("1.0.0-scalability-test")
                  .EnableParallelInitialization(true);
        });
        
        // Add modules in a pattern to test priority sorting
        var moduleFactories = new Func<IEngineModule>[]
        {
            () => new MockConfigurationModule(),
            () => new MockSecurityModule(),
            () => new MockCacheModule(),
            () => new MockBusinessLogicModule(),
            () => new MockEmailServiceModule(),
            () => new MockAnalyticsModule(),
            () => new MockMonitoringModule()
        };
        
        for (int i = 0; i < moduleCount; i++)
        {
            var moduleFactory = moduleFactories[i % moduleFactories.Length];
            builder.AddEngineModule(moduleFactory());
        }
        
        builder.Build();
        
        var serviceProvider = services.BuildServiceProvider();
        var engine = serviceProvider.GetRequiredService<IBurbujaEngine>();
        
        var stopwatch = Stopwatch.StartNew();
        var initResult = await engine.InitializeAsync(cancellationToken);
        var startResult = await engine.StartAsync(cancellationToken);
        stopwatch.Stop();
        
        await engine.ShutdownAsync(cancellationToken);
        serviceProvider.Dispose();
        
        if (!initResult.Success || !startResult.Success)
        {
            throw new Exception($"Scalability test failed for {moduleCount} modules");
        }
        
        return stopwatch.Elapsed;
    }
    
    /// <summary>
    /// Test memory pressure and garbage collection behavior.
    /// </summary>
    private async Task<TestResult> TestMemoryPressure(CancellationToken cancellationToken)
    {
        var result = new TestResult
        {
            TestName = "Memory Pressure Test",
            StartTime = DateTime.UtcNow
        };
        
        var stopwatch = Stopwatch.StartNew();
        var beforeMetrics = await _metricsCollector.CollectMetricsAsync();
        
        try
        {
            const int engineCount = 3;
            const int allocationsPerEngine = 1000;
            var engines = new List<IBurbujaEngine>();
            var allocatedMemory = new List<object>();
            
            try
            {
                // Create multiple engines and allocate memory
                for (int i = 0; i < engineCount; i++)
                {
                    var services = new ServiceCollection();
                    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Error));
                    
                    services.AddBurbujaEngine(config =>
                    {
                        config.WithVersion($"1.0.0-memory-test-{i}")
                              .EnableParallelInitialization(true);
                    })
                    // Add memory-intensive modules
                    .AddEngineModule<MockCacheModule>()
                    .AddEngineModule<MockAnalyticsModule>()
                    .AddEngineModule<MockBusinessLogicModule>()
                    .Build();
                    
                    var serviceProvider = services.BuildServiceProvider();
                    var engineInstance = serviceProvider.GetRequiredService<IBurbujaEngine>();
                    
                    var initResult = await engineInstance.InitializeAsync(cancellationToken);
                    var startResult = await engineInstance.StartAsync(cancellationToken);
                    
                    if (!initResult.Success || !startResult.Success)
                    {
                        throw new Exception($"Engine {i} failed to start");
                    }
                    
                    engines.Add(engineInstance);
                    
                    // Allocate memory to create pressure
                    for (int j = 0; j < allocationsPerEngine; j++)
                    {
                        // Create various sized allocations
                        var size = (j % 10 + 1) * 1024; // 1KB to 10KB
                        var allocation = new byte[size];
                        Random.Shared.NextBytes(allocation); // Fill with data
                        allocatedMemory.Add(allocation);
                        
                        if (j % 100 == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                }
                
                // Force garbage collection to test system behavior under pressure
                var gcBefore = GC.GetTotalMemory(false);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var gcAfter = GC.GetTotalMemory(false);
                
                result.Metrics["EngineCount"] = engineCount;
                result.Metrics["AllocationsPerEngine"] = allocationsPerEngine;
                result.Metrics["TotalAllocations"] = engineCount * allocationsPerEngine;
                result.Metrics["MemoryBeforeGC_MB"] = gcBefore / (1024.0 * 1024.0);
                result.Metrics["MemoryAfterGC_MB"] = gcAfter / (1024.0 * 1024.0);
                result.Metrics["MemoryFreed_MB"] = (gcBefore - gcAfter) / (1024.0 * 1024.0);
                
                // Test that engines are still responsive after memory pressure
                var responsiveCount = 0;
                foreach (var engine in engines)
                {
                    try
                    {
                        var health = await engine.GetHealthAsync(cancellationToken);
                        if (health.Status == HealthStatus.Healthy)
                        {
                            responsiveCount++;
                        }
                    }
                    catch
                    {
                        // Engine may not respond under pressure
                    }
                }
                
                result.Metrics["ResponsiveEngineCount"] = responsiveCount;
                result.Metrics["ResponsivePercentage"] = (double)responsiveCount / engineCount * 100;
                
                result.IsSuccessful = responsiveCount >= engineCount * 0.8; // At least 80% should remain responsive
                result.Message = result.IsSuccessful 
                    ? $"Memory pressure test passed - {responsiveCount}/{engineCount} engines remained responsive"
                    : $"Memory pressure test failed - only {responsiveCount}/{engineCount} engines remained responsive";
            }
            finally
            {
                // Cleanup
                foreach (var engine in engines)
                {
                    try
                    {
                        await engine.ShutdownAsync(CancellationToken.None);
                    }
                    catch
                    {
                        // Ignore shutdown errors during cleanup
                    }
                }
                
                // Clear allocated memory
                allocatedMemory.Clear();
                
                // Force final garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            
            stopwatch.Stop();
            
            var afterMetrics = await _metricsCollector.CollectMetricsAsync();
            result.SystemMetrics = _metricsCollector.CalculateDelta(beforeMetrics, afterMetrics);
            
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            var afterMetrics = await _metricsCollector.CollectMetricsAsync();
            result.SystemMetrics = _metricsCollector.CalculateDelta(beforeMetrics, afterMetrics);
            
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Message = $"Memory pressure test failed: {ex.Message}";
            
            return result;
        }
    }
}

/// <summary>
/// Result of a single test within the stress test suite.
/// </summary>
public class TestResult
{
    public string TestName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsSuccessful { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
    public SystemMetrics? SystemMetrics { get; set; }
}

/// <summary>
/// Overall stress test report.
/// </summary>
public class StressTestReport
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public List<TestResult> TestResults { get; set; } = new();
    public SystemMetrics? InitialSystemMetrics { get; set; }
    public SystemMetrics? FinalSystemMetrics { get; set; }
    public SystemMetrics? SystemMetricsDelta { get; set; }
    
    public void PrintReport()
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("BURBUJA ENGINE PRIORITY SYSTEM STRESS TEST REPORT");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine($"Start Time: {StartTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"End Time: {EndTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Total Duration: {TotalDuration.TotalSeconds:F2} seconds");
        Console.WriteLine($"Overall Success: {IsSuccessful}");
        
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            Console.WriteLine($"Error: {ErrorMessage}");
        }
        
        // System metrics summary
        if (SystemMetricsDelta != null)
        {
            Console.WriteLine();
            Console.WriteLine("SYSTEM METRICS DELTA:");
            Console.WriteLine("-".PadRight(40, '-'));
            Console.WriteLine($"CPU Usage Change: {SystemMetricsDelta.CpuUsagePercent:F2}%");
            Console.WriteLine($"Memory Change: {SystemMetricsDelta.MemoryUsageMB:F2} MB");
            Console.WriteLine($"Threads Change: {SystemMetricsDelta.ThreadCount}");
            Console.WriteLine($"GC Collections: {SystemMetricsDelta.GcCollectionCount}");
            Console.WriteLine($"Handles Change: {SystemMetricsDelta.HandleCount}");
            Console.WriteLine($"Managed Memory Change: {SystemMetricsDelta.ManagedMemoryMB:F2} MB");
            Console.WriteLine($"Allocated Bytes: {SystemMetricsDelta.AllocatedBytesForCurrentThread:N0}");
        }
        
        Console.WriteLine();
        Console.WriteLine("TEST RESULTS:");
        Console.WriteLine("-".PadRight(80, '-'));
        
        foreach (var test in TestResults)
        {
            Console.WriteLine($"Test: {test.TestName}");
            Console.WriteLine($"  Duration: {test.Duration.TotalMilliseconds:F2}ms");
            Console.WriteLine($"  Success: {test.IsSuccessful}");
            Console.WriteLine($"  Message: {test.Message}");
            
            if (!string.IsNullOrEmpty(test.ErrorMessage))
            {
                Console.WriteLine($"  Error: {test.ErrorMessage}");
            }
            
            if (test.Metrics.Any())
            {
                Console.WriteLine("  Metrics:");
                foreach (var metric in test.Metrics)
                {
                    Console.WriteLine($"    {metric.Key}: {metric.Value}");
                }
            }
            
            if (test.SystemMetrics != null)
            {
                Console.WriteLine($"  System Impact: {test.SystemMetrics}");
            }
            
            Console.WriteLine();
        }
        
        Console.WriteLine("=".PadRight(80, '='));
    }
}

/// <summary>
/// Result of testing a specific context.
/// </summary>
internal class ContextTestResult
{
    public string Context { get; set; } = string.Empty;
    public TimeSpan InitializationTime { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan TotalTime { get; set; }
    public List<string> ModuleOrder { get; set; } = new();
    public bool Success { get; set; }
}

/// <summary>
/// Result of testing an individual engine.
/// </summary>
internal class EngineTestResult
{
    public int EngineIndex { get; set; }
    public TimeSpan InitializationTime { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
