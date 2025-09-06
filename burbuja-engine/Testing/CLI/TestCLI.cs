using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using BurbujaEngine.Testing.SystemTest;
using BurbujaEngine.Testing.StressTest;

namespace BurbujaEngine.Testing.CLI;

/// <summary>
/// Command-line interface for running BurbujaEngine tests.
/// Replaces the shell script with a comprehensive C# test runner.
/// </summary>
public static class TestCLI
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("BurbujaEngine Test Runner")
        {
            Description = "Comprehensive testing suite for BurbujaEngine priority system and functionality"
        };
        
        // Add system test command
        var systemTestCommand = new Command("system", "Run system tests against running engine")
        {
            new Option<string>("--url", () => "http://localhost:5220", "Base URL for the engine"),
            new Option<bool>("--verbose", () => false, "Enable verbose logging"),
            new Option<string>("--output", () => "", "Output file for test results (JSON format)")
        };
        
        systemTestCommand.SetHandler(async (string url, bool verbose, string output) =>
        {
            await RunSystemTests(url, verbose, output);
        }, systemTestCommand.Options.Cast<Option<string>>().First(),
           systemTestCommand.Options.Cast<Option<bool>>().First(),
           systemTestCommand.Options.Cast<Option<string>>().Last());
        
        // Add stress test command
        var stressTestCommand = new Command("stress", "Run comprehensive stress tests")
        {
            new Option<bool>("--verbose", () => false, "Enable verbose logging"),
            new Option<string>("--output", () => "", "Output file for test results (JSON format)"),
            new Option<int>("--iterations", () => 1, "Number of stress test iterations to run")
        };
        
        stressTestCommand.SetHandler(async (bool verbose, string output, int iterations) =>
        {
            await RunStressTests(verbose, output, iterations);
        }, stressTestCommand.Options.Cast<Option<bool>>().First(),
           stressTestCommand.Options.Cast<Option<string>>().First(),
           stressTestCommand.Options.Cast<Option<int>>().First());
        
        // Add combined test command
        var allTestsCommand = new Command("all", "Run all tests (system + stress)")
        {
            new Option<string>("--url", () => "http://localhost:5220", "Base URL for the engine"),
            new Option<bool>("--verbose", () => false, "Enable verbose logging"),
            new Option<string>("--output", () => "", "Output file for test results (JSON format)")
        };
        
        allTestsCommand.SetHandler(async (string url, bool verbose, string output) =>
        {
            await RunAllTests(url, verbose, output);
        }, allTestsCommand.Options.Cast<Option<string>>().First(),
           allTestsCommand.Options.Cast<Option<bool>>().First(),
           allTestsCommand.Options.Cast<Option<string>>().Last());
        
        rootCommand.AddCommand(systemTestCommand);
        rootCommand.AddCommand(stressTestCommand);
        rootCommand.AddCommand(allTestsCommand);
        
        return await rootCommand.InvokeAsync(args);
    }
    
    private static async Task RunSystemTests(string url, bool verbose, string outputFile)
    {
        Console.WriteLine("Starting BurbujaEngine System Tests");
        Console.WriteLine($"Target URL: {url}");
        Console.WriteLine();
        
        var services = CreateServiceCollection(verbose);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<EngineSystemTestRunner>>();
        
        var testRunner = new EngineSystemTestRunner(logger, null);
        
        try
        {
            var report = await testRunner.RunCompleteTestSuiteAsync();
            
            // Print report to console
            report.PrintReport();
            
            // Save to file if specified
            if (!string.IsNullOrEmpty(outputFile))
            {
                await SaveReportToFile(report, outputFile, "system");
                Console.WriteLine($"Report saved to: {outputFile}");
            }
            
            Environment.Exit(report.IsSuccessful ? 0 : 1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: System test failed: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.ToString());
            }
            Environment.Exit(1);
        }
    }
    
    private static async Task RunStressTests(bool verbose, string outputFile, int iterations)
    {
        Console.WriteLine("Starting BurbujaEngine Stress Tests");
        Console.WriteLine($"Iterations: {iterations}");
        Console.WriteLine();
        
        var services = CreateServiceCollection(verbose);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PriorityStressTest>>();
        
        var allReports = new List<StressTestReport>();
        
        try
        {
            for (int i = 0; i < iterations; i++)
            {
                if (iterations > 1)
                {
                    Console.WriteLine($"--- Iteration {i + 1}/{iterations} ---");
                }
                
                var stressTest = new PriorityStressTest(logger);
                var report = await stressTest.RunStressTestAsync();
                
                allReports.Add(report);
                
                // Print report for this iteration
                report.PrintReport();
                
                if (i < iterations - 1)
                {
                    Console.WriteLine("Waiting 5 seconds before next iteration...");
                    await Task.Delay(5000);
                }
            }
            
            // Print summary if multiple iterations
            if (iterations > 1)
            {
                PrintStressTestSummary(allReports);
            }
            
            // Save to file if specified
            if (!string.IsNullOrEmpty(outputFile))
            {
                var reportToSave = iterations == 1 ? allReports[0] : CreateCombinedReport(allReports);
                await SaveReportToFile(reportToSave, outputFile, "stress");
                Console.WriteLine($"Report saved to: {outputFile}");
            }
            
            var allSuccessful = allReports.All(r => r.IsSuccessful);
            Environment.Exit(allSuccessful ? 0 : 1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: Stress test failed: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.ToString());
            }
            Environment.Exit(1);
        }
    }
    
    private static async Task RunAllTests(string url, bool verbose, string outputFile)
    {
        Console.WriteLine("Starting ALL BurbujaEngine Tests");
        Console.WriteLine($"Target URL: {url}");
        Console.WriteLine();
        
        var services = CreateServiceCollection(verbose);
        var serviceProvider = services.BuildServiceProvider();
        
        try
        {
            // Run system tests first
            Console.WriteLine("PHASE 1: System Tests");
            Console.WriteLine("=".PadRight(50, '='));
            
            var systemLogger = serviceProvider.GetRequiredService<ILogger<EngineSystemTestRunner>>();
            var systemTestRunner = new EngineSystemTestRunner(systemLogger, null);
            var systemReport = await systemTestRunner.RunCompleteTestSuiteAsync();
            
            systemReport.PrintReport();
            
            if (!systemReport.IsSuccessful)
            {
                Console.WriteLine("FAILED: System tests failed. Skipping stress tests.");
                Environment.Exit(1);
            }
            
            Console.WriteLine();
            Console.WriteLine("PHASE 2: Stress Tests");
            Console.WriteLine("=".PadRight(50, '='));
            
            // Run stress tests
            var stressLogger = serviceProvider.GetRequiredService<ILogger<PriorityStressTest>>();
            var stressTest = new PriorityStressTest(stressLogger);
            var stressReport = await stressTest.RunStressTestAsync();
            
            stressReport.PrintReport();
            
            // Save combined results if specified
            if (!string.IsNullOrEmpty(outputFile))
            {
                var combinedReport = new
                {
                    timestamp = DateTime.UtcNow,
                    system_tests = systemReport,
                    stress_tests = stressReport,
                    overall_success = systemReport.IsSuccessful && stressReport.IsSuccessful
                };
                
                await SaveReportToFile(combinedReport, outputFile, "combined");
                Console.WriteLine($"Combined report saved to: {outputFile}");
            }
            
            // Print final summary
            Console.WriteLine();
            Console.WriteLine("FINAL SUMMARY");
            Console.WriteLine("=".PadRight(50, '='));
            Console.WriteLine($"System Tests: {(systemReport.IsSuccessful ? "PASS" : "FAIL")}");
            Console.WriteLine($"Stress Tests: {(stressReport.IsSuccessful ? "PASS" : "FAIL")}");
            
            var overallSuccess = systemReport.IsSuccessful && stressReport.IsSuccessful;
            Console.WriteLine($"Overall: {(overallSuccess ? "PASS" : "FAIL")}");
            
            Environment.Exit(overallSuccess ? 0 : 1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: Test suite failed: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.ToString());
            }
            Environment.Exit(1);
        }
    }
    
    private static ServiceCollection CreateServiceCollection(bool verbose)
    {
        var services = new ServiceCollection();
        
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            
            if (verbose)
            {
                builder.SetMinimumLevel(LogLevel.Debug);
            }
            else
            {
                builder.SetMinimumLevel(LogLevel.Information);
            }
        });
        
        return services;
    }
    
    private static async Task SaveReportToFile(object report, string filePath, string testType)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
            
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Could not save report to file: {ex.Message}");
        }
    }
    
    private static void PrintStressTestSummary(List<StressTestReport> reports)
    {
        Console.WriteLine();
        Console.WriteLine("STRESS TEST SUMMARY");
        Console.WriteLine("=".PadRight(50, '='));
        
        var successCount = reports.Count(r => r.IsSuccessful);
        var averageDuration = reports.Average(r => r.TotalDuration.TotalSeconds);
        
        Console.WriteLine($"Total Iterations: {reports.Count}");
        Console.WriteLine($"Successful: {successCount}/{reports.Count} ({(double)successCount / reports.Count * 100:F1}%)");
        Console.WriteLine($"Average Duration: {averageDuration:F2} seconds");
        
        if (reports.Any(r => r.SystemMetricsDelta != null))
        {
            var avgCpuChange = reports.Where(r => r.SystemMetricsDelta != null)
                                   .Average(r => r.SystemMetricsDelta!.CpuUsagePercent);
            var avgMemoryChange = reports.Where(r => r.SystemMetricsDelta != null)
                                        .Average(r => r.SystemMetricsDelta!.MemoryUsageMB);
            
            Console.WriteLine($"Average CPU Change: {avgCpuChange:F2}%");
            Console.WriteLine($"Average Memory Change: {avgMemoryChange:F2} MB");
        }
    }
    
    private static StressTestReport CreateCombinedReport(List<StressTestReport> reports)
    {
        return new StressTestReport
        {
            StartTime = reports.First().StartTime,
            EndTime = reports.Last().EndTime,
            TotalDuration = TimeSpan.FromMilliseconds(reports.Sum(r => r.TotalDuration.TotalMilliseconds)),
            IsSuccessful = reports.All(r => r.IsSuccessful),
            TestResults = reports.SelectMany(r => r.TestResults).ToList(),
            ErrorMessage = string.Join("; ", reports.Where(r => !string.IsNullOrEmpty(r.ErrorMessage))
                                                   .Select(r => r.ErrorMessage))
        };
    }
}
