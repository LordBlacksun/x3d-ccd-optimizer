using System.Runtime.InteropServices;
using Serilog;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.Native;

namespace X3DCcdOptimizer.Core;

public class PerformanceMonitor : IDisposable
{
    private readonly CpuTopology _topology;
    private readonly System.Timers.Timer _timer;
    private IntPtr _queryHandle;
    private readonly IntPtr[] _loadCounters;
    private readonly IntPtr[] _freqCounters;
    private readonly bool[] _freqAvailable;
    private readonly object _disposeLock = new();
    private volatile bool _disposed;
    private volatile bool _firstCollectionDone;

    public event Action<CoreSnapshot[]>? SnapshotReady;

    public PerformanceMonitor(CpuTopology topology, int intervalMs = 1000)
    {
        _topology = topology;
        _loadCounters = new IntPtr[topology.TotalLogicalCores];
        _freqCounters = new IntPtr[topology.TotalLogicalCores];
        _freqAvailable = new bool[topology.TotalLogicalCores];

        InitializePdhCounters();

        // Do an initial collection so the first real read has a baseline
        Pdh.PdhCollectQueryData(_queryHandle);
        _firstCollectionDone = false;

        _timer = new System.Timers.Timer(intervalMs);
        _timer.Elapsed += (_, _) => CollectAndPublish();
        _timer.AutoReset = true;
    }

    public void Start()
    {
        _timer.Start();
        Log.Information("Performance monitor started ({Interval}ms interval)", _timer.Interval);
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public CoreSnapshot[] CollectSnapshot()
    {
        int status = Pdh.PdhCollectQueryData(_queryHandle);
        if (status != 0)
        {
            Log.Warning("PdhCollectQueryData returned 0x{Status:X8}", status);
            return [];
        }

        var snapshots = new CoreSnapshot[_topology.TotalLogicalCores];

        for (int i = 0; i < _topology.TotalLogicalCores; i++)
        {
            double load = 0;
            double freq = 0;

            // Read load
            status = Pdh.PdhGetFormattedCounterValue(
                _loadCounters[i], Pdh.PDH_FMT_DOUBLE, out _, out var loadVal);
            if (status == 0 &&
                (loadVal.CStatus == Pdh.PDH_CSTATUS_VALID_DATA || loadVal.CStatus == Pdh.PDH_CSTATUS_NEW_DATA))
            {
                load = Math.Clamp(loadVal.doubleValue, 0, 100);
            }

            // Read frequency
            if (_freqAvailable[i])
            {
                status = Pdh.PdhGetFormattedCounterValue(
                    _freqCounters[i], Pdh.PDH_FMT_DOUBLE, out _, out var freqVal);
                if (status == 0 &&
                    (freqVal.CStatus == Pdh.PDH_CSTATUS_VALID_DATA || freqVal.CStatus == Pdh.PDH_CSTATUS_NEW_DATA))
                {
                    freq = freqVal.doubleValue;
                }
            }

            snapshots[i] = new CoreSnapshot
            {
                CoreIndex = i,
                CcdIndex = _topology.GetCcdIndex(i),
                LoadPercent = Math.Round(load, 1),
                FrequencyMHz = Math.Round(freq, 0)
            };
        }

        return snapshots;
    }

    private void CollectAndPublish()
    {
        if (_disposed) return;

        lock (_disposeLock)
        {
            if (_disposed) return;

            try
            {
                var snapshots = CollectSnapshot();

                if (!_firstCollectionDone)
                {
                    _firstCollectionDone = true;
                    return;
                }

                if (snapshots.Length > 0)
                    SnapshotReady?.Invoke(snapshots);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error collecting performance snapshot");
            }
        }
    }

    private void InitializePdhCounters()
    {
        int status = Pdh.PdhOpenQuery(null, IntPtr.Zero, out _queryHandle);
        if (status != 0)
            throw new InvalidOperationException($"PdhOpenQuery failed with 0x{status:X8}");

        for (int i = 0; i < _topology.TotalLogicalCores; i++)
        {
            // Load counter
            string loadPath = $@"\Processor Information(0,{i})\% Processor Utility";
            status = Pdh.PdhAddEnglishCounter(_queryHandle, loadPath, IntPtr.Zero, out _loadCounters[i]);
            if (status != 0)
            {
                // Fallback to % Processor Time
                loadPath = $@"\Processor Information(0,{i})\% Processor Time";
                status = Pdh.PdhAddEnglishCounter(_queryHandle, loadPath, IntPtr.Zero, out _loadCounters[i]);
                if (status != 0)
                    Log.Warning("Failed to add load counter for core {Core}: 0x{Status:X8}", i, status);
            }

            // Frequency counter
            string freqPath = $@"\Processor Information(0,{i})\Processor Frequency";
            status = Pdh.PdhAddEnglishCounter(_queryHandle, freqPath, IntPtr.Zero, out _freqCounters[i]);
            _freqAvailable[i] = status == 0;
            if (status != 0)
                Log.Debug("Frequency counter not available for core {Core}", i);
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;

            _timer.Stop();
            _timer.Dispose();

            if (_queryHandle != IntPtr.Zero)
            {
                Pdh.PdhCloseQuery(_queryHandle);
                _queryHandle = IntPtr.Zero;
            }
        }

        GC.SuppressFinalize(this);
    }
}
