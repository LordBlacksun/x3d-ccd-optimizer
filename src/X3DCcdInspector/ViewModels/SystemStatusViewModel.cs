using System.Windows;
using System.Windows.Media;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.ViewModels;

/// <summary>
/// ViewModel for the System Status panel at the top of the dashboard.
/// Static CPU info set once from topology, dynamic status updated from SystemStateMonitor.
/// </summary>
public class SystemStatusViewModel : ViewModelBase
{
    // Static CPU info (set once)
    public string CpuModel { get; }
    public string TierText { get; }
    public string Ccd0Info { get; }
    public string Ccd1Info { get; }

    // Dynamic status fields
    private string _driverServiceStatus = "Checking\u2026";
    private SolidColorBrush _driverServiceDot;
    private string _driverStateText = "Checking\u2026";
    private SolidColorBrush _driverStateDot;
    private string _gameBarStatus = "Checking\u2026";
    private SolidColorBrush _gameBarDot;
    private string _gameModeText = "Unknown";
    private SolidColorBrush _gameModeDot;

    public string DriverServiceStatus
    {
        get => _driverServiceStatus;
        private set => SetProperty(ref _driverServiceStatus, value);
    }

    public SolidColorBrush DriverServiceDot
    {
        get => _driverServiceDot;
        private set => SetProperty(ref _driverServiceDot, value);
    }

    public string DriverStateText
    {
        get => _driverStateText;
        private set => SetProperty(ref _driverStateText, value);
    }

    public SolidColorBrush DriverStateDot
    {
        get => _driverStateDot;
        private set => SetProperty(ref _driverStateDot, value);
    }

    public string GameBarStatus
    {
        get => _gameBarStatus;
        private set => SetProperty(ref _gameBarStatus, value);
    }

    public SolidColorBrush GameBarDot
    {
        get => _gameBarDot;
        private set => SetProperty(ref _gameBarDot, value);
    }

    public string GameModeText
    {
        get => _gameModeText;
        private set => SetProperty(ref _gameModeText, value);
    }

    public SolidColorBrush GameModeDot
    {
        get => _gameModeDot;
        private set => SetProperty(ref _gameModeDot, value);
    }

    public SystemStatusViewModel(CpuTopology topology)
    {
        CpuModel = topology.CpuModel;
        TierText = topology.Tier switch
        {
            ProcessorTier.DualCcdX3D => "Dual-CCD X3D",
            ProcessorTier.DualCcdStandard => "Dual-CCD Standard",
            _ => topology.Tier.ToString()
        };

        var vcCores = topology.VCacheCores;
        var fqCores = topology.FrequencyCores;

        Ccd0Info = vcCores.Length > 0
            ? $"V-Cache \u2014 {topology.VCacheL3SizeMB}MB L3, Cores {vcCores.Min()}-{vcCores.Max()}"
            : "V-Cache";
        Ccd1Info = fqCores.Length > 0
            ? $"Frequency \u2014 {topology.StandardL3SizeMB}MB L3, Cores {fqCores.Min()}-{fqCores.Max()}"
            : "Frequency";

        var grayDot = FindBrush("TextTertiaryBrush");
        _driverServiceDot = grayDot;
        _driverStateDot = grayDot;
        _gameBarDot = grayDot;
        _gameModeDot = grayDot;
    }

    public void OnStateChanged(SystemState state)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // Driver service status
            if (!state.IsDriverInstalled)
            {
                DriverServiceStatus = "Not Installed";
                DriverServiceDot = FindBrush("TextTertiaryBrush");
            }
            else if (state.IsDriverServiceRunning)
            {
                DriverServiceStatus = "Running";
                DriverServiceDot = FindBrush("AccentGreenBrush");
            }
            else
            {
                DriverServiceStatus = "Not Running";
                DriverServiceDot = FindBrush("AccentYellowBrush");
            }

            // Driver preference state
            DriverStateText = state.DriverPreference switch
            {
                0 => "PREFER_FREQ",
                1 => "PREFER_CACHE",
                _ => "N/A"
            };
            DriverStateDot = state.DriverPreference.HasValue
                ? FindBrush("AccentGreenBrush")
                : FindBrush("TextTertiaryBrush");

            // Game Bar
            GameBarStatus = state.IsGameBarRunning ? "Running" : "Standby";
            GameBarDot = state.IsGameBarRunning
                ? FindBrush("AccentGreenBrush")
                : FindBrush("TextTertiaryBrush");

            // GameMode
            var gameModeDisplay = state.GameModeStatus == "Inactive" ? "Standby" : state.GameModeStatus;
            GameModeText = gameModeDisplay;
            GameModeDot = state.GameModeStatus switch
            {
                "Active" => FindBrush("AccentGreenBrush"),
                "Inactive" => FindBrush("TextTertiaryBrush"),
                _ => FindBrush("AccentYellowBrush")
            };
        });
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Gray);
    }
}
