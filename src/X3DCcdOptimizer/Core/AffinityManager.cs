using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.Native;

namespace X3DCcdOptimizer.Core;

public class AffinityManager : IDisposable
{
    private readonly CpuTopology _topology;
    private readonly HashSet<string> _protectedProcesses;
    private readonly Dictionary<int, (IntPtr Mask, string Name)> _originalMasks = new();
    private readonly HashSet<string> _loggedMigrateExes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncLock = new();
    private readonly List<AffinityEvent> _pendingEvents = [];
    private readonly OptimizeStrategy _strategy;
    private List<GameProfile> _gameProfiles = [];
    private readonly System.Timers.Timer? _reMigrationTimer;
    private bool _engaged;
#if DEBUG
    private readonly Stopwatch _migrateStopwatch = new();
#endif
    private ProcessInfo? _currentGame;
    private volatile bool _disposed;

    private static readonly IReadOnlySet<string> HardcodedProtected = ProtectedProcesses.Names;

    // Critical system processes — never modify affinity even with admin rights.
    // Belt-and-suspenders safety alongside AccessDeniedException handling.
    private static readonly HashSet<string> CriticalSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "csrss", "smss", "wininit", "winlogon", "services",
        "lsass", "dwm", "svchost", "fontdrvhost", "Memory Compression",
        "Registry", "dllhost", "conhost", "dasHost", "sihost", "taskhostw"
    };

    public static bool IsCriticalSystemProcess(string processName) =>
        CriticalSystemProcesses.Contains(processName);

    public event Action<AffinityEvent>? AffinityChanged;

    public OperationMode Mode { get; private set; }

    public ProcessInfo? CurrentGame
    {
        get { lock (_syncLock) return _currentGame; }
    }

    public bool IsEngaged
    {
        get { lock (_syncLock) return _engaged; }
    }

    private HashSet<string> _backgroundApps;

    public AffinityManager(CpuTopology topology, IEnumerable<string> configProtected, OperationMode initialMode,
        OptimizeStrategy strategy = OptimizeStrategy.AffinityPinning, IEnumerable<string>? backgroundApps = null)
    {
        _topology = topology;
        Mode = initialMode;
        _strategy = strategy;
        _protectedProcesses = new HashSet<string>(HardcodedProtected, StringComparer.OrdinalIgnoreCase);
        _backgroundApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in configProtected)
        {
            var clean = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;
            _protectedProcesses.Add(clean);
        }

        if (backgroundApps != null)
        {
            foreach (var name in backgroundApps)
            {
                var clean = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? name[..^4]
                    : name;
                _backgroundApps.Add(clean);
            }
        }

        // Background re-migration runs for both strategies — only game handling differs
        _reMigrationTimer = new System.Timers.Timer(5000);
        _reMigrationTimer.AutoReset = true;
        _reMigrationTimer.Elapsed += (_, _) => MigrateNewProcesses();
    }

    public void OnGameDetected(ProcessInfo game)
    {
        lock (_syncLock)
        {
            if (_engaged)
            {
                Log.Warning("Already engaged — ignoring duplicate game detection");
                return;
            }

            _engaged = true;
            _currentGame = game;
            _originalMasks.Clear();
            _loggedMigrateExes.Clear();

            var effectiveStrategy = GetEffectiveStrategy(game.Name);

            if (Mode == OperationMode.Optimize)
            {
                RecoveryManager.OnEngage(game.Name, game.Pid, effectiveStrategy);

                // Game handling depends on strategy (per-game profile or global)
                if (effectiveStrategy == OptimizeStrategy.DriverPreference)
                    EngageGameViaDriver(game);
                else
                    EngageGame(game);

                // Background migration runs for both strategies
                MigrateBackground(game.Pid);
                _reMigrationTimer?.Start();
            }
            else
            {
                // Monitor mode simulation
                if (effectiveStrategy == OptimizeStrategy.DriverPreference)
                    SimulateEngageViaDriver(game);
                else
                    SimulateEngage(game);

                SimulateMigrateBackground(game.Pid);
            }
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
            _reMigrationTimer?.Stop();

            if (Mode == OperationMode.Optimize)
            {
                // Restore game handling
                if (_strategy == OptimizeStrategy.DriverPreference)
                    RestoreDriver();

                // Restore background affinities (both strategies)
                RestoreAll();

                RecoveryManager.OnDisengage();
            }
            else
            {
                if (_strategy == OptimizeStrategy.DriverPreference)
                    Emit(AffinityAction.WouldRestoreDriver, "amd3dvcache", 0,
                        "game exited \u2014 would restore default CCD preference", "");

                Emit(AffinityAction.WouldRestore, "all", 0,
                    "game exited — would have restored all affinities");
            }

            _currentGame = null;
        }
        FlushEvents();
    }

    public void SwitchToOptimize()
    {
        lock (_syncLock)
        {
            if (Mode == OperationMode.Optimize)
                return;

            Mode = OperationMode.Optimize;
            Log.Information("Switched to Optimize mode");

            if (_engaged && _currentGame != null)
            {
                _originalMasks.Clear();
                _loggedMigrateExes.Clear();
                RecoveryManager.OnEngage(_currentGame.Name, _currentGame.Pid, _strategy);

                // Game handling depends on strategy
                if (_strategy == OptimizeStrategy.DriverPreference)
                    EngageGameViaDriver(_currentGame);
                else
                    EngageGame(_currentGame);

                // Background migration runs for both strategies
                MigrateBackground(_currentGame.Pid);
                _reMigrationTimer?.Start();
            }
        }
        FlushEvents();
    }

    public void SwitchToMonitor()
    {
        lock (_syncLock)
        {
            if (Mode == OperationMode.Monitor)
                return;

            _reMigrationTimer?.Stop();
            var wasEngagedAffinity = _engaged && _originalMasks.Count > 0;
            var wasEngagedDriver = _engaged && _strategy == OptimizeStrategy.DriverPreference;
            Mode = OperationMode.Monitor;
            Log.Information("Switched to Monitor mode");

            if (wasEngagedDriver)
            {
                RestoreDriver();
                RecoveryManager.OnDisengage();
            }
            else if (wasEngagedAffinity)
            {
                RestoreAll();
                RecoveryManager.OnDisengage();
            }
        }
        FlushEvents();
    }

    private void EngageGame(ProcessInfo game)
    {
        var handle = Kernel32.OpenProcess(
            Kernel32.PROCESS_SET_INFORMATION | Kernel32.PROCESS_QUERY_INFORMATION,
            false, game.Pid);

        if (handle == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            Emit(AffinityAction.Error, game.Name, game.Pid,
                $"Failed to open process — {FormatWin32Error(err)}", game.DisplayName);
            return;
        }

        try
        {
            if (Kernel32.GetProcessAffinityMask(handle, out var originalMask, out _))
            {
                _originalMasks[game.Pid] = (originalMask, game.Name);
            }
            else
            {
                Log.Warning("Failed to read affinity for {Name} (PID {Pid}), error {Err}",
                    game.Name, game.Pid, Marshal.GetLastWin32Error());
            }

            if (Kernel32.SetProcessAffinityMask(handle, _topology.VCacheMask))
            {
                RecoveryManager.AddModifiedProcess(game.Name, game.Pid,
                    _originalMasks.TryGetValue(game.Pid, out var saved) ? saved.Mask : new IntPtr(-1));
                Emit(AffinityAction.Engaged, game.Name, game.Pid,
                    $"→ CCD0 (V-Cache, mask {_topology.VCacheMaskHex})", game.DisplayName);
            }
            else
            {
                var err = Marshal.GetLastWin32Error();
                Emit(AffinityAction.Error, game.Name, game.Pid,
                    $"engage failed — {FormatWin32Error(err)}", game.DisplayName);
            }
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    private void MigrateBackground(int gamePid)
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch (Exception ex) { Log.Warning(ex, "Failed to enumerate processes for migration"); return; }

        foreach (var proc in processes)
        {
            try
            {
                if (proc.Id == gamePid || proc.Id <= 4)
                    continue;

                string name = proc.ProcessName;

                if (CriticalSystemProcesses.Contains(name))
                    continue;

                if (IsProtected(name))
                {
                    Emit(AffinityAction.Skipped, name + ".exe", proc.Id, "protected process");
                    continue;
                }

                var handle = Kernel32.OpenProcess(
                    Kernel32.PROCESS_SET_INFORMATION | Kernel32.PROCESS_QUERY_INFORMATION,
                    false, proc.Id);

                if (handle == IntPtr.Zero)
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err == 5)
                        Emit(AffinityAction.Skipped, name + ".exe", proc.Id, "Access Denied (process is protected)");
                    continue;
                }

                try
                {
                    if (Kernel32.GetProcessAffinityMask(handle, out var origMask, out _))
                    {
                        _originalMasks[proc.Id] = (origMask, name + ".exe");

                        if (Kernel32.SetProcessAffinityMask(handle, _topology.FrequencyMask))
                        {
                            RecoveryManager.AddModifiedProcess(name + ".exe", proc.Id, origMask);

                            // Only log first migration per exe name
                            if (_loggedMigrateExes.Add(name))
                            {
                                var reason = IsBackgroundApp(name) ? "rule" : "auto";
                                Emit(AffinityAction.Migrated, name + ".exe", proc.Id,
                                    $"\u2192 Frequency CCD ({reason})");
                            }
                        }
                    }
                }
                finally
                {
                    Kernel32.CloseHandle(handle);
                }
            }
            catch
            {
                // Skip processes we can't access
            }
            finally
            {
                proc.Dispose();
            }
        }
    }

    private void EngageGameViaDriver(ProcessInfo game)
    {
        if (VCacheDriverManager.SetCachePreferred())
        {
            Emit(AffinityAction.DriverSet, game.Name, game.Pid,
                "\u2014 V-Cache CCD preferred", game.DisplayName);
        }
        else
        {
            Emit(AffinityAction.Error, game.Name, game.Pid,
                "Failed to set V-Cache CCD preference", game.DisplayName);
        }
    }

    private void SimulateEngageViaDriver(ProcessInfo game)
    {
        Emit(AffinityAction.WouldSetDriver, game.Name, game.Pid,
            "\u2014 would prefer V-Cache CCD", game.DisplayName);
    }

    private void RestoreDriver()
    {
        if (VCacheDriverManager.RestoreDefault())
        {
            Emit(AffinityAction.DriverRestored, "amd3dvcache", 0,
                "Default preference restored (Frequency CCD)", "");
        }
        else
        {
            Emit(AffinityAction.Error, "amd3dvcache", 0,
                "Failed to restore default CCD preference", "");
        }
    }

    private void SimulateEngage(ProcessInfo game)
    {
        Emit(AffinityAction.WouldEngage, game.Name, game.Pid,
            $"→ CCD0 (V-Cache, mask {_topology.VCacheMaskHex})", game.DisplayName);
    }

    private void SimulateMigrateBackground(int gamePid)
    {
        int wouldMigrate = 0;

        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch (Exception ex) { Log.Warning(ex, "Failed to enumerate processes for simulation"); return; }

        foreach (var proc in processes)
        {
            try
            {
                if (proc.Id == gamePid || proc.Id <= 4)
                    continue;

                string name = proc.ProcessName;

                if (IsProtected(name))
                    continue;

                // Check if we can open it (to accurately report what would happen)
                var handle = Kernel32.OpenProcess(
                    Kernel32.PROCESS_QUERY_LIMITED_INFORMATION,
                    false, proc.Id);

                if (handle == IntPtr.Zero)
                    continue;

                Kernel32.CloseHandle(handle);
                wouldMigrate++;

                // Only emit individual events for the first few to avoid log spam
                if (wouldMigrate <= 5)
                {
                    Emit(AffinityAction.WouldMigrate, name + ".exe", proc.Id,
                        $"→ CCD1 (Frequency, mask {_topology.FrequencyMaskHex})");
                }
            }
            catch
            {
                // Skip
            }
            finally
            {
                proc.Dispose();
            }
        }

        if (wouldMigrate > 5)
        {
            Emit(AffinityAction.WouldMigrate, "...", 0,
                $"and {wouldMigrate - 5} more processes");
        }
    }

    private void RestoreAll()
    {
        int restored = 0;
        int exited = 0;
        int errors = 0;

        foreach (var (pid, (mask, cachedName)) in _originalMasks)
        {
            string processName = cachedName ?? "unknown";

            try
            {
                var handle = Kernel32.OpenProcess(
                    Kernel32.PROCESS_SET_INFORMATION, false, pid);

                if (handle == IntPtr.Zero)
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err == 87) // Invalid parameter — process exited
                    {
                        exited++;
                        continue;
                    }
                    Emit(AffinityAction.Error, processName, pid,
                        $"restore failed — {FormatWin32Error(err)}");
                    errors++;
                    continue;
                }

                try
                {
                    if (Kernel32.SetProcessAffinityMask(handle, mask))
                    {
                        restored++;
                    }
                    else
                    {
                        var err = Marshal.GetLastWin32Error();
                        Emit(AffinityAction.Error, processName, pid,
                            $"restore failed — {FormatWin32Error(err)}");
                        errors++;
                    }
                }
                finally
                {
                    Kernel32.CloseHandle(handle);
                }
            }
            catch
            {
                errors++;
            }
        }

        var summary = $"Restored {restored} processes.";
        if (exited > 0)
            summary += $" {exited} had already exited (normal).";
        if (errors > 0)
            summary += $" {errors} failed (see errors above).";

        Log.Information("RESTORE: {Summary}", summary);
        Emit(AffinityAction.Restored, "all", 0, summary);

        _originalMasks.Clear();
        _loggedMigrateExes.Clear();
    }

    public void UpdateGameProfiles(List<GameProfile> profiles)
    {
        lock (_syncLock)
        {
            _gameProfiles = profiles;
        }
    }

    private OptimizeStrategy GetEffectiveStrategy(string processName)
    {
        var clean = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;

        foreach (var profile in _gameProfiles)
        {
            var profileName = profile.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? profile.ProcessName[..^4] : profile.ProcessName;

            if (string.Equals(clean, profileName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(profile.Strategy, "global", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<OptimizeStrategy>(profile.Strategy, true, out var strategy))
                {
                    Log.Information("Using per-game profile for {Name}: {Strategy}", processName, strategy);
                    return strategy;
                }
            }
        }

        return _strategy;
    }

    public void UpdateBackgroundApps(IEnumerable<string> apps)
    {
        lock (_syncLock)
        {
            var newSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in apps)
            {
                var clean = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? name[..^4]
                    : name;
                newSet.Add(clean);
            }
            _backgroundApps = newSet;
        }
    }

    private bool IsBackgroundApp(string processName)
    {
        return _backgroundApps.Contains(processName);
    }

    private bool IsProtected(string processName)
    {
        return _protectedProcesses.Contains(processName);
    }

    private static string FormatWin32Error(int errorCode)
    {
        return errorCode switch
        {
            5 => "Access Denied (process is protected)",
            6 => "Invalid Handle (process exited)",
            87 => "Invalid Parameter",
            _ => new Win32Exception(errorCode).Message
        };
    }

    private void MigrateNewProcesses()
    {
        if (_disposed) return;

#if DEBUG
        _migrateStopwatch.Restart();
#endif

        lock (_syncLock)
        {
            if (!_engaged || Mode != OperationMode.Optimize || _currentGame == null)
                return;

            var gamePid = _currentGame.Pid;

            // Prune dead PIDs to prevent unbounded dictionary growth
            var deadPids = new List<int>();
            foreach (var pid in _originalMasks.Keys)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (p.HasExited)
                        deadPids.Add(pid);
                }
                catch (ArgumentException)
                {
                    deadPids.Add(pid);
                }
                catch
                {
                    // Keep entry if status unknown
                }
            }
            foreach (var pid in deadPids)
                _originalMasks.Remove(pid);

            // Scan for new processes to migrate
            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch { return; }

            foreach (var proc in processes)
            {
                try
                {
                    if (proc.Id == gamePid || proc.Id <= 4)
                        continue;

                    // Already handled
                    if (_originalMasks.ContainsKey(proc.Id))
                        continue;

                    string name = proc.ProcessName;

                    if (CriticalSystemProcesses.Contains(name))
                        continue;

                    if (IsProtected(name))
                        continue;

                    var handle = Kernel32.OpenProcess(
                        Kernel32.PROCESS_SET_INFORMATION | Kernel32.PROCESS_QUERY_INFORMATION,
                        false, proc.Id);

                    if (handle == IntPtr.Zero)
                        continue;

                    try
                    {
                        if (Kernel32.GetProcessAffinityMask(handle, out var origMask2, out _))
                        {
                            _originalMasks[proc.Id] = (origMask2, name + ".exe");

                            if (Kernel32.SetProcessAffinityMask(handle, _topology.FrequencyMask))
                            {
                                RecoveryManager.AddModifiedProcess(name + ".exe", proc.Id, origMask2);

                                // Only log first migration per exe name — subsequent child PIDs migrate silently
                                if (_loggedMigrateExes.Add(name))
                                {
                                    var reason = IsBackgroundApp(name) ? "rule" : "auto";
                                    Emit(AffinityAction.Migrated, name + ".exe", proc.Id,
                                        $"\u2192 Frequency CCD ({reason})");
                                }
                            }
                        }
                    }
                    finally
                    {
                        Kernel32.CloseHandle(handle);
                    }
                }
                catch (OutOfMemoryException) { throw; }
                catch
                {
                    // Skip processes we can't access
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
#if DEBUG
        _migrateStopwatch.Stop();
        if (_migrateStopwatch.ElapsedMilliseconds > 50)
            Log.Debug("AffinityManager.MigrateNewProcesses took {Ms}ms", _migrateStopwatch.ElapsedMilliseconds);
#endif
        FlushEvents();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _reMigrationTimer?.Stop();
        _reMigrationTimer?.Dispose();
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
            case AffinityAction.Engaged:
                Log.Information("ENGAGE: {Name} (PID {Pid}) {Detail}", processName, pid, detail);
                break;
            case AffinityAction.Migrated:
                Log.Information("MIGRATE: {Name} (PID {Pid}) {Detail}", processName, pid, detail);
                break;
            case AffinityAction.Restored:
                Log.Information("RESTORE: {Detail}", detail);
                break;
            case AffinityAction.Skipped:
                Log.Information("SKIP: {Name} (PID {Pid}) — {Detail}", processName, pid, detail);
                break;
            case AffinityAction.Error:
                Log.Error("ERROR: {Name} (PID {Pid}) — {Detail}", processName, pid, detail);
                break;
            case AffinityAction.WouldEngage:
                Log.Information("[MONITOR] WOULD ENGAGE: {Name} (PID {Pid}) {Detail}", processName, pid, detail);
                break;
            case AffinityAction.WouldMigrate:
                Log.Information("[MONITOR] WOULD MIGRATE: {Name} (PID {Pid}) {Detail}", processName, pid, detail);
                break;
            case AffinityAction.WouldRestore:
                Log.Information("[MONITOR] WOULD RESTORE: {Detail}", detail);
                break;
            case AffinityAction.DriverSet:
                Log.Information("DRIVER SET: {Name} (PID {Pid}) {Detail}", processName, pid, detail);
                break;
            case AffinityAction.DriverRestored:
                Log.Information("DRIVER RESTORE: {Detail}", detail);
                break;
            case AffinityAction.WouldSetDriver:
                Log.Information("[MONITOR] WOULD SET DRIVER: {Name} (PID {Pid}) {Detail}", processName, pid, detail);
                break;
            case AffinityAction.WouldRestoreDriver:
                Log.Information("[MONITOR] WOULD RESTORE DRIVER: {Detail}", detail);
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
