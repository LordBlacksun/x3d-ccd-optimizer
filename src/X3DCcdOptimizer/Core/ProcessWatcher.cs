using System.Diagnostics;
using Serilog;
using X3DCcdOptimizer.Native;

namespace X3DCcdOptimizer.Core;

public class ProcessWatcher : IDisposable
{
    private readonly GameDetector _detector;
    private readonly bool _requireForeground;
    private readonly System.Timers.Timer _timer;
    private bool _disposed;

    public event Action<ProcessInfo>? GameDetected;
    public event Action<ProcessInfo>? GameExited;

    public ProcessWatcher(GameDetector detector, int pollingIntervalMs = 2000, bool requireForeground = true)
    {
        _detector = detector;
        _requireForeground = requireForeground;
        _timer = new System.Timers.Timer(pollingIntervalMs);
        _timer.Elapsed += (_, _) => Poll();
        _timer.AutoReset = true;
    }

    public void Start()
    {
        _timer.Start();
        Log.Information("Process watcher started. Polling: {Interval}ms. Manual games: {Count} entries.",
            _timer.Interval, _detector.GameCount);
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private void Poll()
    {
        try
        {
            // Check if currently tracked game is still running
            if (_detector.CurrentGame is { } current)
            {
                try
                {
                    using var proc = Process.GetProcessById(current.Pid);
                    if (proc.HasExited)
                    {
                        HandleGameExit(current);
                        return;
                    }
                }
                catch (ArgumentException)
                {
                    HandleGameExit(current);
                    return;
                }
                catch (InvalidOperationException)
                {
                    HandleGameExit(current);
                    return;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    HandleGameExit(current);
                    return;
                }
            }

            // If we already have a tracked game, don't scan for new ones
            if (_detector.CurrentGame != null)
                return;

            // Get foreground window PID
            uint foregroundPid = 0;
            if (_requireForeground)
            {
                var hwnd = User32.GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                    User32.GetWindowThreadProcessId(hwnd, out foregroundPid);
            }

            // Scan running processes
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string name = proc.ProcessName;
                    if (!_detector.IsGame(name))
                        continue;

                    // If requireForeground is enabled, only trigger if the game is in the foreground
                    if (_requireForeground && foregroundPid != 0 && proc.Id != (int)foregroundPid)
                        continue;

                    var info = new ProcessInfo
                    {
                        Name = name + ".exe",
                        Pid = proc.Id,
                        DetectionSource = "manual list"
                    };

                    _detector.CurrentGame = info;
                    Log.Information("GAME DETECTED: {Name} (PID {Pid}) [{Source}]",
                        info.Name, info.Pid, info.DetectionSource);
                    GameDetected?.Invoke(info);
                    return;
                }
                catch
                {
                    // Access denied for some processes — skip
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during process scan");
        }
    }

    private void HandleGameExit(ProcessInfo game)
    {
        _detector.CurrentGame = null;
        Log.Information("GAME EXITED: {Name} (PID {Pid})", game.Name, game.Pid);
        GameExited?.Invoke(game);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }
}
