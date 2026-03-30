using System.Diagnostics;
using Serilog;
using X3DCcdInspector.Models;
using X3DCcdInspector.Native;

namespace X3DCcdInspector.Core;

/// <summary>
/// Central polling service that detects system state for the dashboard and overlay.
/// Owns two timers: a state timer (7s) for driver/GameBar/thread mapping, and a
/// foreground timer (1.5s) for overlay game-only visibility.
/// </summary>
public class SystemStateMonitor : IDisposable
{
    private readonly CpuTopology _topology;
    private readonly System.Timers.Timer _stateTimer;
    private readonly System.Timers.Timer _foregroundTimer;
    private readonly object _syncLock = new();

    private volatile bool _disposed;
    private int _trackedGamePid;
    private string _trackedGameName = "";
    private bool _gameActive;

    // Previous state for change detection
    private SystemState? _previousState;
    private bool _previousForeground;

    public event Action<SystemState>? StateChanged;
    public event Action<AffinityEvent>? SystemEvent;
    public event Action<bool>? ForegroundChanged;

    public SystemStateMonitor(CpuTopology topology, int stateIntervalMs = 7000, int foregroundIntervalMs = 1500)
    {
        _topology = topology;

        _stateTimer = new System.Timers.Timer(stateIntervalMs);
        _stateTimer.Elapsed += (_, _) => PollState();
        _stateTimer.AutoReset = true;

        _foregroundTimer = new System.Timers.Timer(foregroundIntervalMs);
        _foregroundTimer.Elapsed += (_, _) => PollForeground();
        _foregroundTimer.AutoReset = true;
    }

    public void Start()
    {
        _stateTimer.Start();
        // Do an immediate first poll so dashboard has data on startup
        _ = System.Threading.Tasks.Task.Run(PollState);
        Log.Information("SystemStateMonitor started (state: {StateMs}ms, foreground: {FgMs}ms)",
            _stateTimer.Interval, _foregroundTimer.Interval);
    }

    public void Stop()
    {
        _stateTimer.Stop();
        _foregroundTimer.Stop();
    }

    public void OnGameDetected(ProcessInfo game)
    {
        lock (_syncLock)
        {
            _trackedGamePid = game.Pid;
            _trackedGameName = game.DisplayName ?? game.Name;
            _gameActive = true;
        }
        _foregroundTimer.Start();
        // Immediately poll state to get thread distribution
        _ = System.Threading.Tasks.Task.Run(PollState);
    }

    public void OnGameExited(ProcessInfo game)
    {
        lock (_syncLock)
        {
            _trackedGamePid = 0;
            _trackedGameName = "";
            _gameActive = false;
            _previousForeground = false;
        }
        _foregroundTimer.Stop();

        // Fire foreground false so overlay hides
        ForegroundChanged?.Invoke(false);
    }

    private void PollState()
    {
        if (_disposed) return;

        try
        {
            var isDriverInstalled = VCacheDriverManager.IsDriverAvailable;
            var isDriverServiceRunning = CheckServiceRunning();
            var driverPreference = VCacheDriverManager.GetCurrentPreference();
            var isGameBarRunning = CheckGameBarRunning();

            int ccd0Threads = 0, ccd1Threads = 0;
            string activeCcd = "Unknown";
            bool gameActive;
            int gamePid;

            lock (_syncLock)
            {
                gameActive = _gameActive;
                gamePid = _trackedGamePid;
            }

            if (gameActive && gamePid > 0)
            {
                (ccd0Threads, ccd1Threads) = MapThreadsToCcd(gamePid);
                activeCcd = DetermineActiveCcd(ccd0Threads, ccd1Threads);
            }

            var gameModeStatus = DetermineGameMode(driverPreference, gameActive, isDriverInstalled);

            var newState = new SystemState
            {
                IsDriverInstalled = isDriverInstalled,
                IsDriverServiceRunning = isDriverServiceRunning,
                DriverPreference = driverPreference,
                IsGameBarRunning = isGameBarRunning,
                GameModeStatus = gameModeStatus,
                IsGameForeground = _previousForeground,
                Ccd0ThreadCount = ccd0Threads,
                Ccd1ThreadCount = ccd1Threads,
                ActiveCcd = activeCcd
            };

            // Detect discrete state changes and emit events (skip first poll)
            if (_previousState != null)
                EmitChangeEvents(_previousState, newState);

            _previousState = newState;
            StateChanged?.Invoke(newState);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SystemStateMonitor poll error");
        }
    }

    private void PollForeground()
    {
        if (_disposed) return;

        try
        {
            int gamePid;
            bool gameActive;
            lock (_syncLock)
            {
                gamePid = _trackedGamePid;
                gameActive = _gameActive;
            }

            if (!gameActive || gamePid == 0)
                return;

            var foregroundHwnd = User32.GetForegroundWindow();
            if (foregroundHwnd == IntPtr.Zero)
                return;

            User32.GetWindowThreadProcessId(foregroundHwnd, out var foregroundPid);
            var isGameForeground = foregroundPid == gamePid;

            if (isGameForeground != _previousForeground)
            {
                _previousForeground = isGameForeground;
                ForegroundChanged?.Invoke(isGameForeground);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SystemStateMonitor foreground poll error");
        }
    }

    private static bool CheckServiceRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("amd3dvcacheSvc");
            var running = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            return running;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckGameBarRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("GameBarPresenceWriter");
            var running = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            return running;
        }
        catch
        {
            return false;
        }
    }

    private (int ccd0, int ccd1) MapThreadsToCcd(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var handle = process.Handle;

            if (!Kernel32.GetProcessAffinityMask(handle, out var processMask, out _))
                return (0, 0);

            var mask = (long)processMask;
            var vcacheMask = _topology.VCacheMask.ToInt64();
            var freqMask = _topology.FrequencyMask.ToInt64();

            // Count threads in the process (total)
            var totalThreads = process.Threads.Count;

            // Determine which CCDs the process is allowed to run on
            var onCcd0 = (mask & vcacheMask) != 0;
            var onCcd1 = (mask & freqMask) != 0;

            if (onCcd0 && onCcd1)
            {
                // Process has access to both CCDs — estimate distribution
                // by assuming threads spread proportionally to allowed cores
                var ccd0Cores = CountBits(mask & vcacheMask);
                var ccd1Cores = CountBits(mask & freqMask);
                var totalCores = ccd0Cores + ccd1Cores;
                if (totalCores == 0) return (0, 0);

                var ccd0Threads = (int)Math.Round((double)totalThreads * ccd0Cores / totalCores);
                var ccd1Threads = totalThreads - ccd0Threads;
                return (ccd0Threads, ccd1Threads);
            }
            else if (onCcd0)
            {
                return (totalThreads, 0);
            }
            else if (onCcd1)
            {
                return (0, totalThreads);
            }
        }
        catch (ArgumentException)
        {
            // Process exited between detection and enumeration
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to map threads to CCD for PID {Pid}", pid);
        }

        return (0, 0);
    }

    private static int CountBits(long value)
    {
        int count = 0;
        while (value != 0)
        {
            count += (int)(value & 1);
            value >>= 1;
        }
        return count;
    }

    private static string DetermineActiveCcd(int ccd0Threads, int ccd1Threads)
    {
        if (ccd0Threads == 0 && ccd1Threads == 0)
            return "Unknown";

        var total = ccd0Threads + ccd1Threads;
        var ccd0Pct = (double)ccd0Threads / total;

        if (ccd0Pct >= 0.7) return "CCD0";
        if (ccd0Pct <= 0.3) return "CCD1";
        return "Both";
    }

    private static string DetermineGameMode(int? driverPreference, bool gameActive, bool driverInstalled)
    {
        if (!driverInstalled)
            return "Unknown";
        if (gameActive && driverPreference == 1) // PREFER_CACHE
            return "Active";
        return "Inactive";
    }

    private void EmitChangeEvents(SystemState prev, SystemState newState)
    {
        // Driver preference changed
        if (newState.DriverPreference != prev.DriverPreference)
        {
            var fromText = prev.DriverPreference switch
            {
                0 => "PREFER_FREQ",
                1 => "PREFER_CACHE",
                _ => "N/A"
            };
            var toText = newState.DriverPreference switch
            {
                0 => "PREFER_FREQ",
                1 => "PREFER_CACHE",
                _ => "N/A"
            };

            SystemEvent?.Invoke(new AffinityEvent
            {
                Action = AffinityAction.DriverStateChanged,
                ProcessName = "amd3dvcache",
                Detail = $"AMD driver state changed: {fromText} \u2192 {toText}"
            });
        }

        // Game Bar status changed
        if (newState.IsGameBarRunning != prev.IsGameBarRunning)
        {
            SystemEvent?.Invoke(new AffinityEvent
            {
                Action = AffinityAction.GameBarStatus,
                ProcessName = "GameBarPresenceWriter",
                Detail = $"Xbox Game Bar: {(newState.IsGameBarRunning ? "Running" : "Not Running")}"
            });
        }

        // CCD observation — log significant thread distribution changes when game active
        bool gameActive;
        string gameName;
        lock (_syncLock)
        {
            gameActive = _gameActive;
            gameName = _trackedGameName;
        }

        if (gameActive && newState.ActiveCcd != "Unknown" && newState.ActiveCcd != prev.ActiveCcd
            && prev.ActiveCcd != "Unknown")
        {
            SystemEvent?.Invoke(new AffinityEvent
            {
                Action = AffinityAction.CcdObservation,
                ProcessName = gameName,
                Detail = $"{gameName}: {newState.Ccd0ThreadCount} threads CCD0 (V-Cache), " +
                         $"{newState.Ccd1ThreadCount} threads CCD1 (Frequency)"
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stateTimer.Stop();
        _foregroundTimer.Stop();
        _stateTimer.Dispose();
        _foregroundTimer.Dispose();

        GC.SuppressFinalize(this);
    }
}
