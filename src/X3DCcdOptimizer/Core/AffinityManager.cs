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
    private bool _engaged;

    private static readonly HashSet<string> HardcodedProtected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "csrss", "smss", "services", "wininit",
        "lsass", "winlogon", "dwm", "audiodg", "fontdrvhost",
        "Registry", "Memory Compression", "svchost",
        "X3DCcdOptimizer" // Don't touch ourselves
    };

    public event Action<AffinityEvent>? AffinityChanged;

    public AffinityManager(CpuTopology topology, IEnumerable<string> configProtected)
    {
        _topology = topology;
        _protectedProcesses = new HashSet<string>(HardcodedProtected, StringComparer.OrdinalIgnoreCase);

        foreach (var name in configProtected)
        {
            // Store without .exe for matching against ProcessName
            var clean = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;
            _protectedProcesses.Add(clean);
        }
    }

    public void OnGameDetected(ProcessInfo game)
    {
        if (_engaged)
        {
            Log.Warning("Already engaged — ignoring duplicate game detection");
            return;
        }

        _engaged = true;
        _originalMasks.Clear();

        // Pin the game to V-Cache CCD
        EngageGame(game);

        // Migrate background processes to Frequency CCD
        MigrateBackground(game.Pid);
    }

    public void OnGameExited(ProcessInfo game)
    {
        if (!_engaged)
            return;

        _engaged = false;
        RestoreAll();
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
            // Store original mask
            if (Kernel32.GetProcessAffinityMask(handle, out var originalMask, out _))
                _originalMasks[game.Pid] = originalMask;

            if (Kernel32.SetProcessAffinityMask(handle, _topology.VCacheMask))
            {
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
                    if (err == 5) // ACCESS_DENIED
                        Emit(AffinityAction.Skipped, name + ".exe", proc.Id, "access denied");
                    continue;
                }

                try
                {
                    // Store original mask
                    if (Kernel32.GetProcessAffinityMask(handle, out var originalMask, out _))
                    {
                        _originalMasks[proc.Id] = originalMask;

                        if (Kernel32.SetProcessAffinityMask(handle, _topology.FrequencyMask))
                        {
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
                    {
                        restored++;
                    }
                    else
                    {
                        failed++;
                    }
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

        // Log based on action type
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
        }

        AffinityChanged?.Invoke(evt);
    }
}
