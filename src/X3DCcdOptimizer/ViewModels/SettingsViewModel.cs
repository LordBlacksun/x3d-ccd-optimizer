using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Serilog;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Core;
using X3DCcdOptimizer.Data;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.Views;

namespace X3DCcdOptimizer.ViewModels;

public class GameDisplayItem
{
    public string Exe { get; }
    public string Display { get; }

    public GameDisplayItem(string exe, string? displayName = null)
    {
        Exe = exe;
        var name = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;
        Display = displayName != null ? $"{displayName} ({exe})" : exe;
    }

    public override string ToString() => Display;
}

public class SettingsViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly CpuTopology _topology;

    // General
    private bool _startWithWindows;
    private string _defaultMode;
    private string _optimizeStrategy;
    private bool _startMinimized;
    private bool _minimizeToTray;
    private bool _notifications;
    private int _pollingIntervalMs;
    private int _dashboardRefreshMs;

    // Detection
    private bool _autoDetectEnabled;
    private int _gpuThreshold;
    private bool _requireForeground;
    private int _detectionDelay;
    private int _exitDelay;

    // Overlay
    private bool _overlayEnabled;
    private double _overlayOpacity;
    private int _autoHideSeconds;
    private int _pixelShiftMinutes;
    private bool _showOverlayBars;

    // Advanced
    private string _logLevel;

    // Process Rules — autocomplete
    private string? _newGameText;
    private string? _newBgText;
    private ObservableCollection<string> _gameSuggestions = [];
    private ObservableCollection<string> _bgSuggestions = [];

    // General
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }
    public string DefaultMode { get => _defaultMode; set => SetProperty(ref _defaultMode, value); }
    public string OptimizeStrategy
    {
        get => _optimizeStrategy;
        set
        {
            if (SetProperty(ref _optimizeStrategy, value))
            {
                OnPropertyChanged(nameof(AffinityPinningWarningVisibility));
            }
        }
    }
    public bool HasVCache => _topology.HasVCache;
    public bool CanOptimize => _topology.IsDualCcd;
    public bool IsStrategyAvailable => _topology.IsDualCcd &&
        _topology.Tier is ProcessorTier.DualCcdX3D or ProcessorTier.DualCcdStandard;
    public bool IsDriverAvailable => VCacheDriverManager.IsDriverAvailable &&
        _topology.Tier is ProcessorTier.DualCcdX3D or ProcessorTier.SingleCcdX3D;
    public Visibility DriverWarningVisibility =>
        (_topology.Tier is ProcessorTier.DualCcdX3D or ProcessorTier.SingleCcdX3D) && !VCacheDriverManager.IsDriverAvailable
            ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StrategyVisibility => IsStrategyAvailable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AffinityPinningWarningVisibility =>
        string.Equals(_optimizeStrategy, "affinityPinning", StringComparison.OrdinalIgnoreCase) && IsStrategyAvailable
            ? Visibility.Visible : Visibility.Collapsed;
    public string TierDescription => _topology.Tier switch
    {
        ProcessorTier.SingleCcdX3D => "Single-CCD X3D — monitoring only, no CCD steering needed",
        ProcessorTier.SingleCcdStandard => "Single-CCD — monitoring only",
        ProcessorTier.DualCcdStandard => "Dual-CCD (no V-Cache) — affinity pinning available",
        ProcessorTier.DualCcdX3D => "Dual-CCD X3D — full optimization available",
        _ => ""
    };
    public bool StartMinimized { get => _startMinimized; set => SetProperty(ref _startMinimized, value); }
    public bool MinimizeToTray { get => _minimizeToTray; set => SetProperty(ref _minimizeToTray, value); }
    public bool Notifications { get => _notifications; set => SetProperty(ref _notifications, value); }
    public int PollingIntervalMs { get => _pollingIntervalMs; set => SetProperty(ref _pollingIntervalMs, value); }
    public int DashboardRefreshMs { get => _dashboardRefreshMs; set => SetProperty(ref _dashboardRefreshMs, value); }

    // Detection
    public bool AutoDetectEnabled { get => _autoDetectEnabled; set => SetProperty(ref _autoDetectEnabled, value); }
    public int GpuThreshold { get => _gpuThreshold; set => SetProperty(ref _gpuThreshold, value); }
    public bool RequireForeground { get => _requireForeground; set => SetProperty(ref _requireForeground, value); }
    public int DetectionDelay { get => _detectionDelay; set => SetProperty(ref _detectionDelay, value); }
    public int ExitDelay { get => _exitDelay; set => SetProperty(ref _exitDelay, value); }

    // Overlay
    public bool OverlayEnabled { get => _overlayEnabled; set => SetProperty(ref _overlayEnabled, value); }
    public double OverlayOpacity { get => _overlayOpacity; set => SetProperty(ref _overlayOpacity, value); }
    public int AutoHideSeconds { get => _autoHideSeconds; set => SetProperty(ref _autoHideSeconds, value); }
    public int PixelShiftMinutes { get => _pixelShiftMinutes; set => SetProperty(ref _pixelShiftMinutes, value); }
    public bool ShowOverlayBars { get => _showOverlayBars; set => SetProperty(ref _showOverlayBars, value); }

    // Advanced
    public string LogLevel { get => _logLevel; set => SetProperty(ref _logLevel, value); }

    // Process Rules — tier awareness
    public ProcessorTier Tier => _topology.Tier;
    public bool IsDualCcd => _topology.IsDualCcd;
    public bool IsSingleCcd => _topology.IsSingleCcd;
    public Visibility DualCcdVisibility => IsDualCcd ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SingleCcdStandardVisibility =>
        Tier == ProcessorTier.SingleCcdStandard ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SingleCcdX3DVisibility =>
        Tier == ProcessorTier.SingleCcdX3D ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GameColumnVisibility =>
        Tier != ProcessorTier.SingleCcdStandard ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BgColumnVisibility => DualCcdVisibility;

    public string GameColumnHeader => Tier switch
    {
        ProcessorTier.DualCcdX3D => "V-Cache CCD (Games)",
        ProcessorTier.DualCcdStandard => "CCD0 \u2014 Primary (Games)",
        _ => "Games (Detection)"
    };
    public string BgColumnHeader => Tier switch
    {
        ProcessorTier.DualCcdX3D => "Frequency CCD (Background)",
        _ => "CCD1 \u2014 Background"
    };
    public string GameColumnTooltip => Tier switch
    {
        ProcessorTier.DualCcdX3D => "Games pinned to V-Cache CCD in Optimize mode.",
        ProcessorTier.DualCcdStandard => "Games pinned to CCD0 in Optimize mode. Your processor has two symmetric CCDs — pinning games to one CCD improves cache isolation.",
        _ => "Games detected for monitoring."
    };
    public string BgColumnTooltip => Tier switch
    {
        ProcessorTier.DualCcdX3D => "Apps explicitly pinned to Frequency CCD in Optimize mode.",
        _ => "Apps pinned to CCD1 in Optimize mode. Your processor has two symmetric CCDs — pinning background apps to the other CCD improves cache isolation."
    };

    // Process Rules
    public ObservableCollection<GameDisplayItem> ManualGames { get; } = [];
    public ObservableCollection<string> BackgroundApps { get; } = [];
    public ObservableCollection<string> ExcludedProcesses { get; } = [];
    public ObservableCollection<string> KnownGames { get; } = [];

    public string? NewGameText
    {
        get => _newGameText;
        set
        {
            if (SetProperty(ref _newGameText, value))
                UpdateGameSuggestions(value);
        }
    }
    public ObservableCollection<string> GameSuggestions
    {
        get => _gameSuggestions;
        set => SetProperty(ref _gameSuggestions, value);
    }

    public string? NewBgText
    {
        get => _newBgText;
        set
        {
            if (SetProperty(ref _newBgText, value))
                UpdateBgSuggestions(value);
        }
    }
    public ObservableCollection<string> BgSuggestions
    {
        get => _bgSuggestions;
        set => SetProperty(ref _bgSuggestions, value);
    }

    // Commands
    public RelayCommand AddGameCommand { get; }
    public RelayCommand RemoveGameCommand { get; }
    public RelayCommand AddBgCommand { get; }
    public RelayCommand AddBgFromPickerCommand { get; }
    public RelayCommand RemoveBgCommand { get; }
    public RelayCommand AddExclusionCommand { get; }
    public RelayCommand RemoveExclusionCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand OpenConfigFolderCommand { get; }
    public RelayCommand ResetDefaultsCommand { get; }

    public GameDisplayItem? SelectedGame { get; set; }
    public string? SelectedBg { get; set; }
    public string? SelectedExclusion { get; set; }
    public string? NewExclusionText { get; set; }

    // Known games database for autocomplete (display → exe)
    private readonly List<(string DisplayName, string Exe)> _knownGamesList = [];

    public SettingsViewModel(AppConfig config, CpuTopology topology)
    {
        _config = config;
        _topology = topology;
        _defaultMode = config.OperationMode;
        _optimizeStrategy = config.OptimizeStrategy;

        // Load current values
        _startWithWindows = StartupManager.IsEnabled();
        _startMinimized = config.Ui.StartMinimized;
        _minimizeToTray = config.Ui.MinimizeToTray;
        _notifications = config.Ui.Notifications;
        _pollingIntervalMs = config.PollingIntervalMs;
        _dashboardRefreshMs = config.DashboardRefreshMs;

        _autoDetectEnabled = config.AutoDetection.Enabled;
        _gpuThreshold = config.AutoDetection.GpuThresholdPercent;
        _requireForeground = config.AutoDetection.RequireForeground;
        _detectionDelay = config.AutoDetection.DetectionDelaySeconds;
        _exitDelay = config.AutoDetection.ExitDelaySeconds;

        _overlayEnabled = config.Overlay.Enabled;
        _overlayOpacity = config.Overlay.Opacity * 100;
        _autoHideSeconds = config.Overlay.AutoHideSeconds;
        _pixelShiftMinutes = config.Overlay.PixelShiftMinutes;
        _showOverlayBars = config.Overlay.ShowLoadBars;

        _logLevel = config.Logging.Level;

        foreach (var b in config.BackgroundApps) BackgroundApps.Add(b);
        foreach (var e in config.ExcludedProcesses) ExcludedProcesses.Add(e);

        // Load known games DB for autocomplete + display name resolution
        try
        {
            var dir = AppContext.BaseDirectory;
            var path = Path.Combine(dir, "Data", "known_games.json");
            if (File.Exists(path))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("games", out var arr))
                    foreach (var g in arr.EnumerateArray())
                    {
                        if (g.TryGetProperty("name", out var nameEl) && g.TryGetProperty("exe", out var exeEl))
                        {
                            var name = nameEl.GetString();
                            var exe = exeEl.GetString();
                            if (name != null && exe != null)
                            {
                                _knownGamesList.Add((name, exe));
                                KnownGames.Add($"{name} ({exe})");
                            }
                        }
                    }
            }
        }
        catch { }

        // Populate game list with display names resolved from known DB
        foreach (var g in config.ManualGames)
        {
            var displayName = _knownGamesList
                .FirstOrDefault(k => string.Equals(k.Exe, g, StringComparison.OrdinalIgnoreCase))
                .DisplayName;
            ManualGames.Add(new GameDisplayItem(g, displayName));
        }

        AddGameCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrWhiteSpace(NewGameText))
            {
                var game = NewGameText.Trim();
                if (!game.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    game += ".exe";
                if (!ManualGames.Any(g => string.Equals(g.Exe, game, StringComparison.OrdinalIgnoreCase)))
                {
                    var displayName = _knownGamesList
                        .FirstOrDefault(k => string.Equals(k.Exe, game, StringComparison.OrdinalIgnoreCase))
                        .DisplayName;
                    ManualGames.Add(new GameDisplayItem(game, displayName));
                }
                NewGameText = "";
            }
        });

        RemoveGameCommand = new RelayCommand(() =>
        {
            if (SelectedGame != null)
                ManualGames.Remove(SelectedGame);
        });

        AddBgCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrWhiteSpace(NewBgText))
            {
                var proc = NewBgText.Trim();
                if (!proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    proc += ".exe";
                if (!BackgroundApps.Contains(proc, StringComparer.OrdinalIgnoreCase))
                    BackgroundApps.Add(proc);
                NewBgText = "";
            }
        });

        AddBgFromPickerCommand = new RelayCommand(() =>
        {
            var allAssigned = new List<string>();
            allAssigned.AddRange(ManualGames.Select(g => g.Exe));
            allAssigned.AddRange(BackgroundApps);

            var picker = new ProcessPickerWindow(allAssigned);
            if (picker.ShowDialog() == true)
            {
                foreach (var exe in picker.SelectedExes)
                {
                    if (!BackgroundApps.Contains(exe, StringComparer.OrdinalIgnoreCase))
                        BackgroundApps.Add(exe);
                }
            }
        });

        RemoveBgCommand = new RelayCommand(() =>
        {
            if (SelectedBg != null)
                BackgroundApps.Remove(SelectedBg);
        });

        AddExclusionCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrWhiteSpace(NewExclusionText))
            {
                var proc = NewExclusionText.Trim();
                if (!proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    proc += ".exe";
                if (!ExcludedProcesses.Contains(proc))
                    ExcludedProcesses.Add(proc);
                NewExclusionText = "";
                OnPropertyChanged(nameof(NewExclusionText));
            }
        });

        RemoveExclusionCommand = new RelayCommand(() =>
        {
            if (SelectedExclusion != null)
                ExcludedProcesses.Remove(SelectedExclusion);
        });

        OpenLogFolderCommand = new RelayCommand(() =>
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "X3DCCDOptimizer", "logs");
            if (System.IO.Directory.Exists(logDir))
                Process.Start("explorer.exe", logDir);
        });

        OpenConfigFolderCommand = new RelayCommand(() =>
        {
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "X3DCCDOptimizer");
            if (System.IO.Directory.Exists(configDir))
                Process.Start("explorer.exe", configDir);
        });

        ResetDefaultsCommand = new RelayCommand(() =>
        {
            if (MessageBox.Show("Reset all settings to defaults?", "Confirm Reset",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var defaults = new AppConfig();
                PollingIntervalMs = defaults.PollingIntervalMs;
                DashboardRefreshMs = defaults.DashboardRefreshMs;
                AutoDetectEnabled = defaults.AutoDetection.Enabled;
                GpuThreshold = defaults.AutoDetection.GpuThresholdPercent;
                RequireForeground = defaults.AutoDetection.RequireForeground;
                DetectionDelay = defaults.AutoDetection.DetectionDelaySeconds;
                ExitDelay = defaults.AutoDetection.ExitDelaySeconds;
                OverlayOpacity = defaults.Overlay.Opacity * 100;
                AutoHideSeconds = defaults.Overlay.AutoHideSeconds;
                PixelShiftMinutes = defaults.Overlay.PixelShiftMinutes;
                LogLevel = defaults.Logging.Level;
                MinimizeToTray = defaults.Ui.MinimizeToTray;
                Notifications = defaults.Ui.Notifications;
                OptimizeStrategy = defaults.OptimizeStrategy;
            }
        });
    }

    private void UpdateGameSuggestions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
        {
            GameSuggestions = [];
            return;
        }

        var matches = _knownGamesList
            .Where(g => g.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                        g.Exe.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(g => g.Exe)
            .ToList();
        GameSuggestions = new ObservableCollection<string>(matches);
    }

    private void UpdateBgSuggestions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
        {
            BgSuggestions = [];
            return;
        }

        var matches = BackgroundAppSuggestions.Apps
            .Where(a => a.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                        a.Exe.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(a => a.Exe)
            .ToList();
        BgSuggestions = new ObservableCollection<string>(matches);
    }

    public void Apply()
    {
        // Start with Windows
        if (_startWithWindows)
            StartupManager.Enable();
        else
            StartupManager.Disable();

        // General
        _config.OperationMode = _defaultMode;
        _config.OptimizeStrategy = _optimizeStrategy;
        _config.Ui.StartMinimized = _startMinimized;
        _config.Ui.MinimizeToTray = _minimizeToTray;
        _config.Ui.Notifications = _notifications;
        _config.PollingIntervalMs = _pollingIntervalMs;
        _config.DashboardRefreshMs = _dashboardRefreshMs;

        // Detection
        _config.AutoDetection.Enabled = _autoDetectEnabled;
        _config.AutoDetection.GpuThresholdPercent = _gpuThreshold;
        _config.AutoDetection.RequireForeground = _requireForeground;
        _config.AutoDetection.DetectionDelaySeconds = _detectionDelay;
        _config.AutoDetection.ExitDelaySeconds = _exitDelay;

        // Overlay
        _config.Overlay.Enabled = _overlayEnabled;
        _config.Overlay.Opacity = _overlayOpacity / 100.0;
        _config.Overlay.AutoHideSeconds = _autoHideSeconds;
        _config.Overlay.PixelShiftMinutes = _pixelShiftMinutes;
        _config.Overlay.ShowLoadBars = _showOverlayBars;

        // Advanced
        _config.Logging.Level = _logLevel;

        // Process rules
        _config.ManualGames = ManualGames.Select(g => g.Exe).ToList();
        _config.BackgroundApps = [.. BackgroundApps];
        _config.ExcludedProcesses = [.. ExcludedProcesses];

        _config.Save();
        Log.Information("Settings saved");
    }
}
