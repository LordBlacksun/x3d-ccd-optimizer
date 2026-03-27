using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Serilog;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.Native;

namespace X3DCcdOptimizer.Core;

/// <summary>
/// Handles dirty shutdown recovery. Writes a recovery.json file while affinities are
/// engaged so that if the app crashes, the next launch can restore all processes to
/// their default affinity (all cores).
/// </summary>
public static class RecoveryManager
{
    private static readonly string RecoveryDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "X3DCCDOptimizer");

    private static readonly string RecoveryPath = Path.Combine(RecoveryDir, "recovery.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static RecoveryState? _currentState;
    private static readonly object _lock = new();

    /// <summary>
    /// Check if a dirty shutdown occurred and recover if needed.
    /// Call this before normal startup.
    /// </summary>
    public static void RecoverFromDirtyShutdown()
    {
        try
        {
            if (!File.Exists(RecoveryPath))
                return;

            Log.Warning("Recovery file found — previous session ended unexpectedly");

            RecoveryState? state;
            try
            {
                var json = File.ReadAllText(RecoveryPath);
                state = JsonSerializer.Deserialize<RecoveryState>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Recovery file corrupted — deleting and continuing");
                DeleteRecoveryFile();
                return;
            }

            if (state?.ModifiedProcesses == null || state.ModifiedProcesses.Count == 0)
            {
                Log.Information("Recovery file empty — no processes to restore");
                DeleteRecoveryFile();
                return;
            }

            Log.Information("Recovering {Count} processes from dirty shutdown (game was {Game})",
                state.ModifiedProcesses.Count, state.GameProcess);

            int restored = 0;
            int skipped = 0;

            // Get all running processes once
            var runningProcesses = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName;
                    if (!runningProcesses.ContainsKey(name))
                        runningProcesses[name] = [];
                    runningProcesses[name].Add(proc);
                }
                catch
                {
                    proc.Dispose();
                }
            }

            try
            {
                foreach (var entry in state.ModifiedProcesses)
                {
                    // Strip .exe for process name matching
                    var nameWithoutExe = entry.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? entry.Name[..^4] : entry.Name;

                    if (!runningProcesses.TryGetValue(nameWithoutExe, out var matches))
                    {
                        Log.Debug("Recovery: {Name} (PID {Pid}) — process no longer running, skipped",
                            entry.Name, entry.Pid);
                        skipped++;
                        continue;
                    }

                    // Reset affinity on all matching processes (PID may have changed after restart)
                    foreach (var proc in matches)
                    {
                        try
                        {
                            var handle = Kernel32.OpenProcess(
                                Kernel32.PROCESS_SET_INFORMATION, false, proc.Id);

                            if (handle == IntPtr.Zero)
                            {
                                skipped++;
                                continue;
                            }

                            try
                            {
                                // Reset to all cores (full system mask)
                                var fullMask = new IntPtr(-1);
                                if (Kernel32.SetProcessAffinityMask(handle, fullMask))
                                {
                                    restored++;
                                    Log.Information("Recovery: restored {Name} (PID {Pid}) to all cores",
                                        entry.Name, proc.Id);
                                }
                                else
                                {
                                    skipped++;
                                }
                            }
                            finally
                            {
                                Kernel32.CloseHandle(handle);
                            }
                        }
                        catch
                        {
                            skipped++;
                        }
                    }
                }
            }
            finally
            {
                // Dispose all cached processes
                foreach (var list in runningProcesses.Values)
                    foreach (var proc in list)
                        proc.Dispose();
            }

            Log.Information("Recovery complete: {Restored} restored, {Skipped} skipped/exited",
                restored, skipped);
            DeleteRecoveryFile();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Recovery failed — deleting recovery file and continuing");
            DeleteRecoveryFile();
        }
    }

    /// <summary>
    /// Called when Optimize mode engages a game. Creates the recovery file.
    /// </summary>
    public static void OnEngage(string gameName, int gamePid)
    {
        lock (_lock)
        {
            _currentState = new RecoveryState
            {
                Engaged = true,
                Timestamp = DateTime.UtcNow,
                GameProcess = gameName,
                GamePid = gamePid,
                ModifiedProcesses = []
            };
            WriteState();
        }
    }

    /// <summary>
    /// Called when a process is migrated. Adds it to the recovery file.
    /// </summary>
    public static void AddModifiedProcess(string name, int pid, IntPtr originalMask)
    {
        lock (_lock)
        {
            if (_currentState == null) return;

            _currentState.ModifiedProcesses.Add(new RecoveryProcessEntry
            {
                Name = name,
                Pid = pid,
                OriginalMask = $"0x{originalMask.ToInt64():X}"
            });
            WriteState();
        }
    }

    /// <summary>
    /// Called on clean disengage or clean exit. Deletes recovery file.
    /// </summary>
    public static void OnDisengage()
    {
        lock (_lock)
        {
            _currentState = null;
            DeleteRecoveryFile();
        }
    }

    public static bool IsRecoveryNeeded()
    {
        return File.Exists(RecoveryPath);
    }

    private static void WriteState()
    {
        try
        {
            Directory.CreateDirectory(RecoveryDir);
            var json = JsonSerializer.Serialize(_currentState, JsonOptions);
            File.WriteAllText(RecoveryPath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write recovery state");
        }
    }

    private static void DeleteRecoveryFile()
    {
        try
        {
            if (File.Exists(RecoveryPath))
                File.Delete(RecoveryPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete recovery file");
        }
    }
}
