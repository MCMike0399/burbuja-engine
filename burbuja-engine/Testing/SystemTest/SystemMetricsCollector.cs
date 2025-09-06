using System.Diagnostics;
using System.Runtime;

namespace BurbujaEngine.Testing.SystemTest;

/// <summary>
/// Collects comprehensive system metrics for performance analysis during testing.
/// Provides detailed insights into CPU, memory, GC, threading, and process metrics.
/// </summary>
public class SystemMetricsCollector
{
    private readonly Process _currentProcess;
    
    public SystemMetricsCollector()
    {
        _currentProcess = Process.GetCurrentProcess();
    }
    
    /// <summary>
    /// Collect comprehensive system metrics.
    /// </summary>
    public async Task<SystemMetrics> CollectMetricsAsync()
    {
        var metrics = new SystemMetrics
        {
            Timestamp = DateTime.UtcNow,
            
            // Process metrics
            ProcessId = _currentProcess.Id,
            ProcessName = _currentProcess.ProcessName,
            
            // Memory metrics
            WorkingSetMB = _currentProcess.WorkingSet64 / (1024.0 * 1024.0),
            PrivateMemoryMB = _currentProcess.PrivateMemorySize64 / (1024.0 * 1024.0),
            VirtualMemoryMB = _currentProcess.VirtualMemorySize64 / (1024.0 * 1024.0),
            
            // Managed memory metrics
            ManagedMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
            Gen0CollectionCount = GC.CollectionCount(0),
            Gen1CollectionCount = GC.CollectionCount(1),
            Gen2CollectionCount = GC.CollectionCount(2),
            
            // Threading metrics
            ThreadCount = _currentProcess.Threads.Count,
            
            // CPU metrics (requires some time to calculate)
            CpuUsagePercent = await CalculateCpuUsageAsync(),
            
            // Handle metrics
            HandleCount = _currentProcess.HandleCount,
            
            // GC metrics
            IsServerGC = GCSettings.IsServerGC,
            LatencyMode = GCSettings.LatencyMode.ToString(),
            
            // Performance counters
            PagedMemoryMB = _currentProcess.PagedMemorySize64 / (1024.0 * 1024.0),
            NonPagedMemoryMB = _currentProcess.NonpagedSystemMemorySize64 / (1024.0 * 1024.0),
            
            // Timing metrics
            TotalProcessorTimeMs = _currentProcess.TotalProcessorTime.TotalMilliseconds,
            UserProcessorTimeMs = _currentProcess.UserProcessorTime.TotalMilliseconds,
            
            // Additional system info
            ProcessorCount = Environment.ProcessorCount,
            Is64BitProcess = Environment.Is64BitProcess,
            OSVersion = Environment.OSVersion.ToString(),
            WorkingDirectory = Environment.CurrentDirectory,
            
            // Runtime metrics
            RuntimeVersion = Environment.Version.ToString(),
            UptimeMs = Environment.TickCount64
        };
        
        // Collect additional .NET runtime metrics
        try
        {
            metrics.AllocatedBytesForCurrentThread = GC.GetAllocatedBytesForCurrentThread();
        }
        catch
        {
            // Not available in all .NET versions
            metrics.AllocatedBytesForCurrentThread = 0;
        }
        
        // Collect assembly metrics
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            metrics.LoadedAssemblyCount = assemblies.Length;
            metrics.LoadedModuleCount = assemblies.SelectMany(a => 
            {
                try { return a.GetModules(); }
                catch { return Array.Empty<System.Reflection.Module>(); }
            }).Count();
        }
        catch
        {
            metrics.LoadedAssemblyCount = 0;
            metrics.LoadedModuleCount = 0;
        }
        
        return metrics;
    }
    
    /// <summary>
    /// Calculate CPU usage percentage over a short period.
    /// </summary>
    private async Task<double> CalculateCpuUsageAsync()
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = _currentProcess.TotalProcessorTime;
            
            await Task.Delay(100); // Short delay to measure CPU usage
            
            var endTime = DateTime.UtcNow;
            var endCpuUsage = _currentProcess.TotalProcessorTime;
            
            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
            
            return cpuUsageTotal * 100;
        }
        catch
        {
            return 0.0; // Return 0 if calculation fails
        }
    }
    
    /// <summary>
    /// Calculate the delta between two sets of metrics.
    /// </summary>
    public SystemMetrics CalculateDelta(SystemMetrics before, SystemMetrics after)
    {
        return new SystemMetrics
        {
            Timestamp = after.Timestamp,
            ProcessId = after.ProcessId,
            ProcessName = after.ProcessName,
            
            // Calculate deltas
            WorkingSetMB = after.WorkingSetMB - before.WorkingSetMB,
            PrivateMemoryMB = after.PrivateMemoryMB - before.PrivateMemoryMB,
            VirtualMemoryMB = after.VirtualMemoryMB - before.VirtualMemoryMB,
            ManagedMemoryMB = after.ManagedMemoryMB - before.ManagedMemoryMB,
            
            Gen0CollectionCount = after.Gen0CollectionCount - before.Gen0CollectionCount,
            Gen1CollectionCount = after.Gen1CollectionCount - before.Gen1CollectionCount,
            Gen2CollectionCount = after.Gen2CollectionCount - before.Gen2CollectionCount,
            
            ThreadCount = after.ThreadCount - before.ThreadCount,
            HandleCount = after.HandleCount - before.HandleCount,
            
            CpuUsagePercent = after.CpuUsagePercent - before.CpuUsagePercent,
            
            PagedMemoryMB = after.PagedMemoryMB - before.PagedMemoryMB,
            NonPagedMemoryMB = after.NonPagedMemoryMB - before.NonPagedMemoryMB,
            
            TotalProcessorTimeMs = after.TotalProcessorTimeMs - before.TotalProcessorTimeMs,
            UserProcessorTimeMs = after.UserProcessorTimeMs - before.UserProcessorTimeMs,
            
            AllocatedBytesForCurrentThread = after.AllocatedBytesForCurrentThread - before.AllocatedBytesForCurrentThread,
            
            LoadedAssemblyCount = after.LoadedAssemblyCount - before.LoadedAssemblyCount,
            LoadedModuleCount = after.LoadedModuleCount - before.LoadedModuleCount,
            
            UptimeMs = after.UptimeMs - before.UptimeMs,
            
            // Copy static values from after
            IsServerGC = after.IsServerGC,
            LatencyMode = after.LatencyMode,
            ProcessorCount = after.ProcessorCount,
            Is64BitProcess = after.Is64BitProcess,
            OSVersion = after.OSVersion,
            WorkingDirectory = after.WorkingDirectory,
            RuntimeVersion = after.RuntimeVersion
        };
    }
    
    /// <summary>
    /// Get system memory information from the OS.
    /// </summary>
    public SystemMemoryInfo GetSystemMemoryInfo()
    {
        try
        {
            var totalMemory = GC.GetTotalMemory(false);
            
            // Try to get total physical memory (this is platform-specific)
            long totalPhysicalMemory = 0;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    totalPhysicalMemory = GetWindowsPhysicalMemory();
                }
                else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    totalPhysicalMemory = GetUnixPhysicalMemory();
                }
            }
            catch
            {
                // Ignore errors, we'll use 0 as fallback
            }
            
            return new SystemMemoryInfo
            {
                TotalManagedMemoryMB = totalMemory / (1024.0 * 1024.0),
                TotalPhysicalMemoryMB = totalPhysicalMemory / (1024.0 * 1024.0),
                ProcessWorkingSetMB = _currentProcess.WorkingSet64 / (1024.0 * 1024.0)
            };
        }
        catch (Exception ex)
        {
            return new SystemMemoryInfo
            {
                ErrorMessage = ex.Message
            };
        }
    }
    
    private long GetWindowsPhysicalMemory()
    {
        // This would require P/Invoke to Windows APIs
        // For now, return 0 as we don't want to add Windows-specific dependencies
        return 0;
    }
    
    private long GetUnixPhysicalMemory()
    {
        try
        {
            // Try reading from /proc/meminfo on Linux/macOS
            if (File.Exists("/proc/meminfo"))
            {
                var lines = File.ReadAllLines("/proc/meminfo");
                var totalLine = lines.FirstOrDefault(l => l.StartsWith("MemTotal:"));
                if (totalLine != null)
                {
                    var parts = totalLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                    {
                        return kb * 1024; // Convert from KB to bytes
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return 0;
    }
}

/// <summary>
/// Comprehensive system metrics data structure.
/// </summary>
public class SystemMetrics
{
    public DateTime Timestamp { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    
    // Memory metrics (in MB)
    public double WorkingSetMB { get; set; }
    public double PrivateMemoryMB { get; set; }
    public double VirtualMemoryMB { get; set; }
    public double ManagedMemoryMB { get; set; }
    public double PagedMemoryMB { get; set; }
    public double NonPagedMemoryMB { get; set; }
    
    // GC metrics
    public int Gen0CollectionCount { get; set; }
    public int Gen1CollectionCount { get; set; }
    public int Gen2CollectionCount { get; set; }
    public bool IsServerGC { get; set; }
    public string LatencyMode { get; set; } = string.Empty;
    public long AllocatedBytesForCurrentThread { get; set; }
    
    // Threading metrics
    public int ThreadCount { get; set; }
    
    // CPU metrics
    public double CpuUsagePercent { get; set; }
    public double TotalProcessorTimeMs { get; set; }
    public double UserProcessorTimeMs { get; set; }
    
    // Handle metrics
    public int HandleCount { get; set; }
    
    // Assembly metrics
    public int LoadedAssemblyCount { get; set; }
    public int LoadedModuleCount { get; set; }
    
    // System info
    public int ProcessorCount { get; set; }
    public bool Is64BitProcess { get; set; }
    public string OSVersion { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public long UptimeMs { get; set; }
    
    /// <summary>
    /// Get total GC collection count across all generations.
    /// </summary>
    public int GcCollectionCount => Gen0CollectionCount + Gen1CollectionCount + Gen2CollectionCount;
    
    /// <summary>
    /// Get total memory usage in MB.
    /// </summary>
    public double MemoryUsageMB => WorkingSetMB;
    
    /// <summary>
    /// Format metrics as a readable string.
    /// </summary>
    public override string ToString()
    {
        return $"CPU: {CpuUsagePercent:F1}%, Memory: {MemoryUsageMB:F1}MB, " +
               $"Threads: {ThreadCount}, GC: {GcCollectionCount}, Handles: {HandleCount}";
    }
}

/// <summary>
/// System memory information.
/// </summary>
public class SystemMemoryInfo
{
    public double TotalManagedMemoryMB { get; set; }
    public double TotalPhysicalMemoryMB { get; set; }
    public double ProcessWorkingSetMB { get; set; }
    public string? ErrorMessage { get; set; }
}
