using Serilog;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Core;
using X3DCcdOptimizer.Logging;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer;

public class Program
{
    private const string Version = "0.1.0";

    public static void Main(string[] args)
    {
        PerformanceMonitor? perfMon = null;
        ProcessWatcher? processWatcher = null;

        try
        {
            // Step 1: Load config
            var config = AppConfig.Load();

            // Step 2: Initialize logger
            AppLogger.Initialize(config.Logging.Level);
            Log.Information("X3D Dual CCD Optimizer v{Version}", Version);

            // Step 3: Detect CCD topology
            CpuTopology topology;
            try
            {
                topology = CcdMapper.Detect(config);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to detect CCD topology — exiting");
                return;
            }

            Log.Information("CPU: {Model}", topology.CpuModel);
            Log.Information("CCD0 (V-Cache): Cores {Cores}, L3: {L3}MB, Mask: {Mask}",
                FormatCoreRange(topology.VCacheCores), topology.VCacheL3SizeMB, topology.VCacheMaskHex);
            Log.Information("CCD1 (Frequency): Cores {Cores}, L3: {L3}MB, Mask: {Mask}",
                FormatCoreRange(topology.FrequencyCores), topology.StandardL3SizeMB, topology.FrequencyMaskHex);

            // Step 4: Set up performance monitor
            perfMon = new PerformanceMonitor(topology, config.DashboardRefreshMs);
            perfMon.SnapshotReady += snapshots => PrintCoreStatus(snapshots, topology);
            perfMon.Start();

            // Step 5: Set up game detector + process watcher + affinity manager
            var gameDetector = new GameDetector(config.ManualGames);
            var affinityManager = new AffinityManager(topology, config.ProtectedProcesses);

            processWatcher = new ProcessWatcher(
                gameDetector,
                config.PollingIntervalMs,
                config.AutoDetection.RequireForeground);

            processWatcher.GameDetected += affinityManager.OnGameDetected;
            processWatcher.GameExited += affinityManager.OnGameExited;
            processWatcher.Start();

            Log.Information("Monitoring started. Polling: {Interval}ms. Manual games: {Count} entries.",
                config.PollingIntervalMs, gameDetector.GameCount);

            // Step 6: Wait for Ctrl+C
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Log.Information("Shutdown requested...");
            };

            try
            {
                Task.Delay(Timeout.Infinite, cts.Token).Wait();
            }
            catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
            {
                // Expected on Ctrl+C
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception");
        }
        finally
        {
            processWatcher?.Dispose();
            perfMon?.Dispose();
            Log.Information("X3D Dual CCD Optimizer stopped.");
            AppLogger.Shutdown();
        }
    }

    private static void PrintCoreStatus(CoreSnapshot[] snapshots, CpuTopology topology)
    {
        var ccd0 = snapshots.Where(s => s.CcdIndex == 0).OrderBy(s => s.CoreIndex).ToArray();
        var ccd1 = snapshots.Where(s => s.CcdIndex == 1).OrderBy(s => s.CoreIndex).ToArray();

        var line0 = string.Join(" ", ccd0.Select(s => $"C{s.CoreIndex}:{s.LoadPercent:F0}%"));
        var line1 = string.Join(" ", ccd1.Select(s => $"C{s.CoreIndex}:{s.LoadPercent:F0}%"));

        Log.Information("=== Core Status ===");
        Log.Information("CCD0 [V-Cache] {Cores}", line0);
        Log.Information("CCD1 [Freq]    {Cores}", line1);
    }

    private static string FormatCoreRange(int[] cores)
    {
        if (cores.Length == 0) return "none";
        if (cores.Length == 1) return cores[0].ToString();

        // Check if contiguous
        Array.Sort(cores);
        bool contiguous = true;
        for (int i = 1; i < cores.Length; i++)
        {
            if (cores[i] != cores[i - 1] + 1)
            {
                contiguous = false;
                break;
            }
        }

        return contiguous
            ? $"{cores[0]}-{cores[^1]}"
            : string.Join(",", cores);
    }
}
