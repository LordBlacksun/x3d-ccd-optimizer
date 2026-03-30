using System.Windows;
using X3DCcdInspector.Core;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.ViewModels;

/// <summary>
/// ViewModel for the Active Game panel in the dashboard center section.
/// Shows idle state when no game detected, or game details when active.
/// </summary>
public class ActiveGameViewModel : ViewModelBase
{
    private GameDatabase? _gameDb;
    private bool _isGameActive;
    private string _gameName = "";
    private string _detectionMethod = "";
    private string _processInfo = "";
    private string _ccdDistribution = "";
    private string _threadCounts = "";
    private string _ccdPreference = "Auto";
    private string _driverAction = "";
    private string _detectionStatusText = "Listening via ETW + polling fallback";

    /// <summary>
    /// Fired when a CCD preference is verified/applied on game detect.
    /// Carries an AffinityEvent for the activity log.
    /// </summary>
    public event Action<AffinityEvent>? PreferenceVerified;

    public void SetGameDatabase(GameDatabase gameDb) => _gameDb = gameDb;

    public bool IsGameActive
    {
        get => _isGameActive;
        private set
        {
            if (SetProperty(ref _isGameActive, value))
            {
                OnPropertyChanged(nameof(GameActiveVisibility));
                OnPropertyChanged(nameof(IdleVisibility));
            }
        }
    }

    public Visibility GameActiveVisibility => _isGameActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IdleVisibility => _isGameActive ? Visibility.Collapsed : Visibility.Visible;

    public string GameName
    {
        get => _gameName;
        private set => SetProperty(ref _gameName, value);
    }

    public string DetectionMethod
    {
        get => _detectionMethod;
        private set => SetProperty(ref _detectionMethod, value);
    }

    public string ProcessInfo
    {
        get => _processInfo;
        private set => SetProperty(ref _processInfo, value);
    }

    public string CcdDistribution
    {
        get => _ccdDistribution;
        private set => SetProperty(ref _ccdDistribution, value);
    }

    public string ThreadCounts
    {
        get => _threadCounts;
        private set => SetProperty(ref _threadCounts, value);
    }

    public string CcdPreference
    {
        get => _ccdPreference;
        private set => SetProperty(ref _ccdPreference, value);
    }

    public string DriverAction
    {
        get => _driverAction;
        private set => SetProperty(ref _driverAction, value);
    }

    public string DetectionStatusText
    {
        get => _detectionStatusText;
        private set => SetProperty(ref _detectionStatusText, value);
    }

    public void OnGameDetected(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            IsGameActive = true;
            GameName = game.DisplayName ?? game.Name;

            DetectionMethod = game.Method switch
            {
                Core.DetectionMethod.Manual => game.DetectionSource.Contains("ETW", System.StringComparison.OrdinalIgnoreCase)
                    ? "ETW (Manual Rule)"
                    : "Manual Rule",
                Core.DetectionMethod.LibraryScan => game.DetectionSource.Contains("ETW", System.StringComparison.OrdinalIgnoreCase)
                    ? "ETW (Library Rule)"
                    : "Library Rule",
                Core.DetectionMethod.Auto => $"GPU Heuristic ({game.GpuUsage:F0}%)",
                _ => "Unknown"
            };

            ProcessInfo = $"{game.Name} (PID {game.Pid})";
            CcdDistribution = "Analyzing\u2026";
            ThreadCounts = "Analyzing\u2026";
            DriverAction = "Checking\u2026";

            // Look up and display CCD preference / fallback pin
            var displayName = game.DisplayName ?? game.Name;

            if (VCacheDriverManager.IsDriverAvailable)
            {
                // Driver-based preference (Phase 4)
                var pref = _gameDb?.GetCcdPreference(game.Name) ?? "Auto";
                if (pref == "Auto")
                {
                    CcdPreference = "Auto (AMD default)";
                }
                else
                {
                    var prefLabel = pref == "VCache" ? "V-Cache" : "Frequency";
                    CcdPreference = $"{prefLabel} (via AMD driver profile)";

                    var profileName = VCacheDriverManager.SanitizeProfileName(displayName);
                    var existing = VCacheDriverManager.GetAppProfile(profileName);
                    if (existing == null)
                    {
                        var type = pref == "VCache" ? 1 : 0;
                        VCacheDriverManager.SetAppProfile(profileName, game.Name, type);
                    }

                    PreferenceVerified?.Invoke(new AffinityEvent
                    {
                        Action = AffinityAction.CcdPreferenceSet,
                        ProcessName = game.Name,
                        DisplayName = displayName,
                        Pid = game.Pid,
                        Detail = $"CCD preference active: {displayName} \u2192 {prefLabel}"
                    });
                }
            }
            else
            {
                // Fallback affinity pin (Phase 5)
                var fallback = _gameDb?.GetFallbackPin(game.Name) ?? "None";
                if (fallback == "None")
                {
                    CcdPreference = "None (driver unavailable)";
                }
                else
                {
                    var ccdLabel = fallback == "VCache" ? "V-Cache" : "Frequency";
                    CcdPreference = $"{ccdLabel} (affinity pin \u2014 driver unavailable)";
                }
            }
        });
    }

    public void OnGameExited(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            IsGameActive = false;
            GameName = "";
            DetectionMethod = "";
            ProcessInfo = "";
            CcdDistribution = "";
            ThreadCounts = "";
            CcdPreference = "Auto (AMD default)";
            DriverAction = "";
        });
    }

    public void OnStateChanged(SystemState state)
    {
        if (!_isGameActive) return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            CcdDistribution = state.ActiveCcd switch
            {
                "CCD0" => "CCD0 (V-Cache)",
                "CCD1" => "CCD1 (Frequency)",
                "Both" => "Both CCDs",
                _ => "Unknown"
            };

            ThreadCounts = state.Ccd0ThreadCount > 0 || state.Ccd1ThreadCount > 0
                ? $"{state.Ccd0ThreadCount} threads on CCD0, {state.Ccd1ThreadCount} threads on CCD1"
                : "No thread data";

            DriverAction = state.GameModeStatus switch
            {
                "Active" => "GameMode triggered PREFER_CACHE",
                "Inactive" => "No driver action",
                _ => "Driver not available"
            };
        });
    }
}
