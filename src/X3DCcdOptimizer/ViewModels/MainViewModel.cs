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
    private string _currentGameDisplayName = "";
    private DateTime _sessionStart;
    private bool _isOverlayVisible;

    public CcdPanelViewModel Ccd0Panel { get; }
    public CcdPanelViewModel? Ccd1Panel { get; }
    public bool ShowSecondPanel { get; }
    public ActivityLogViewModel ActivityLog { get; } = new();
    public ProcessRouterViewModel ProcessRouter { get; }

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
    public RelayCommand OpenSettingsCommand { get; }

    // Expose for settings window creation
    public CpuTopology Topology => _topology;
    public AppConfig Config => _config;

    public string? PowerPlanWarning { get; }

    public MainViewModel(CpuTopology topology, PerformanceMonitor perfMon,
        ProcessWatcher processWatcher, GameDetector gameDetector,
        AffinityManager affinityManager, AppConfig config,
        string? powerPlanWarning = null)
    {
        _topology = topology;
        _affinityManager = affinityManager;
        _config = config;
        _currentMode = affinityManager.Mode;
        PowerPlanWarning = powerPlanWarning;

        IsOptimizeEnabled = topology.IsDualCcd;
        ShowSecondPanel = topology.IsDualCcd;

        Ccd0Panel = new CcdPanelViewModel(topology, 0);
        Ccd1Panel = topology.IsDualCcd ? new CcdPanelViewModel(topology, 1) : null;

        // Process router with CCD group names
        var ccd0Name = Ccd0Panel.BadgeText;
        var ccd1Name = Ccd1Panel?.BadgeText ?? "";
        ProcessRouter = new ProcessRouterViewModel(ccd0Name, ccd1Name);

        _statusColor = FindBrush("AccentBlueBrush");
        FooterText = $"v1.0.0 | {topology.CpuModel} | {topology.TotalPhysicalCores} cores | {topology.TotalLogicalCores} threads | Polling: {config.PollingIntervalMs}ms";

        ToggleModeCommand = new RelayCommand(
            () => IsOptimizeMode = !IsOptimizeMode,
            () => IsOptimizeEnabled);

        ToggleOverlayCommand = new RelayCommand(() =>
        {
            IsOverlayVisible = !IsOverlayVisible;
            OnPropertyChanged(nameof(OverlayButtonText));
        });

        OpenSettingsCommand = new RelayCommand(() =>
        {
            // Check if a Settings window is already open
            foreach (System.Windows.Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Views.SettingsWindow existing)
                {
                    existing.Activate();
                    return;
                }
            }

            var settingsVm = new SettingsViewModel(config, topology);
            var settingsWindow = new Views.SettingsWindow
            {
                DataContext = settingsVm
            };
            settingsWindow.Show();
        });

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _sessionStart;
            SessionDurationText = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}s"
                : $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        };

        if (config.IsFirstRun && topology.IsDualCcd)
            StatusText = "Monitor mode \u2014 observing your CPU without making changes. Switch to Optimize to pin games to V-Cache.";
        else
            UpdateStatus();
    }

    public void OnSnapshotReady(CoreSnapshot[] snapshots)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Ccd0Panel.UpdateSnapshots(snapshots);
            Ccd1Panel?.UpdateSnapshots(snapshots);
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
            _currentGameDisplayName = game.DisplayName ?? game.Name;
            IsGameActive = true;
            _sessionStart = DateTime.Now;
            _sessionTimer.Start();

            var display = _currentGameDisplayName;
            var strat = _config.GetOptimizeStrategy();
            if (_topology.IsSingleCcd)
            {
                Ccd0Panel.RoleLabel = $"Active \u2014 {display}";
            }
            else if (_currentMode == OperationMode.Optimize)
            {
                Ccd0Panel.RoleLabel = _topology.Tier == ProcessorTier.DualCcdStandard
                    ? $"Pinned \u2014 {display}"
                    : strat == OptimizeStrategy.DriverPreference
                        ? $"V-Cache Preferred \u2014 {display}"
                        : $"Gaming \u2014 {display}";
                if (Ccd1Panel != null)
                    Ccd1Panel.RoleLabel = strat == OptimizeStrategy.DriverPreference
                        ? "Standard"
                        : "Background";
            }
            else
            {
                Ccd0Panel.RoleLabel = $"Observed \u2014 {display}";
                if (Ccd1Panel != null)
                    Ccd1Panel.RoleLabel = "Idle";
            }

            UpdateStatus();
            UpdateBorders();
        });
    }

    public void OnGameExited(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _currentGameName = "";
            _currentGameDisplayName = "";
            IsGameActive = false;
            _sessionTimer.Stop();
            SessionDurationText = "";

            Ccd0Panel.RoleLabel = "Idle";
            if (Ccd1Panel != null) Ccd1Panel.RoleLabel = "Idle";
            ProcessRouter.Clear();

            UpdateStatus();
            UpdateBorders();
        });
    }

    private void UpdateStatus()
    {
        var strategy = _config.GetOptimizeStrategy();
        var display = _currentGameDisplayName;

        if (_isGameActive)
        {
            if (_topology.IsSingleCcd)
            {
                StatusText = _topology.Tier == ProcessorTier.SingleCcdX3D
                    ? $"Monitor \u2014 {display} on V-Cache CCD"
                    : $"Monitor \u2014 {display} on CCD 0";
                StatusColor = FindBrush("AccentBlueBrush");
            }
            else if (_currentMode == OperationMode.Optimize)
            {
                StatusText = _topology.Tier == ProcessorTier.DualCcdStandard
                    ? $"Optimize \u2014 {display} pinned to CCD 0"
                    : strategy == OptimizeStrategy.DriverPreference
                        ? $"Optimize \u2014 {display} V-Cache preferred (driver)"
                        : $"Optimize \u2014 {display} pinned to V-Cache CCD";
                StatusColor = FindBrush("AccentGreenBrush");
            }
            else
            {
                StatusText = $"Monitor \u2014 observing {display} on CCD 0";
                StatusColor = FindBrush("AccentBlueBrush");
            }
        }
        else
        {
            if (_topology.IsSingleCcd)
            {
                StatusText = _topology.Tier == ProcessorTier.SingleCcdX3D
                    ? "Monitor — single V-Cache CCD"
                    : "Monitor — single CCD";
                StatusColor = FindBrush("AccentBlueBrush");
            }
            else if (_currentMode == OperationMode.Optimize)
            {
                StatusText = PowerPlanWarning != null
                    ? $"Optimize — waiting for game | \u26A0 {PowerPlanWarning}"
                    : "Optimize — waiting for game";
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
        Ccd1Panel?.UpdateBorderState(_currentMode, _isGameActive, _isGameActive ? 1 : null);
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Gray);
    }
}
