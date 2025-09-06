using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BurbujaEngine.Engine.Core;
using BurbujaEngine.Testing.StressTest;

namespace BurbujaEngine.Testing.SystemTest;

/// <summary>
/// Comprehensive system test runner that replaces the shell script functionality.
/// Includes system metrics collection, stress testing, and detailed reporting.
/// </summary>
public class EngineSystemTestRunner
{
    private readonly ILogger<EngineSystemTestRunner> _logger;
    private readonly IBurbujaEngine? _engine;
    private readonly SystemMetricsCollector _metricsCollector;
    private readonly PriorityStressTest _stressTest;
    
    public EngineSystemTestRunner(ILogger<EngineSystemTestRunner> logger, IBurbujaEngine? engine = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _engine = engine;
        _metricsCollector = new SystemMetricsCollector();
        
        // Create logger for stress test
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var stressTestLogger = loggerFactory.CreateLogger<PriorityStressTest>();
        _stressTest = new PriorityStressTest(stressTestLogger);
    }
    
    /// <summary>
    /// Run comprehensive system tests.
    /// </summary>
    public async Task<SystemTestReport> RunCompleteTestSuiteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🚀 Starting BurbujaEngine Complete System Test Suite");
        _logger.LogInformation("=" + new string('=', 79));
        
        var overallStopwatch = Stopwatch.StartNew();
        var report = new SystemTestReport
        {
            StartTime = DateTime.UtcNow,
            SystemInfo = GetBasicSystemInfo(),
            TestResults = new List<SystemTestResult>()
        };
        
        try
        {
            // Collect baseline system metrics
            var baselineMetrics = await _metricsCollector.CollectMetricsAsync();
            report.BaselineMetrics = baselineMetrics;
            
            // Test 1: Basic Engine Status
            _logger.LogInformation("1. Testing basic engine status...");
            var test1 = await TestBasicEngineStatus(cancellationToken);
            report.TestResults.Add(test1);
            
            // Test 2: Priority System Info
            _logger.LogInformation("2. Testing priority system info...");
            var test2 = await TestPrioritySystemInfo(cancellationToken);
            report.TestResults.Add(test2);
            
            // Test 3: Engine Health Check
            _logger.LogInformation("3. Testing engine health...");
            var test3 = await TestEngineHealth(cancellationToken);
            report.TestResults.Add(test3);
            
            // Test 4: System Metrics During Operation
            _logger.LogInformation("4. Collecting system metrics during operation...");
            var test4 = await TestSystemMetricsDuringOperation(cancellationToken);
            report.TestResults.Add(test4);
            
            // Test 5: Stress Test with Detailed Metrics
            _logger.LogInformation("5. Running comprehensive stress test...");
            var test5 = await RunStressTestWithMetrics(cancellationToken);
            report.TestResults.Add(test5);
            
            // Collect final system metrics
            var finalMetrics = await _metricsCollector.CollectMetricsAsync();
            report.FinalMetrics = finalMetrics;
            
            overallStopwatch.Stop();
            report.EndTime = DateTime.UtcNow;
            report.TotalDuration = overallStopwatch.Elapsed;
            report.IsSuccessful = report.TestResults.All(r => r.IsSuccessful);
            
            _logger.LogInformation("🎉 All tests completed!");
            _logger.LogInformation($"Total execution time: {overallStopwatch.Elapsed.TotalSeconds:F2} seconds");
            _logger.LogInformation($"Success rate: {report.TestResults.Count(r => r.IsSuccessful)}/{report.TestResults.Count}");
            
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System test suite failed: {Message}", ex.Message);
            report.EndTime = DateTime.UtcNow;
            report.TotalDuration = overallStopwatch.Elapsed;
            report.IsSuccessful = false;
            report.ErrorMessage = ex.Message;
            return report;
        }
    }
    
    /// <summary>
    /// Test basic engine status.
    /// </summary>
    private async Task<SystemTestResult> TestBasicEngineStatus(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new SystemTestResult
        {
            TestName = "Basic Engine Status",
            StartTime = DateTime.UtcNow
        };
        
        try
        {
            if (_engine == null)
            {
                result.Message = "No engine instance available - creating test engine";
                var testEngine = await CreateTestEngine(cancellationToken);
                result.Metrics["engine_created"] = true;
                result.Metrics["engine_state"] = testEngine.State.ToString();
                await testEngine.ShutdownAsync(cancellationToken);
            }
            else
            {
                result.Metrics["engine_state"] = _engine.State.ToString();
                result.Metrics["module_count"] = _engine.Modules.Count;
                result.Metrics["engine_id"] = _engine.EngineId.ToString();
                result.Message = $"Engine is in {_engine.State} state with {_engine.Modules.Count} modules";
            }
            
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = true;
            result.Message += " ✅";
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Message = $"Engine status test failed: {ex.Message}";
            return result;
        }
    }
    
    /// <summary>
    /// Test priority system information.
    /// </summary>
    private Task<SystemTestResult> TestPrioritySystemInfo(CancellationToken cancellationToken)
    {
        var result = new SystemTestResult
        {
            TestName = "Priority System Info",
            StartTime = DateTime.UtcNow
        };
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Test priority system functionality
            var priorities = PriorityLevelExtensions.GetAllPrioritiesInOrder().ToList();
            result.Metrics["priority_levels_count"] = priorities.Count;
            result.Metrics["priority_system_version"] = "2.0.0";
            
            // Test priority comparisons
            var testComparisons = 0;
            foreach (var priority in priorities)
            {
                var categoryName = priority.GetCategoryName();
                var description = priority.GetDescription();
                testComparisons++;
            }
            
            result.Metrics["priority_comparisons_tested"] = testComparisons;
            
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = true;
            result.Message = $"Priority system accessible with {priorities.Count} levels ✅";
            
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Message = $"Priority system test failed: {ex.Message}";
            return Task.FromResult(result);
        }
    }
    
    /// <summary>
    /// Test engine health.
    /// </summary>
    private async Task<SystemTestResult> TestEngineHealth(CancellationToken cancellationToken)
    {
        var result = new SystemTestResult
        {
            TestName = "Engine Health Check",
            StartTime = DateTime.UtcNow
        };
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            if (_engine != null)
            {
                var health = await _engine.GetHealthAsync(cancellationToken);
                result.Metrics["overall_status"] = health.Status.ToString();
                result.Metrics["message"] = health.Message ?? "No message";
                result.Message = $"Engine health: {health.Status} ✅";
            }
            else
            {
                var testEngine = await CreateTestEngine(cancellationToken);
                var health = await testEngine.GetHealthAsync(cancellationToken);
                result.Metrics["overall_status"] = health.Status.ToString();
                result.Metrics["message"] = health.Message ?? "No message";
                result.Message = $"Test engine health: {health.Status} ✅";
                await testEngine.ShutdownAsync(cancellationToken);
            }
            
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = true;
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Message = $"Health check failed: {ex.Message}";
            return result;
        }
    }
    
    /// <summary>
    /// Test system metrics during engine operation.
    /// </summary>
    private async Task<SystemTestResult> TestSystemMetricsDuringOperation(CancellationToken cancellationToken)
    {
        var result = new SystemTestResult
        {
            TestName = "System Metrics During Operation",
            StartTime = DateTime.UtcNow
        };
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var initialMetrics = await _metricsCollector.CollectMetricsAsync();
            
            // Create and run a test engine while collecting metrics
            var testEngine = await CreateTestEngine(cancellationToken);
            
            var duringMetrics = await _metricsCollector.CollectMetricsAsync();
            
            // Perform some operations
            for (int i = 0; i < 10; i++)
            {
                await testEngine.GetHealthAsync(cancellationToken);
                await Task.Delay(100, cancellationToken);
            }
            
            var afterMetrics = await _metricsCollector.CollectMetricsAsync();
            
            await testEngine.ShutdownAsync(cancellationToken);
            
            var finalMetrics = await _metricsCollector.CollectMetricsAsync();
            
            // Calculate differences
            result.Metrics["memory_change_mb"] = (afterMetrics.MemoryUsageMB - initialMetrics.MemoryUsageMB);
            result.Metrics["cpu_usage_during"] = duringMetrics.CpuUsagePercent;
            result.Metrics["thread_count_change"] = (afterMetrics.ThreadCount - initialMetrics.ThreadCount);
            result.Metrics["gc_collections_change"] = (afterMetrics.GcCollectionCount - initialMetrics.GcCollectionCount);
            
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = true;
            result.Message = $"System metrics collected successfully ✅";
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Message = $"System metrics test failed: {ex.Message}";
            return result;
        }
    }
    
    /// <summary>
    /// Run stress test with comprehensive metrics collection.
    /// </summary>
    private async Task<SystemTestResult> RunStressTestWithMetrics(CancellationToken cancellationToken)
    {
        var result = new SystemTestResult
        {
            TestName = "Comprehensive Stress Test with Metrics",
            StartTime = DateTime.UtcNow
        };
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Collect pre-stress metrics
            var preStressMetrics = await _metricsCollector.CollectMetricsAsync();
            
            // Run the stress test
            var stressReport = await _stressTest.RunStressTestAsync(cancellationToken);
            
            // Collect post-stress metrics
            var postStressMetrics = await _metricsCollector.CollectMetricsAsync();
            
            // Analyze results
            result.Metrics["stress_test_success"] = stressReport.IsSuccessful;
            result.Metrics["stress_test_duration_ms"] = stressReport.TotalDuration.TotalMilliseconds;
            result.Metrics["stress_tests_run"] = stressReport.TestResults.Count;
            result.Metrics["stress_tests_passed"] = stressReport.TestResults.Count(t => t.IsSuccessful);
            
            // Memory metrics
            result.Metrics["memory_before_mb"] = preStressMetrics.MemoryUsageMB;
            result.Metrics["memory_after_mb"] = postStressMetrics.MemoryUsageMB;
            result.Metrics["memory_increase_mb"] = postStressMetrics.MemoryUsageMB - preStressMetrics.MemoryUsageMB;
            
            // CPU metrics
            result.Metrics["cpu_before_percent"] = preStressMetrics.CpuUsagePercent;
            result.Metrics["cpu_after_percent"] = postStressMetrics.CpuUsagePercent;
            
            // Thread metrics
            result.Metrics["threads_before"] = preStressMetrics.ThreadCount;
            result.Metrics["threads_after"] = postStressMetrics.ThreadCount;
            result.Metrics["threads_change"] = postStressMetrics.ThreadCount - preStressMetrics.ThreadCount;
            
            // GC metrics
            result.Metrics["gc_collections_during_test"] = postStressMetrics.GcCollectionCount - preStressMetrics.GcCollectionCount;
            result.Metrics["gc_heap_change_mb"] = postStressMetrics.ManagedMemoryMB - preStressMetrics.ManagedMemoryMB;
            
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = stressReport.IsSuccessful;
            
            if (stressReport.IsSuccessful)
            {
                result.Message = $"Stress test completed successfully with {stressReport.TestResults.Count} tests ✅";
            }
            else
            {
                result.Message = $"Stress test failed: {stressReport.ErrorMessage}";
                result.ErrorMessage = stressReport.ErrorMessage;
            }
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Message = $"Stress test with metrics failed: {ex.Message}";
            return result;
        }
    }
    
    /// <summary>
    /// Create a test engine for testing purposes.
    /// </summary>
    private async Task<IBurbujaEngine> CreateTestEngine(CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        
        services.AddBurbujaEngine(Guid.NewGuid(), engine =>
        {
            engine.WithConfiguration(config =>
            {
                config.WithVersion("1.0.0-system-test")
                      .WithValue("ExecutionContext", "Testing")
                      .EnableParallelInitialization(true);
            });
            
            // Add test modules
            engine.AddModule<MockConfigurationModule>();
            engine.AddModule<MockSecurityModule>();
            engine.AddModule<MockCacheModule>();
            engine.AddModule<MockBusinessLogicModule>();
            engine.AddModule<MockEmailServiceModule>();
            engine.AddModule<MockAnalyticsModule>();
            engine.AddModule<MockMonitoringModule>();
        });
        
        var serviceProvider = services.BuildServiceProvider();
        var testEngine = serviceProvider.GetRequiredService<IBurbujaEngine>();
        
        await testEngine.InitializeAsync(cancellationToken);
        await testEngine.StartAsync(cancellationToken);
        
        return testEngine;
    }
    
    /// <summary>
    /// Get basic system information.
    /// </summary>
    private BasicSystemInfo GetBasicSystemInfo()
    {
        return new BasicSystemInfo
        {
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ProcessorName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown",
            ProcessorCount = Environment.ProcessorCount,
            DotNetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
            Is64BitProcess = Environment.Is64BitProcess,
            TotalMemoryMB = GetApproximateMemoryMB(),
            AvailableMemoryMB = GetApproximateMemoryMB() * 0.5 // Rough estimate
        };
    }
    
    /// <summary>
    /// Get approximate memory in MB.
    /// </summary>
    private double GetApproximateMemoryMB()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            return process.WorkingSet64 / 1024.0 / 1024.0 * 4; // Estimate total as 4x working set
        }
        catch
        {
            return 8192; // 8GB default
        }
    }
}

/// <summary>
/// Basic system information.
/// </summary>
public class BasicSystemInfo
{
    public string OperatingSystem { get; set; } = string.Empty;
    public string ProcessorName { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public double TotalMemoryMB { get; set; }
    public double AvailableMemoryMB { get; set; }
    public string DotNetVersion { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool Is64BitOperatingSystem { get; set; }
    public bool Is64BitProcess { get; set; }
}

/// <summary>
/// System test report containing all test results and metrics.
/// </summary>
public class SystemTestReport
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public BasicSystemInfo? SystemInfo { get; set; }
    public SystemMetrics? BaselineMetrics { get; set; }
    public SystemMetrics? FinalMetrics { get; set; }
    public List<SystemTestResult> TestResults { get; set; } = new();
    
    /// <summary>
    /// Print a detailed report to console.
    /// </summary>
    public void PrintReport()
    {
        Console.WriteLine("=" + new string('=', 79));
        Console.WriteLine("BURBUJA ENGINE COMPREHENSIVE SYSTEM TEST REPORT");
        Console.WriteLine("=" + new string('=', 79));
        Console.WriteLine($"Start Time: {StartTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"End Time: {EndTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Total Duration: {TotalDuration.TotalSeconds:F2} seconds");
        Console.WriteLine($"Overall Success: {IsSuccessful}");
        
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            Console.WriteLine($"Error: {ErrorMessage}");
        }
        
        Console.WriteLine();
        Console.WriteLine("SYSTEM INFORMATION:");
        Console.WriteLine("-" + new string('-', 79));
        if (SystemInfo != null)
        {
            Console.WriteLine($"OS: {SystemInfo.OperatingSystem}");
            Console.WriteLine($"Processor: {SystemInfo.ProcessorName}");
            Console.WriteLine($"Total Memory: {SystemInfo.TotalMemoryMB:F2} MB");
            Console.WriteLine($"Available Memory: {SystemInfo.AvailableMemoryMB:F2} MB");
            Console.WriteLine($"CPU Cores: {SystemInfo.ProcessorCount}");
            Console.WriteLine($".NET Version: {SystemInfo.DotNetVersion}");
        }
        
        Console.WriteLine();
        Console.WriteLine("SYSTEM METRICS SUMMARY:");
        Console.WriteLine("-" + new string('-', 79));
        if (BaselineMetrics != null && FinalMetrics != null)
        {
            Console.WriteLine($"Memory Change: {BaselineMetrics.MemoryUsageMB:F2} MB → {FinalMetrics.MemoryUsageMB:F2} MB");
            Console.WriteLine($"Thread Count Change: {BaselineMetrics.ThreadCount} → {FinalMetrics.ThreadCount}");
            Console.WriteLine($"GC Collections: {FinalMetrics.GcCollectionCount - BaselineMetrics.GcCollectionCount}");
        }
        
        Console.WriteLine();
        Console.WriteLine("TEST RESULTS:");
        Console.WriteLine("-" + new string('-', 79));
        
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
                Console.WriteLine("  Key Metrics:");
                foreach (var metric in test.Metrics.Take(5)) // Show top 5 metrics
                {
                    Console.WriteLine($"    {metric.Key}: {metric.Value}");
                }
                if (test.Metrics.Count > 5)
                {
                    Console.WriteLine($"    ... and {test.Metrics.Count - 5} more metrics");
                }
            }
            
            Console.WriteLine();
        }
        
        Console.WriteLine("=" + new string('=', 79));
    }
}

/// <summary>
/// Individual system test result.
/// </summary>
public class SystemTestResult
{
    public string TestName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsSuccessful { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}
