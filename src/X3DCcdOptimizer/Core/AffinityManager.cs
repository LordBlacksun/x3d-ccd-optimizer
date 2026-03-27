using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.Native;

namespace X3DCcdOptimizer.Core;

public class AffinityManager
{
    private readonly CpuTopology _topology;
    private readonly HashSet<string> _protectedProcesses;
    private readonly Dictionary<int, IntPtr> _originalMasks = new();
    private readonly object _syncLock = new();
    private bool _engaged;
    private ProcessInfo? _currentGame;

    private static readonly HashSet<string> HardcodedProtected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "csrss", "smss", "services", "wininit",
        "lsass", "winlogon", "dwm", "audiodg", "fontdrvhost",
        "Registry", "Memory Compression", "svchost",
        "X3DCcdOptimizer" // Don't touch ourselves
    };

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

    public AffinityManager(CpuTopology topology, IEnumerable<string> configProtected, OperationMode initialMode)
    {
        _topology = topology;
        Mode = initialMode;
        _protectedProcesses = new HashSet<string>(HardcodedProtected, StringComparer.OrdinalIgnoreCase);

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
                Log.Warning("Already engaged — ignoring duplicate game detection");
                return;
            }

            _engaged = true;
            _currentGame = game;
            _originalMasks.Clear();

            if (Mode == OperationMode.Optimize)
            {
                RecoveryManager.OnEngage(game.Name, game.Pid);
                EngageGame(game);
                MigrateBackground(game.Pid);
            }
            else
            {
                SimulateEngage(game);
                SimulateMigrateBackground(game.Pid);
            }
        }
    }

    public void OnGameExited(ProcessInfo game)
    {
        lock (_syncLock)
        {
            if (!_engaged)
                return;

            _engaged = false;

            if (Mode == OperationMode.Optimize)
            {
                RestoreAll();
                RecoveryManager.OnDisengage();
            }
            else
            {
                Emit(AffinityAction.WouldRestore, "all", 0,
                    "game exited — would have restored all affinities");
            }

            _currentGame = null;
        }
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
                RecoveryManager.OnEngage(_currentGame.Name, _currentGame.Pid);
                EngageGame(_currentGame);
                MigrateBackground(_currentGame.Pid);
            }
        }
    }

    public void SwitchToMonitor()
    {
        lock (_syncLock)
        {
            if (Mode == OperationMode.Monitor)
                return;

            var wasEngaged = _engaged && _originalMasks.Count > 0;
            Mode = OperationMode.Monitor;
            Log.Information("Switched to Monitor mode");

            if (wasEngaged)
            {
                RestoreAll();
                RecoveryManager.OnDisengage();
            }
        }
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
                $"Failed to open process (error {err})");
            return;
        }

        try
        {
            if (Kernel32.GetProcessAffinityMask(handle, out var originalMask, out _))
            {
                _originalMasks[game.Pid] = originalMask;
            }
            else
            {
                Log.Warning("Failed to read affinity for {Name} (PID {Pid}), error {Err}",
                    game.Name, game.Pid, Marshal.GetLastWin32Error());
            }

            if (Kernel32.SetProcessAffinityMask(handle, _topology.VCacheMask))
            {
                RecoveryManager.AddModifiedProcess(game.Name, game.Pid,
                    _originalMasks.GetValueOrDefault(game.Pid, new IntPtr(-1)));
                Emit(AffinityAction.Engaged, game.Name, game.Pid,
                    $"→ CCD0 (V-Cache, mask {_topology.VCacheMaskHex})");
            }
            else
            {
                var err = Marshal.GetLastWin32Error();
                Emit(AffinityAction.Error, game.Name, game.Pid,
                    $"SetProcessAffinityMask failed (error {err})");
            }
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    private void MigrateBackground(int gamePid)
    {
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id == gamePid || proc.Id <= 4)
                    continue;

                string name = proc.ProcessName;

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
                        Emit(AffinityAction.Skipped, name + ".exe", proc.Id, "access denied");
                    continue;
                }

                try
                {
                    if (Kernel32.GetProcessAffinityMask(handle, out var originalMask, out _))
                    {
                        _originalMasks[proc.Id] = originalMask;

                        if (Kernel32.SetProcessAffinityMask(handle, _topology.FrequencyMask))
                        {
                            RecoveryManager.AddModifiedProcess(name + ".exe", proc.Id, originalMask);
                            Emit(AffinityAction.Migrated, name + ".exe", proc.Id,
                                $"→ CCD1 (Frequency, mask {_topology.FrequencyMaskHex})");
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

    private void SimulateEngage(ProcessInfo game)
    {
        Emit(AffinityAction.WouldEngage, game.Name, game.Pid,
            $"→ CCD0 (V-Cache, mask {_topology.VCacheMaskHex})");
    }

    private void SimulateMigrateBackground(int gamePid)
    {
        int wouldMigrate = 0;

        foreach (var proc in Process.GetProcesses())
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
        int failed = 0;

        foreach (var (pid, originalMask) in _originalMasks)
        {
            try
            {
                var handle = Kernel32.OpenProcess(
                    Kernel32.PROCESS_SET_INFORMATION, false, pid);

                if (handle == IntPtr.Zero)
                {
                    failed++;
                    continue;
                }

                try
                {
                    if (Kernel32.SetProcessAffinityMask(handle, originalMask))
                        restored++;
                    else
                        failed++;
                }
                finally
                {
                    Kernel32.CloseHandle(handle);
                }
            }
            catch
            {
                failed++;
            }
        }

        Log.Information("RESTORE: {Restored} processes restored, {Failed} failed/exited",
            restored, failed);

        Emit(AffinityAction.Restored, "all", 0,
            $"{restored} restored, {failed} failed/exited");

        _originalMasks.Clear();
    }

    private bool IsProtected(string processName)
    {
        return _protectedProcesses.Contains(processName);
    }

    private void Emit(AffinityAction action, string processName, int pid, string detail)
    {
        var evt = new AffinityEvent
        {
            Action = action,
            ProcessName = processName,
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
        }

        AffinityChanged?.Invoke(evt);
    }
}
