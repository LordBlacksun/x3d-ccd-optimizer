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
    private bool _isOverlayVisible;

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

    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set => SetProperty(ref _isOverlayVisible, value);
    }

    public string OverlayButtonText => _isOverlayVisible ? "Hide Overlay" : "Show Overlay";

    public string FooterText { get; }

    public RelayCommand ToggleModeCommand { get; }
    public RelayCommand ToggleOverlayCommand { get; }

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

        ToggleOverlayCommand = new RelayCommand(() =>
        {
            IsOverlayVisible = !IsOverlayVisible;
            OnPropertyChanged(nameof(OverlayButtonText));
        });

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
        int? gameCcd = _isGameActive ? 0 : null;
        Ccd0Panel.UpdateBorderState(_currentMode, _isGameActive, gameCcd);
        Ccd1Panel.UpdateBorderState(_currentMode, _isGameActive, _isGameActive ? 1 : null);
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Gray);
    }
}
