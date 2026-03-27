using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Serilog;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Core;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly CpuTopology _topology;
    private readonly AffinityManager _affinityManager;
    private readonly AppConfig _config;
    private readonly DispatcherTimer _sessionTimer;

    private OperationMode _currentMode;
    private string _statusText = "";
    private SolidColorBrush _statusColor;
    private bool _isGameActive;
    private string _sessionDurationText = "";
    private string _currentGameName = "";
    private DateTime _sessionStart;
    private bool _isValidating;
    private string _validateButtonText = "Validate";
    private CancellationTokenSource? _validateCts;

    public CcdPanelViewModel Ccd0Panel { get; }
    public CcdPanelViewModel Ccd1Panel { get; }
    public ActivityLogViewModel ActivityLog { get; } = new();
    public ProcessRouterViewModel ProcessRouter { get; } = new();

    public OperationMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (SetProperty(ref _currentMode, value))
            {
                OnPropertyChanged(nameof(IsOptimizeMode));

                if (value == OperationMode.Optimize)
                    _affinityManager.SwitchToOptimize();
                else
                    _affinityManager.SwitchToMonitor();

                _config.OperationMode = value.ToString().ToLowerInvariant();
                _config.Save();

                UpdateStatus();
                UpdateBorders();
            }
        }
    }

    public bool IsOptimizeMode
    {
        get => _currentMode == OperationMode.Optimize;
        set => CurrentMode = value ? OperationMode.Optimize : OperationMode.Monitor;
    }

    public bool IsOptimizeEnabled { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public SolidColorBrush StatusColor
    {
        get => _statusColor;
        private set => SetProperty(ref _statusColor, value);
    }

    public bool IsGameActive
    {
        get => _isGameActive;
        private set
        {
            if (SetProperty(ref _isGameActive, value))
                OnPropertyChanged(nameof(SessionVisible));
        }
    }

    public Visibility SessionVisible => _isGameActive ? Visibility.Visible : Visibility.Collapsed;

    public string SessionDurationText
    {
        get => _sessionDurationText;
        private set => SetProperty(ref _sessionDurationText, value);
    }

    public string FooterText { get; }

    public RelayCommand ToggleModeCommand { get; }
    public RelayCommand ValidateCommand { get; }

    public bool IsValidating
    {
        get => _isValidating;
        private set => SetProperty(ref _isValidating, value);
    }

    public string ValidateButtonText
    {
        get => _validateButtonText;
        private set => SetProperty(ref _validateButtonText, value);
    }

    public MainViewModel(CpuTopology topology, PerformanceMonitor perfMon,
        ProcessWatcher processWatcher, GameDetector gameDetector,
        AffinityManager affinityManager, AppConfig config)
    {
        _topology = topology;
        _affinityManager = affinityManager;
        _config = config;
        _currentMode = affinityManager.Mode;

        IsOptimizeEnabled = topology.HasVCache;

        Ccd0Panel = new CcdPanelViewModel(topology, 0);
        Ccd1Panel = new CcdPanelViewModel(topology, 1);

        _statusColor = FindBrush("AccentBlueBrush");
        FooterText = $"v0.2.0 | {topology.CpuModel} | {topology.TotalLogicalCores} cores | Polling: {config.PollingIntervalMs}ms";

        ToggleModeCommand = new RelayCommand(
            () => IsOptimizeMode = !IsOptimizeMode,
            () => IsOptimizeEnabled);

        ValidateCommand = new RelayCommand(RunValidation, () => !_isValidating);

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _sessionStart;
            SessionDurationText = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}s"
                : $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        };

        UpdateStatus();
    }

    public void OnSnapshotReady(CoreSnapshot[] snapshots)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Ccd0Panel.UpdateSnapshots(snapshots);
            Ccd1Panel.UpdateSnapshots(snapshots);
        });
    }

    public void OnAffinityChanged(AffinityEvent evt)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            ActivityLog.AddEntry(evt);
            ProcessRouter.OnAffinityChanged(evt);
        });
    }

    public void OnGameDetected(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _currentGameName = game.Name;
            IsGameActive = true;
            _sessionStart = DateTime.Now;
            _sessionTimer.Start();

            Ccd0Panel.RoleLabel = _currentMode == OperationMode.Optimize
                ? $"Gaming — {game.Name}"
                : $"Observed — {game.Name}";
            Ccd1Panel.RoleLabel = _currentMode == OperationMode.Optimize
                ? "Background"
                : "Idle";

            UpdateStatus();
            UpdateBorders();
        });
    }

    public void OnGameExited(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _currentGameName = "";
            IsGameActive = false;
            _sessionTimer.Stop();
            SessionDurationText = "";

            Ccd0Panel.RoleLabel = "Idle";
            Ccd1Panel.RoleLabel = "Idle";
            ProcessRouter.Clear();

            UpdateStatus();
            UpdateBorders();
        });
    }

    private void RunValidation()
    {
        if (_isValidating) return;
        IsValidating = true;
        _validateCts = new CancellationTokenSource();
        var ct = _validateCts.Token;

        var cores = new[] { 0, 8 };
        var coreNames = new[] { "Core 0 (CCD0, V-Cache)", "Core 8 (CCD0, V-Cache)" };

        // Adjust if topology has different core indices
        if (_topology.VCacheCores.Length > 0)
            cores[0] = _topology.VCacheCores[0];
        if (_topology.VCacheCores.Length > 8)
            cores[1] = _topology.VCacheCores[8];
        else if (_topology.FrequencyCores.Length > 0)
            cores[1] = _topology.FrequencyCores[0];

        coreNames[0] = $"Core {cores[0]} (CCD{_topology.GetCcdIndex(cores[0])})";
        coreNames[1] = $"Core {cores[1]} (CCD{_topology.GetCcdIndex(cores[1])})";

        Log.Information("VALIDATE: Starting core mapping validation");

        Task.Run(() =>
        {
            try
            {
                for (int phase = 0; phase < cores.Length; phase++)
                {
                    if (ct.IsCancellationRequested) break;

                    int coreIndex = cores[phase];
                    string label = coreNames[phase];

                    Application.Current?.Dispatcher.BeginInvoke(() =>
                        ValidateButtonText = $"Burning {label}...");

                    Log.Information("VALIDATE: Burning {Core} for 5 seconds", label);

                    // Pin this thread to the target core
                    var handle = Process.GetCurrentProcess().Handle;
                    ulong mask = 1UL << coreIndex;
                    SetThreadAffinityMask(GetCurrentThread(), new IntPtr((long)mask));

                    // Burn for 5 seconds
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (ct.IsCancellationRequested) break;
                        // Spin — this burns 100% of the pinned core
                    }

                    // Reset thread affinity to all cores
                    SetThreadAffinityMask(GetCurrentThread(), new IntPtr(-1));

                    Log.Information("VALIDATE: Done burning {Core}", label);
                }
            }
            finally
            {
                // Reset affinity in case of exception
                SetThreadAffinityMask(GetCurrentThread(), new IntPtr(-1));

                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    ValidateButtonText = "Validate";
                    IsValidating = false;
                    Log.Information("VALIDATE: Validation complete — check dashboard for which cores lit up");
                });
            }
        }, ct);
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

    private void UpdateStatus()
    {
        if (_isGameActive)
        {
            if (_currentMode == OperationMode.Optimize)
            {
                StatusText = $"Optimize — {_currentGameName} pinned to V-Cache CCD";
                StatusColor = FindBrush("AccentGreenBrush");
            }
            else
            {
                StatusText = $"Monitor — observing {_currentGameName} on CCD0";
                StatusColor = FindBrush("AccentBlueBrush");
            }
        }
        else
        {
            if (_currentMode == OperationMode.Optimize)
            {
                StatusText = "Optimize — waiting for game";
                StatusColor = FindBrush("AccentPurpleBrush");
            }
            else
            {
                StatusText = "Monitor — observing CCD activity";
                StatusColor = FindBrush("AccentBlueBrush");
            }
        }
    }

    private void UpdateBorders()
    {
        int? gameCcd = _isGameActive ? 0 : null; // Game always targets CCD0 (V-Cache)
        Ccd0Panel.UpdateBorderState(_currentMode, _isGameActive, gameCcd);
        Ccd1Panel.UpdateBorderState(_currentMode, _isGameActive, _isGameActive ? 1 : null);
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Gray);
    }
}
