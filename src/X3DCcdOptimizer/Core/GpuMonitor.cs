using System.Management;
using Serilog;

namespace X3DCcdOptimizer.Core;

/// <summary>
/// Monitors per-process GPU usage via WMI performance counters.
/// Uses Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine for 3D engine utilization.
/// </summary>
public class GpuMonitor : IDisposable
{
    private bool _available;
    private bool _disposed;

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

        try
        {
            // Query GPU engine utilization for the specific process
            // The instance name format is: pid_PID_luid_0xHHHH_0xLLLL_phys_N_eng_N_engtype_3D
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                $"SELECT UtilizationPercentage, Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            searcher.Options.Timeout = TimeSpan.FromSeconds(2);

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
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
