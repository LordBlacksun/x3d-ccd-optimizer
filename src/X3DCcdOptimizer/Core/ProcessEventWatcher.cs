using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Serilog;

namespace X3DCcdOptimizer.Core;

/// <summary>
/// Subscribes to ETW kernel process start/stop events for near-instant game detection.
/// Falls back gracefully if ETW session creation fails (e.g., another session with the same name).
/// </summary>
public class ProcessEventWatcher : IDisposable
{
    private const string SessionName = "X3DCcdOptimizer-ProcessWatch";

    private TraceEventSession? _session;
    private ETWTraceEventSource? _source;
    private Thread? _processingThread;
    private volatile bool _disposed;

    public event Action<int, string>? ProcessStarted;
    public event Action<int>? ProcessStopped;

    /// <summary>True if the ETW session was successfully created.</summary>
    public bool IsActive { get; private set; }

    public bool Start()
    {
        try
        {
            // Clean up any orphaned session from a previous crash
            try { TraceEventSession.GetActiveSession(SessionName)?.Dispose(); }
            catch { }

            _session = new TraceEventSession(SessionName)
            {
                StopOnDispose = true
            };

            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);

            _source = _session.Source;
            _source.Kernel.ProcessStart += OnProcessStart;
            _source.Kernel.ProcessStop += OnProcessStop;

            _processingThread = new Thread(() =>
            {
                try
                {
                    _source.Process(); // Blocks until session is stopped
                }
                catch (Exception ex)
                {
                    if (!_disposed)
                        Log.Warning("ETW processing thread exited: {Error}", ex.Message);
                }
            })
            {
                Name = "ETW-ProcessWatch",
                IsBackground = true
            };
            _processingThread.Start();

            IsActive = true;
            Log.Information("ETW process watcher started — near-instant game detection enabled");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning("ETW session creation failed, falling back to polling: {Error}", ex.Message);
            Cleanup();
            IsActive = false;
            return false;
        }
    }

    private void OnProcessStart(ProcessTraceData data)
    {
        if (_disposed) return;
        try
        {
            var name = data.ProcessName;
            var pid = data.ProcessID;
            if (pid > 4 && !string.IsNullOrEmpty(name))
                ProcessStarted?.Invoke(pid, name);
        }
        catch (Exception ex)
        {
            Log.Debug("ETW ProcessStart handler error: {Error}", ex.Message);
        }
    }

    private void OnProcessStop(ProcessTraceData data)
    {
        if (_disposed) return;
        try
        {
            ProcessStopped?.Invoke(data.ProcessID);
        }
        catch (Exception ex)
        {
            Log.Debug("ETW ProcessStop handler error: {Error}", ex.Message);
        }
    }

    private void Cleanup()
    {
        try { _session?.Stop(); } catch { }
        try { _session?.Dispose(); } catch { }
        _session = null;
        _source = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Cleanup();
        GC.SuppressFinalize(this);
    }
}
