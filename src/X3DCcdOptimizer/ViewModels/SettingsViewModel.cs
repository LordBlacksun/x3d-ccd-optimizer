using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Serilog;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Core;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly CpuTopology _topology;

    // General
    private bool _startWithWindows;
    private string _defaultMode;
    private string _optimizeStrategy;
    private bool _startMinimized;
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

    // Advanced
    private string _logLevel;

    // General
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }
    public string DefaultMode { get => _defaultMode; set => SetProperty(ref _defaultMode, value); }
    public string OptimizeStrategy { get => _optimizeStrategy; set => SetProperty(ref _optimizeStrategy, value); }
    public bool HasVCache => _topology.HasVCache;
    public bool CanOptimize => _topology.IsDualCcd;
    public bool IsStrategyAvailable => _topology.IsDualCcd;
    public bool IsDriverAvailable => VCacheDriverManager.IsDriverAvailable && _topology.Tier == ProcessorTier.DualCcdX3D;
    public Visibility DriverWarningVisibility => IsDriverAvailable ? Visibility.Collapsed : Visibility.Visible;
    public string TierDescription => _topology.Tier switch
    {
        ProcessorTier.SingleCcdX3D => "Single-CCD X3D — monitoring only, no CCD steering needed",
        ProcessorTier.DualCcdStandard => "Dual-CCD (no V-Cache) — affinity pinning available",
        ProcessorTier.DualCcdX3D => "Dual-CCD X3D — full optimization available",
        _ => ""
    };
    public bool StartMinimized { get => _startMinimized; set => SetProperty(ref _startMinimized, value); }
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

    // Advanced
    public string LogLevel { get => _logLevel; set => SetProperty(ref _logLevel, value); }

    // Game lists
    public ObservableCollection<string> ManualGames { get; } = [];
    public ObservableCollection<string> ExcludedProcesses { get; } = [];
    public ObservableCollection<string> KnownGames { get; } = [];

    // Commands
    public RelayCommand AddGameCommand { get; }
    public RelayCommand RemoveGameCommand { get; }
    public RelayCommand AddExclusionCommand { get; }
    public RelayCommand RemoveExclusionCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand OpenConfigFolderCommand { get; }
    public RelayCommand ResetDefaultsCommand { get; }

    public string? SelectedGame { get; set; }
    public string? SelectedExclusion { get; set; }
    public string? NewGameText { get; set; }
    public string? NewExclusionText { get; set; }

    public SettingsViewModel(AppConfig config, CpuTopology topology)
    {
        _config = config;
        _topology = topology;
        _defaultMode = config.OperationMode;
        _optimizeStrategy = config.OptimizeStrategy;

        // Load current values
        _startWithWindows = StartupManager.IsEnabled();
        _startMinimized = config.Ui.StartMinimized;
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

        _logLevel = config.Logging.Level;

        foreach (var g in config.ManualGames) ManualGames.Add(g);
        foreach (var e in config.ExcludedProcesses) ExcludedProcesses.Add(e);

        // Load known games DB names
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
                        var name = g.GetProperty("name").GetString();
                        var exe = g.GetProperty("exe").GetString();
                        if (name != null && exe != null)
                            KnownGames.Add($"{name} ({exe})");
                    }
            }
        }
        catch { }

        AddGameCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrWhiteSpace(NewGameText))
            {
                var game = NewGameText.Trim();
                if (!game.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    game += ".exe";
                if (!ManualGames.Contains(game))
                    ManualGames.Add(game);
                NewGameText = "";
                OnPropertyChanged(nameof(NewGameText));
            }
        });

        RemoveGameCommand = new RelayCommand(() =>
        {
            if (SelectedGame != null)
                ManualGames.Remove(SelectedGame);
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
                Notifications = defaults.Ui.Notifications;
                OptimizeStrategy = defaults.OptimizeStrategy;
            }
        });
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

        // Advanced
        _config.Logging.Level = _logLevel;

        // Game lists
        _config.ManualGames = [.. ManualGames];
        _config.ExcludedProcesses = [.. ExcludedProcesses];

        _config.Save();
        Log.Information("Settings saved");
    }
}
