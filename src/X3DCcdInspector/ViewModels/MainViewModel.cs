using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using X3DCcdInspector.Config;
using X3DCcdInspector.Core;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly CpuTopology _topology;
    private readonly AppConfig _config;
    private readonly DispatcherTimer _sessionTimer;
    private GameDatabase? _gameDb;

    private string _statusText = "";
    private SolidColorBrush _statusColor;
    private bool _isGameActive;
    private string _sessionDurationText = "";
    private string _currentGameDisplayName = "";
    private DateTime _sessionStart;
    private bool _isOverlayVisible;
    private CoreSnapshot[]? _lastSnapshots;

    public CcdPanelViewModel Ccd0Panel { get; }
    public CcdPanelViewModel? Ccd1Panel { get; }
    public bool ShowSecondPanel { get; }
    public ActivityLogViewModel ActivityLog { get; } = new();
    public ProcessRouterViewModel CcdMap { get; }
    public GameLibraryViewModel? GameLibrary { get; private set; }
    public SystemStatusViewModel SystemStatus { get; }
    public ActiveGameViewModel ActiveGame { get; }
    public ProcessExclusionsViewModel? ProcessExclusions { get; private set; }

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
        set
        {
            if (SetProperty(ref _updateText, value))
                OnPropertyChanged(nameof(UpdateVisible));
        }
    }

    public bool UpdateVisible => !string.IsNullOrEmpty(_updateText);

    public RelayCommand ApplyUpdateCommand { get; }

    public string FooterText { get; }

    public RelayCommand ToggleOverlayCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenAboutCommand { get; }

    // Expose for settings window creation
    public CpuTopology Topology => _topology;
    public AppConfig Config => _config;

    public MainViewModel(CpuTopology topology, PerformanceMonitor perfMon,
        ProcessWatcher processWatcher, GameDetector gameDetector,
        AffinityManager affinityManager, AppConfig config)
    {
        _topology = topology;
        _config = config;

        ShowSecondPanel = true;

        Ccd0Panel = new CcdPanelViewModel(topology, 0);
        Ccd1Panel = new CcdPanelViewModel(topology, 1);

        SystemStatus = new SystemStatusViewModel(topology);
        ActiveGame = new ActiveGameViewModel();

        // Process router with CCD group names
        var ccd0Name = Ccd0Panel.BadgeText;
        var ccd1Name = Ccd1Panel?.BadgeText ?? "";
        CcdMap = new ProcessRouterViewModel(ccd0Name, ccd1Name);

        _statusColor = FindBrush("AccentBlueBrush");
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        FooterText = $"v{version} | {topology.CpuModel} | {topology.TotalPhysicalCores} cores | {topology.TotalLogicalCores} threads | Polling: {config.PollingIntervalMs}ms";

        ToggleOverlayCommand = new RelayCommand(() =>
        {
            IsOverlayVisible = !IsOverlayVisible;
            OnPropertyChanged(nameof(OverlayButtonText));
        });

        OpenSettingsCommand = new RelayCommand(() =>
        {
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

        ApplyUpdateCommand = new RelayCommand(() =>
        {
            var originalText = _updateText;
            Task.Run(async () =>
            {
                var success = await Core.UpdateChecker.DownloadAndApply(
                    status => Application.Current?.Dispatcher.BeginInvoke(() => UpdateText = status));

                if (success)
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        UpdateText = "Restarting...";
                        Application.Current.Shutdown();
                    });
                }
                else
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                        UpdateText = originalText);
                }
            });
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

    public void InitProcessExclusions(AppConfig config, GameDetector gameDetector)
    {
        ProcessExclusions = new ProcessExclusionsViewModel(config, gameDetector);
        OnPropertyChanged(nameof(ProcessExclusions));
    }

    public void InitGameLibrary(GameDatabase gameDb, IEnumerable<string>? excludedProcesses = null)
    {
        _gameDb = gameDb;
        ActiveGame.SetGameDatabase(gameDb);
        GameLibrary = new GameLibraryViewModel(gameDb, excludedProcesses);
        GameLibrary.PreferenceChanged += OnGamePreferenceChanged;
        OnPropertyChanged(nameof(GameLibrary));
    }

    private void OnGamePreferenceChanged(string exeName, string displayName, string newPreference)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var action = newPreference == "Auto"
                ? AffinityAction.CcdPreferenceRemoved
                : AffinityAction.CcdPreferenceSet;

            var prefLabel = newPreference switch
            {
                "VCache" => "V-Cache",
                "Frequency" => "Frequency",
                _ => "Auto"
            };

            var detail = newPreference == "Auto"
                ? $"CCD preference removed: {displayName} \u2192 Auto"
                : $"CCD preference set: {displayName} \u2192 {prefLabel} (AMD driver profile)";

            OnAffinityChanged(new AffinityEvent
            {
                Action = action,
                ProcessName = exeName,
                DisplayName = displayName,
                Detail = detail
            });
        });
    }

    public void OnSnapshotReady(CoreSnapshot[] snapshots)
    {
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
            CcdMap.OnAffinityChanged(evt);
        });
    }

    public void OnSystemStateChanged(SystemState state)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            SystemStatus.OnStateChanged(state);
            ActiveGame.OnStateChanged(state);
        });
    }

    public void OnGameDetected(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _currentGameDisplayName = game.DisplayName ?? game.Name;
            IsGameActive = true;
            _sessionStart = DateTime.Now;
            _sessionTimer.Start();

            var display = _currentGameDisplayName;
            Ccd0Panel.RoleLabel = $"Detected \u2014 {display}";
            if (Ccd1Panel != null)
                Ccd1Panel.RoleLabel = "Idle";

            ActiveGame.OnGameDetected(game);
            UpdateStatus();
            UpdateBorders();
        });
    }

    public void OnGameExited(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _currentGameDisplayName = "";
            IsGameActive = false;
            _sessionTimer.Stop();
            SessionDurationText = "";

            Ccd0Panel.RoleLabel = "Idle";
            if (Ccd1Panel != null) Ccd1Panel.RoleLabel = "Idle";
            CcdMap.Clear();

            ActiveGame.OnGameExited(game);
            UpdateStatus();
            UpdateBorders();
        });
    }

    private void UpdateStatus()
    {
        var ccdLabel = _topology.Tier == ProcessorTier.DualCcdStandard ? "CCD0" : "V-Cache CCD";

        if (_isGameActive)
        {
            StatusText = $"{_currentGameDisplayName} detected on {ccdLabel}";
            StatusColor = FindBrush("AccentGreenBrush");
        }
        else
        {
            StatusText = "Watching for games";
            StatusColor = FindBrush("AccentBlueBrush");
        }
    }

    private void UpdateBorders()
    {
        int? gameCcd = _isGameActive ? 0 : null;
        Ccd0Panel.UpdateBorderState(_isGameActive, gameCcd);
        Ccd1Panel?.UpdateBorderState(_isGameActive, _isGameActive ? 1 : null);
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Gray);
    }
}
