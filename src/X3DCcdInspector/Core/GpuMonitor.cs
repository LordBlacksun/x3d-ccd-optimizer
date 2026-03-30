using System.Management;
using Serilog;

namespace X3DCcdInspector.Core;

/// <summary>
/// Monitors per-process GPU usage via WMI performance counters.
/// Uses Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine for 3D engine utilization.
/// </summary>
public class GpuMonitor : IDisposable
{
    private bool _available;
    private bool _disposed;
    private int _idleSkipCounter;
    private ManagementObjectSearcher? _cachedSearcher;

    /// <summary>
    /// Set to true when a game is actively detected. When false, GPU queries
    /// run at half frequency to reduce WMI overhead while idle.
    /// </summary>
    public bool IsGameActive { get; set; }

    public GpuMonitor()
    {
        _available = TestGpuCounters();
        if (_available)
            Log.Information("GPU monitoring available — auto-detection enabled");
        else
            Log.Warning("GPU performance counters unavailable — auto-detection disabled");
    }

    /// <summary>
    /// Gets the GPU 3D engine utilization percentage for a specific process.
    /// Returns 0 if not available or process not using GPU.
    /// </summary>
    public float GetGpuUsage(int pid)
    {
        if (!_available || _disposed) return 0;

        // When idle (no game detected), skip 3 of 4 queries to reduce WMI overhead
        if (!IsGameActive)
        {
            if (Interlocked.Increment(ref _idleSkipCounter) % 4 != 0)
                return 0;
        }

        try
        {
            // Reuse cached WMI searcher to avoid per-call connection overhead
            _cachedSearcher ??= new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT UtilizationPercentage, Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine")
            { Options = { Timeout = TimeSpan.FromSeconds(2) } };
            var searcher = _cachedSearcher;

            float totalUsage = 0;

            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    // Filter for this PID and 3D engine type
                    if (!name.Contains($"pid_{pid}_", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var usage = Convert.ToSingle(obj["UtilizationPercentage"]);
                    totalUsage += usage;
                }
                finally
                {
                    obj.Dispose();
                }
            }

            return totalUsage;
        }
        catch (Exception ex)
        {
            Log.Debug("GPU query failed for PID {Pid}: {Error}", pid, ex.Message);
            return 0;
        }
    }

    public bool IsAvailable => _available;

    private bool TestGpuCounters()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            searcher.Options.Timeout = TimeSpan.FromSeconds(3);

            // Just check if the class exists and has results
            foreach (ManagementObject obj in searcher.Get())
            {
                obj.Dispose();
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Debug("GPU counter test failed: {Error}", ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _cachedSearcher?.Dispose();
        _cachedSearcher = null;
        GC.SuppressFinalize(this);
    }
}
