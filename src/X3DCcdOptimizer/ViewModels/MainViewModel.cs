using System.Reflection;
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
    private GameDatabase? _gameDb;

    private OperationMode _currentMode;
    private string _statusText = "";
    private SolidColorBrush _statusColor;
    private bool _isGameActive;
    private string _sessionDurationText = "";
    private string _currentGameName = "";
    private string _currentGameDisplayName = "";
    private DateTime _sessionStart;
    private bool _isOverlayVisible;
    private CoreSnapshot[]? _lastSnapshots;

    public CcdPanelViewModel Ccd0Panel { get; }
    public CcdPanelViewModel? Ccd1Panel { get; }
    public bool ShowSecondPanel { get; }
    public ActivityLogViewModel ActivityLog { get; } = new();
    public ProcessRouterViewModel ProcessRouter { get; }
    public GameLibraryViewModel? GameLibrary { get; private set; }

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

    private string _updateText = "";
    public string UpdateText
    {
        get => _updateText;
        set => SetProperty(ref _updateText, value);
    }

    public string FooterText { get; }

    public RelayCommand ToggleModeCommand { get; }
    public RelayCommand ToggleOverlayCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenAboutCommand { get; }

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

        IsOptimizeEnabled = true;
        ShowSecondPanel = true;

        Ccd0Panel = new CcdPanelViewModel(topology, 0);
        Ccd1Panel = new CcdPanelViewModel(topology, 1);

        // Process router with CCD group names
        var ccd0Name = Ccd0Panel.BadgeText;
        var ccd1Name = Ccd1Panel?.BadgeText ?? "";
        ProcessRouter = new ProcessRouterViewModel(ccd0Name, ccd1Name);

        _statusColor = FindBrush("AccentBlueBrush");
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        FooterText = $"v{version} | {topology.CpuModel} | {topology.TotalPhysicalCores} cores | {topology.TotalLogicalCores} threads | Polling: {config.PollingIntervalMs}ms";

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

            var settingsVm = new SettingsViewModel(config, topology, _gameDb);
            var settingsWindow = new Views.SettingsWindow
            {
                DataContext = settingsVm
            };
            settingsWindow.Show();
        });

        OpenAboutCommand = new RelayCommand(() =>
        {
            foreach (System.Windows.Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Views.AboutWindow existing)
                {
                    existing.Activate();
                    return;
                }
            }

            var aboutWindow = new Views.AboutWindow();
            aboutWindow.Show();
        });

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _sessionStart;
            SessionDurationText = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m {elapsed.Seconds:D2}s"
                : $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        };

        if (config.IsFirstRun)
            StatusText = "Monitor mode \u2014 observing your CPU without making changes. Switch to Optimize to pin games to V-Cache.";
        else
            UpdateStatus();
    }

    public void InitGameLibrary(GameDatabase gameDb)
    {
        _gameDb = gameDb;
        GameLibrary = new GameLibraryViewModel(gameDb);
        OnPropertyChanged(nameof(GameLibrary));
    }

    public void OnSnapshotReady(CoreSnapshot[] snapshots)
    {
        // Debounce: skip UI update if no core changed by more than 1% load or 50 MHz frequency
        if (_lastSnapshots != null && _lastSnapshots.Length == snapshots.Length)
        {
            bool changed = false;
            for (int i = 0; i < snapshots.Length; i++)
            {
                if (Math.Abs(snapshots[i].LoadPercent - _lastSnapshots[i].LoadPercent) > 1.0 ||
                    Math.Abs(snapshots[i].FrequencyMHz - _lastSnapshots[i].FrequencyMHz) > 50.0)
                {
                    changed = true;
                    break;
                }
            }
            if (!changed) return;
        }
        _lastSnapshots = snapshots;

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
            if (_currentMode == OperationMode.Optimize)
            {
                Ccd0Panel.RoleLabel = $"Gaming \u2014 {display}";
                if (Ccd1Panel != null)
                    Ccd1Panel.RoleLabel = "Background";
            }
            else
            {
                Ccd0Panel.RoleLabel = $"Detected \u2014 {display}";
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
        var ccdLabel = _topology.Tier == ProcessorTier.DualCcdStandard ? "CCD0" : "V-Cache CCD";

        if (_isGameActive)
        {
            if (_currentMode == OperationMode.Optimize)
            {
                var suffix = strategy == OptimizeStrategy.AffinityPinning ? " (pinned)" : "";
                StatusText = $"Optimize \u2014 {display} \u2192 {ccdLabel}{suffix}";
                StatusColor = FindBrush("AccentGreenBrush");
            }
            else
            {
                StatusText = $"Monitor \u2014 {display} detected on {ccdLabel}";
                StatusColor = FindBrush("AccentBlueBrush");
            }
        }
        else
        {
            if (_currentMode == OperationMode.Optimize)
            {
                StatusText = PowerPlanWarning != null
                    ? $"Optimize \u2014 waiting for game | \u26A0 {PowerPlanWarning}"
                    : "Optimize \u2014 waiting for game";
                StatusColor = FindBrush("AccentPurpleBrush");
            }
            else
            {
                StatusText = "Monitor \u2014 watching for games";
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
