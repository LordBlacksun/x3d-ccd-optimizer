using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Serilog;
using X3DCcdInspector.Config;
using X3DCcdInspector.Core;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly AppConfig _config;
    private readonly CpuTopology _topology;

    // General
    private bool _startWithWindows;
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
    private string _overlayPosition;

    // Advanced
    private string _logLevel;

    // General
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }
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
    public string OverlayPosition { get => _overlayPosition; set => SetProperty(ref _overlayPosition, value); }

    // Advanced
    public string LogLevel { get => _logLevel; set => SetProperty(ref _logLevel, value); }

    // Exclusions
    public ObservableCollection<string> ExcludedProcesses { get; } = [];
    public string? SelectedExclusion { get; set; }
    public string? NewExclusionText { get; set; }

    // Library rescan, artwork, updates
    private string _scanStatusText = "";
    private bool _enableArtworkDownload;
    private bool _checkForUpdates;
    public bool EnableArtworkDownload
    {
        get => _enableArtworkDownload;
        set => SetProperty(ref _enableArtworkDownload, value);
    }
    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set => SetProperty(ref _checkForUpdates, value);
    }
    public string ScanStatusText
    {
        get => _scanStatusText;
        set => SetProperty(ref _scanStatusText, value);
    }
    public RelayCommand RescanLibrariesCommand { get; }

    // Commands
    public RelayCommand AddExclusionCommand { get; }
    public RelayCommand RemoveExclusionCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand OpenConfigFolderCommand { get; }
    public RelayCommand ResetDefaultsCommand { get; }

    public SettingsViewModel(AppConfig config, CpuTopology topology, GameDatabase? gameDb = null)
    {
        _config = config;
        _topology = topology;

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
        _overlayPosition = config.Overlay.OverlayPosition;

        _logLevel = config.Logging.Level;
        _enableArtworkDownload = config.EnableArtworkDownload;
        _checkForUpdates = config.CheckForUpdates;

        foreach (var e in config.ExcludedProcesses) ExcludedProcesses.Add(e);

        RescanLibrariesCommand = new RelayCommand(() =>
        {
            _config.LibraryScanConsent = true;

            ScanStatusText = "Scanning...";
            Task.Run(() =>
            {
                try
                {
                    var scanned = GameLibraryScanner.ScanAll();
                    using var db = new GameDatabase();
                    db.ReplaceGames(scanned);

                    var steamCount = scanned.Count(g => g.Source == "steam");
                    var epicCount = scanned.Count(g => g.Source == "epic");
                    var gogCount = scanned.Count(g => g.Source == "gog");

                    Application.Current?.Dispatcher.BeginInvoke(() =>
                        ScanStatusText = $"Found {scanned.Count} games ({steamCount} Steam, {epicCount} Epic, {gogCount} GOG)");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Manual library rescan failed");
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                        ScanStatusText = "Scan failed \u2014 see log for details");
                }
            });
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
                "X3DCCDInspector", "logs");
            if (System.IO.Directory.Exists(logDir))
                Process.Start("explorer.exe", logDir);
        });

        OpenConfigFolderCommand = new RelayCommand(() =>
        {
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "X3DCCDInspector");
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
        _config.Overlay.OverlayPosition = _overlayPosition;

        // Advanced
        _config.Logging.Level = _logLevel;
        _config.EnableArtworkDownload = _enableArtworkDownload;
        _config.CheckForUpdates = _checkForUpdates;

        // Exclusions
        _config.ExcludedProcesses = [.. ExcludedProcesses];

        _config.Validate();
        _config.Save();
        Log.Information("Settings saved");
    }
}
