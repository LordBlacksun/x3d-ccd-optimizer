using System.Diagnostics;
using Serilog;
using X3DCcdInspector.Models;
using X3DCcdInspector.Native;

namespace X3DCcdInspector.Core;

public class AffinityManager : IDisposable
{
    private readonly CpuTopology _topology;
    private readonly HashSet<string> _protectedProcesses;
    private readonly object _syncLock = new();
    private readonly List<AffinityEvent> _pendingEvents = [];
    private bool _engaged;
    private ProcessInfo? _currentGame;
    private volatile bool _disposed;

    // Saved original affinity for game pin restoration
    private IntPtr _savedAffinityMask;
    private int _pinnedGamePid;
    private string _pinnedGameName = "";

    private static readonly IReadOnlySet<string> HardcodedProtected = ProtectedProcesses.Names;

    // Critical system processes — never modify affinity even with admin rights.
    private static readonly HashSet<string> CriticalSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "csrss", "smss", "wininit", "winlogon", "services",
        "lsass", "dwm", "svchost", "fontdrvhost", "Memory Compression",
        "Registry", "dllhost", "conhost", "dasHost", "sihost", "taskhostw"
    };

    public static bool IsCriticalSystemProcess(string processName) =>
        CriticalSystemProcesses.Contains(processName);

    // Processes that participate in OS/driver game scheduling or GPU management.
    // These should never have their affinity modified.
    public static readonly IReadOnlySet<string> SchedulingInfrastructureProcesses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // AMD 3D V-Cache CCD scheduling
        "amd3dvcacheSvc", "amd3dvcacheUser",
        // Windows Game Bar / scheduling
        "GameBarPresenceWriter", "GameBar", "GameBarFTServer",
        "XboxGameBarWidgets", "gamingservices", "gamingservicesnet",
        // GPU driver services
        "NVDisplay.Container", "atiesrxx", "atieclxx",
        // Windows shell
        "explorer"
    };

    public event Action<AffinityEvent>? AffinityChanged;

    public ProcessInfo? CurrentGame
    {
        get { lock (_syncLock) return _currentGame; }
    }

    public bool IsEngaged
    {
        get { lock (_syncLock) return _engaged; }
    }

    public AffinityManager(CpuTopology topology, IEnumerable<string> configProtected)
    {
        _topology = topology;
        _protectedProcesses = new HashSet<string>(HardcodedProtected, StringComparer.OrdinalIgnoreCase);
        foreach (var name in SchedulingInfrastructureProcesses)
            _protectedProcesses.Add(name);

        foreach (var name in configProtected)
        {
            var clean = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;
            _protectedProcesses.Add(clean);
        }
    }

    public void OnGameDetected(ProcessInfo game)
    {
        lock (_syncLock)
        {
            if (_engaged)
            {
                Log.Warning("Already tracking a game — ignoring duplicate detection");
                return;
            }

            _engaged = true;
            _currentGame = game;

            Emit(AffinityAction.GameDetected, game.Name, game.Pid,
                "Game detected", game.DisplayName);
        }
        FlushEvents();
    }

    public void OnGameExited(ProcessInfo game)
    {
        lock (_syncLock)
        {
            if (!_engaged)
                return;

            _engaged = false;
            _currentGame = null;

            Emit(AffinityAction.GameExited, game.Name, game.Pid,
                "Game exited", game.DisplayName);
        }
        FlushEvents();
    }

    /// <summary>
    /// Checks whether a process name is in the protected list (scheduling infrastructure,
    /// critical system processes, or config-supplied protected processes).
    /// </summary>
    public bool IsProtectedProcess(string processName)
    {
        var clean = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;
        return _protectedProcesses.Contains(clean) || CriticalSystemProcesses.Contains(clean);
    }

    /// <summary>
    /// Pins the game process to the specified CCD via SetProcessAffinityMask.
    /// Only for fallback use when AMD driver is not available.
    /// </summary>
    public void PinGameToCcd(ProcessInfo game, string ccdTarget)
    {
        var displayName = game.DisplayName ?? game.Name;

        if (IsProtectedProcess(game.Name))
        {
            Log.Warning("Skipped affinity pin for protected process: {Name}", game.Name);
            lock (_syncLock)
                Emit(AffinityAction.Error, game.Name, game.Pid,
                    $"Skipped: {displayName} is a protected process", displayName);
            FlushEvents();
            return;
        }

        var targetMask = ccdTarget == "VCache" ? _topology.VCacheMask : _topology.FrequencyMask;
        if (targetMask == IntPtr.Zero)
        {
            Log.Warning("Cannot pin to {Ccd}: mask is zero", ccdTarget);
            return;
        }

        try
        {
            using var process = Process.GetProcessById(game.Pid);
            var handle = process.Handle;

            // Save original mask for restoration
            if (Kernel32.GetProcessAffinityMask(handle, out var originalMask, out _))
            {
                _savedAffinityMask = originalMask;
                _pinnedGamePid = game.Pid;
                _pinnedGameName = game.Name;
            }

            // Apply target CCD mask
            if (Kernel32.SetProcessAffinityMask(handle, targetMask))
            {
                var ccdLabel = ccdTarget == "VCache" ? "V-Cache CCD" : "Frequency CCD";
                var coreRange = ccdTarget == "VCache" && _topology.VCacheCores.Length > 0
                    ? $"cores {_topology.VCacheCores.Min()}-{_topology.VCacheCores.Max()}"
                    : _topology.FrequencyCores.Length > 0
                        ? $"cores {_topology.FrequencyCores.Min()}-{_topology.FrequencyCores.Max()}"
                        : "CCD cores";

                lock (_syncLock)
                    Emit(AffinityAction.AffinityPinApplied, game.Name, game.Pid,
                        $"Affinity pin applied: {displayName} \u2192 {ccdLabel} ({coreRange})", displayName);
                FlushEvents();

                Log.Information("Pinned {Game} (PID {Pid}) to {Ccd} (mask 0x{Mask:X})",
                    displayName, game.Pid, ccdLabel, targetMask.ToInt64());
            }
            else
            {
                Log.Warning("SetProcessAffinityMask failed for {Game} (PID {Pid})", game.Name, game.Pid);
            }
        }
        catch (ArgumentException)
        {
            Log.Debug("Process {Name} (PID {Pid}) exited before pin could be applied", game.Name, game.Pid);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warning(ex, "Access denied pinning {Name} (PID {Pid})", game.Name, game.Pid);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to pin {Name} (PID {Pid})", game.Name, game.Pid);
        }
    }

    /// <summary>
    /// Restores the game process's original affinity mask after game exit.
    /// </summary>
    public void RestoreGameAffinity(ProcessInfo game)
    {
        if (_pinnedGamePid == 0 || _savedAffinityMask == IntPtr.Zero)
            return;

        var displayName = game.DisplayName ?? game.Name;

        try
        {
            using var process = Process.GetProcessById(_pinnedGamePid);
            var handle = process.Handle;
            Kernel32.SetProcessAffinityMask(handle, _savedAffinityMask);

            Log.Information("Restored affinity for {Game} (PID {Pid})", displayName, _pinnedGamePid);
        }
        catch (ArgumentException)
        {
            Log.Debug("Process already exited — no affinity to restore for {Name}", game.Name);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to restore affinity for {Name} — process may have exited", game.Name);
        }

        lock (_syncLock)
            Emit(AffinityAction.AffinityPinRestored, game.Name, game.Pid,
                $"Affinity pin restored: {displayName} \u2192 original affinity", displayName);
        FlushEvents();

        _pinnedGamePid = 0;
        _savedAffinityMask = IntPtr.Zero;
        _pinnedGameName = "";
    }

    /// <summary>
    /// Returns the name of the last pinned game, or empty if no pin is active.
    /// Used by recovery to detect stale pins.
    /// </summary>
    public string PinnedGameName => _pinnedGameName;
    public int PinnedGamePid => _pinnedGamePid;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void Emit(AffinityAction action, string processName, int pid, string detail,
        string? displayName = null)
    {
        var evt = new AffinityEvent
        {
            Action = action,
            ProcessName = processName,
            DisplayName = displayName,
            Pid = pid,
            Detail = detail
        };

        switch (action)
        {
            case AffinityAction.GameDetected:
                Log.Information("GAME DETECTED: {Name} (PID {Pid}) {Detail}", processName, pid, detail);
                break;
            case AffinityAction.GameExited:
                Log.Information("GAME EXITED: {Name} (PID {Pid}) {Detail}", processName, pid, detail);
                break;
            case AffinityAction.Error:
                Log.Error("ERROR: {Name} (PID {Pid}) — {Detail}", processName, pid, detail);
                break;
            case AffinityAction.DetectionSkipped:
                Log.Information("DETECTION SKIPPED: {Name} (PID {Pid}) — {Detail}", processName, pid, detail);
                break;
        }

        _pendingEvents.Add(evt);
    }

    /// <summary>
    /// Fires all queued AffinityChanged events. Call OUTSIDE _syncLock to prevent deadlocks.
    /// </summary>
    private void FlushEvents()
    {
        List<AffinityEvent> events;
        lock (_syncLock)
        {
            if (_pendingEvents.Count == 0) return;
            events = [.. _pendingEvents];
            _pendingEvents.Clear();
        }

        foreach (var evt in events)
            AffinityChanged?.Invoke(evt);
    }
}
