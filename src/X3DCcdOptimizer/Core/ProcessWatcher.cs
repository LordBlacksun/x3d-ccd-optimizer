using System.Diagnostics;
using Serilog;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.Native;

namespace X3DCcdOptimizer.Core;

public class ProcessWatcher : IDisposable
{
    private readonly GameDetector _detector;
    private readonly GpuMonitor? _gpuMonitor;
    private readonly bool _requireForeground;
    private readonly bool _autoDetectEnabled;
    private readonly int _gpuThreshold;
    private readonly int _detectionDelaySec;
    private readonly int _exitDelaySec;
    private readonly System.Timers.Timer _timer;
    private volatile bool _disposed;

    // Auto-detection debounce state
    private int _autoDetectCandidatePid;
    private string _autoDetectCandidateName = "";
    private DateTime _autoDetectCandidateStart;
    private DateTime _autoDetectExitStart;
    private bool _autoDetectExitPending;

    // PIDs already reported as below-threshold (don't spam every poll cycle)
    private readonly HashSet<int> _belowThresholdReported = new();

    public event Action<ProcessInfo>? GameDetected;
    public event Action<ProcessInfo>? GameExited;
    public event Action<AffinityEvent>? DetectionSkipped;

    public ProcessWatcher(GameDetector detector, int pollingIntervalMs = 2000,
        bool requireForeground = true, bool autoDetectEnabled = true,
        int gpuThreshold = 50, int detectionDelaySec = 5, int exitDelaySec = 10,
        GpuMonitor? gpuMonitor = null)
    {
        _detector = detector;
        _requireForeground = requireForeground;
        _autoDetectEnabled = autoDetectEnabled && gpuMonitor?.IsAvailable == true;
        _gpuThreshold = gpuThreshold;
        _detectionDelaySec = detectionDelaySec;
        _exitDelaySec = exitDelaySec;
        _gpuMonitor = gpuMonitor;

        _timer = new System.Timers.Timer(pollingIntervalMs);
        _timer.Elapsed += (_, _) => Poll();
        _timer.AutoReset = true;
    }

    public void Start()
    {
        _timer.Start();
        Log.Information("Process watcher started. Polling: {Interval}ms. Manual games: {Count} entries. Auto-detect: {Auto}",
            _timer.Interval, _detector.GameCount, _autoDetectEnabled);
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private void Poll()
    {
        if (_disposed) return;

        try
        {
            // Check if currently tracked game is still running
            if (_detector.CurrentGame is { } current)
            {
                bool stillRunning = false;
                try
                {
                    using var proc = Process.GetProcessById(current.Pid);
                    stillRunning = !proc.HasExited;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }

                if (!stillRunning)
                {
                    // For auto-detected games, apply exit delay
                    if (current.Method == DetectionMethod.Auto && _exitDelaySec > 0)
                    {
                        if (!_autoDetectExitPending)
                        {
                            _autoDetectExitPending = true;
                            _autoDetectExitStart = DateTime.UtcNow;
                            return; // Wait for delay
                        }

                        if ((DateTime.UtcNow - _autoDetectExitStart).TotalSeconds < _exitDelaySec)
                            return; // Still waiting
                    }

                    _autoDetectExitPending = false;
                    HandleGameExit(current);
                    return;
                }
                else
                {
                    _autoDetectExitPending = false;
                }

                // For auto-detected games, also check if GPU usage dropped (handles alt-tab/loading screens)
                if (current.Method == DetectionMethod.Auto && _gpuMonitor != null)
                {
                    var gpuNow = _gpuMonitor.GetGpuUsage(current.Pid);
                    var isForeground = IsForeground(current.Pid);

                    if (gpuNow < _gpuThreshold && !isForeground)
                    {
                        if (!_autoDetectExitPending)
                        {
                            _autoDetectExitPending = true;
                            _autoDetectExitStart = DateTime.UtcNow;
                        }
                        else if ((DateTime.UtcNow - _autoDetectExitStart).TotalSeconds >= _exitDelaySec)
                        {
                            _autoDetectExitPending = false;
                            HandleGameExit(current);
                        }
                        return;
                    }
                    else
                    {
                        _autoDetectExitPending = false;
                    }
                }

                return; // Game still tracked, don't scan
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

            // Scan running processes — check manual list and known DB first
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string name = proc.ProcessName;
                    var method = _detector.CheckGame(name);

                    if (method != null)
                    {
                        if (_requireForeground && foregroundPid != 0 && proc.Id != (int)foregroundPid)
                            continue;

                        var source = method switch
                        {
                            DetectionMethod.Manual => "manual",
                            DetectionMethod.Database => "database",
                            _ => "unknown"
                        };

                        var info = new ProcessInfo
                        {
                            Name = name + ".exe",
                            Pid = proc.Id,
                            DetectionSource = $"[{source}]",
                            Method = method.Value
                        };

                        _detector.CurrentGame = info;
                        ResetAutoDetectState();
                        Log.Information("GAME DETECTED: {Name} (PID {Pid}) {Source}",
                            info.Name, info.Pid, info.DetectionSource);
                        GameDetected?.Invoke(info);
                        return;
                    }
                }
                catch (Exception ex) { Log.Debug("Skipping process during scan: {Error}", ex.Message); }
                finally
                {
                    proc.Dispose();
                }
            }

            // GPU heuristic auto-detection (lowest priority)
            if (_autoDetectEnabled && _gpuMonitor != null && foregroundPid > 0)
            {
                TryAutoDetect((int)foregroundPid);
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
                Log.Warning(ex, "Error during process scan");
        }
    }

    private void TryAutoDetect(int foregroundPid)
    {
        try
        {
            using var proc = Process.GetProcessById(foregroundPid);
            var name = proc.ProcessName;

            // Skip excluded processes
            if (_detector.IsExcluded(name))
            {
                ResetAutoDetectState();
                return;
            }

            // Check GPU usage
            var gpuUsage = _gpuMonitor!.GetGpuUsage(foregroundPid);
            if (gpuUsage < _gpuThreshold)
            {
                // Report below-threshold once per PID per session
                if (gpuUsage > 0 && !_belowThresholdReported.Contains(foregroundPid))
                {
                    _belowThresholdReported.Add(foregroundPid);
                    Log.Debug("Foreground process {Name} using {Gpu:F0}% GPU (below {Threshold}% threshold)",
                        name, gpuUsage, _gpuThreshold);
                    DetectionSkipped?.Invoke(new AffinityEvent
                    {
                        Action = AffinityAction.DetectionSkipped,
                        ProcessName = name + ".exe",
                        Pid = foregroundPid,
                        Detail = $"GPU: {gpuUsage:F0}% (below {_gpuThreshold}% threshold)"
                    });
                }
                ResetAutoDetectState();
                return;
            }

            // Debounce: track candidate
            if (_autoDetectCandidatePid != foregroundPid)
            {
                _autoDetectCandidatePid = foregroundPid;
                _autoDetectCandidateName = name;
                _autoDetectCandidateStart = DateTime.UtcNow;
                return; // Start timing
            }

            // Check if debounce period elapsed
            if ((DateTime.UtcNow - _autoDetectCandidateStart).TotalSeconds < _detectionDelaySec)
                return; // Still waiting

            // Auto-detected!
            var info = new ProcessInfo
            {
                Name = name + ".exe",
                Pid = foregroundPid,
                DetectionSource = $"[auto-detected, GPU: {gpuUsage:F0}%]",
                Method = DetectionMethod.Auto,
                GpuUsage = gpuUsage
            };

            _detector.CurrentGame = info;
            ResetAutoDetectState();
            Log.Information("GAME DETECTED: {Name} (PID {Pid}) {Source}",
                info.Name, info.Pid, info.DetectionSource);
            GameDetected?.Invoke(info);
        }
        catch (ArgumentException) { }
        catch (Exception ex) { Log.Debug("Auto-detect error for PID {Pid}: {Error}", foregroundPid, ex.Message); }
    }

    private void ResetAutoDetectState()
    {
        if (_autoDetectCandidatePid != 0)
            _belowThresholdReported.Remove(_autoDetectCandidatePid);
        _autoDetectCandidatePid = 0;
        _autoDetectCandidateName = "";
    }

    private bool IsForeground(int pid)
    {
        var hwnd = User32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        User32.GetWindowThreadProcessId(hwnd, out var fgPid);
        return (int)fgPid == pid;
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
