using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using X3DCcdInspector.Config;
using X3DCcdInspector.Core;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.ViewModels;

public class OverlayViewModel : ViewModelBase
{
    private readonly CpuTopology _topology;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly DispatcherTimer _pixelShiftTimer;
    private readonly Random _rng = new();
    private readonly OverlayConfig _overlayConfig;

    private SolidColorBrush _modeDotColor;
    private string _primaryText = "Watching for games";
    private string _secondaryText = "";
    private double _overlayOpacity;
    private bool _isFadedOut;
    private double _ccd0Load;
    private double _ccd1Load;
    private string _currentGameName = "";
    private bool _gameActive;
    private bool _isAffinityPinned;

    public SolidColorBrush ModeDotColor
    {
        get => _modeDotColor;
        set => SetProperty(ref _modeDotColor, value);
    }

    public string PrimaryText
    {
        get => _primaryText;
        set
        {
            if (SetProperty(ref _primaryText, value))
                OnPropertyChanged(nameof(IsTwoLine));
        }
    }

    public string SecondaryText
    {
        get => _secondaryText;
        set
        {
            if (SetProperty(ref _secondaryText, value))
                OnPropertyChanged(nameof(IsTwoLine));
        }
    }

    public bool IsTwoLine => !string.IsNullOrEmpty(_secondaryText);

    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set => SetProperty(ref _overlayOpacity, value);
    }

    public bool IsFadedOut
    {
        get => _isFadedOut;
        set => SetProperty(ref _isFadedOut, value);
    }

    // CCD load bars
    public bool ShowLoadBars => _overlayConfig.ShowLoadBars;
    public bool ShowSecondBar => _overlayConfig.ShowLoadBars;

    public double Ccd0Load
    {
        get => _ccd0Load;
        set
        {
            if (SetProperty(ref _ccd0Load, value))
                OnPropertyChanged(nameof(Ccd0LoadText));
        }
    }

    public string Ccd0LoadText => $"{_ccd0Load:F0}%";

    public double Ccd1Load
    {
        get => _ccd1Load;
        set
        {
            if (SetProperty(ref _ccd1Load, value))
                OnPropertyChanged(nameof(Ccd1LoadText));
        }
    }

    public string Ccd1LoadText => $"{_ccd1Load:F0}%";

    public string Ccd0Label => _topology.HasVCache ? "V-Cache" : "CCD0";
    public string Ccd1Label => _topology.HasVCache ? "Freq" : "CCD1";

    // Pixel shift requests — the view subscribes to this
    public event Action<double, double>? PixelShiftRequested;

    public OverlayViewModel(CpuTopology topology, OverlayConfig overlayConfig)
    {
        _topology = topology;
        _overlayConfig = overlayConfig;
        _overlayOpacity = overlayConfig.Opacity;
        _modeDotColor = FindBrush("AccentBlueBrush");

        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(overlayConfig.AutoHideSeconds)
        };
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            IsFadedOut = true;
        };

        _pixelShiftTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(overlayConfig.PixelShiftMinutes)
        };
        _pixelShiftTimer.Tick += (_, _) =>
        {
            double dx = _rng.Next(-5, 6);
            double dy = _rng.Next(-5, 6);
            PixelShiftRequested?.Invoke(dx, dy);
        };
        _pixelShiftTimer.Start();

        // Start auto-hide countdown
        _autoHideTimer.Start();
    }

    public void OnSnapshotReady(CoreSnapshot[] snapshots)
    {
        if (!_overlayConfig.ShowLoadBars) return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var c0 = snapshots.Where(s => s.CcdIndex == 0).ToArray();
            var c1 = snapshots.Where(s => s.CcdIndex == 1).ToArray();

            Ccd0Load = c0.Length > 0 ? c0.Average(s => s.LoadPercent) : 0;
            Ccd1Load = c1.Length > 0 ? c1.Average(s => s.LoadPercent) : 0;
        });
    }

    public void OnAffinityChanged(AffinityEvent evt)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            switch (evt.Action)
            {
                case AffinityAction.GameDetected:
                    SecondaryText = "Detected on V-Cache CCD";
                    break;
                case AffinityAction.GameExited:
                    SecondaryText = "Session ended";
                    break;
                case AffinityAction.Error:
                    PrimaryText = evt.DisplayName ?? evt.ProcessName;
                    SecondaryText = "Error: " + evt.Detail;
                    break;
                default:
                    return;
            }

            ResetAutoHide();
        });
    }

    public void OnGameDetected(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _currentGameName = game.DisplayName ?? game.Name;
            PrimaryText = _currentGameName;
            // CCD info and driver state come from OnSystemStateChanged
            ResetAutoHide();
        });
    }

    public void OnGameExited(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            PrimaryText = game.DisplayName ?? game.Name;
            SecondaryText = "Session ended";
            _currentGameName = "";
            ResetAutoHide();
        });
    }

    public void OnGameActiveChanged(bool gameActive)
    {
        _gameActive = gameActive;
        ModeDotColor = gameActive ? FindBrush("AccentGreenBrush") : FindBrush("AccentBlueBrush");

        if (!gameActive)
        {
            _currentGameName = "";
            _isAffinityPinned = false;
            PrimaryText = "Watching for games";
            SecondaryText = "";
        }

        ResetAutoHide();
    }

    public void OnAffinityPinChanged(bool isPinned)
    {
        _isAffinityPinned = isPinned;
    }

    public void OnSystemStateChanged(SystemState state)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_gameActive || string.IsNullOrEmpty(_currentGameName))
                return;

            var ccdLabel = state.ActiveCcd switch
            {
                "CCD0" => "V-Cache CCD",
                "CCD1" => "Frequency CCD",
                "Both" => "Both CCDs",
                _ => ""
            };

            var suffix = _isAffinityPinned ? " (pinned)" : "";
            PrimaryText = string.IsNullOrEmpty(ccdLabel)
                ? _currentGameName
                : $"{_currentGameName} \u2014 {ccdLabel}{suffix}";

            SecondaryText = state.DriverPreference switch
            {
                0 => "Driver: PREFER_FREQ",
                1 => "Driver: PREFER_CACHE",
                _ => "Driver: N/A"
            };
        });
    }

    public void OnForegroundChanged(bool isGameForeground)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_gameActive)
            {
                if (isGameForeground)
                {
                    IsFadedOut = false;
                }
                else
                {
                    IsFadedOut = true;
                }
            }
        });
    }

    public void ResetAutoHide()
    {
        IsFadedOut = false;
        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    public void StopTimers()
    {
        _autoHideTimer.Stop();
        _pixelShiftTimer.Stop();
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Gray);
    }
}
