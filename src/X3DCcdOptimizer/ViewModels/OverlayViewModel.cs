using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Core;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.ViewModels;

public class OverlayViewModel : ViewModelBase
{
    private readonly CpuTopology _topology;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly DispatcherTimer _pixelShiftTimer;
    private readonly Random _rng = new();
    private readonly OverlayConfig _overlayConfig;

    private SolidColorBrush _modeDotColor;
    private string _primaryText = "Monitoring";
    private string _secondaryText = "";
    private double _overlayOpacity;
    private bool _isFadedOut;

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

    public void OnAffinityChanged(AffinityEvent evt)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var display = evt.DisplayName ?? evt.ProcessName;
            switch (evt.Action)
            {
                case AffinityAction.Engaged:
                    SecondaryText = $"\u2192 V-Cache CCD";
                    break;
                case AffinityAction.WouldEngage:
                    SecondaryText = $"\u2192 V-Cache CCD (monitor)";
                    break;
                case AffinityAction.DriverSet:
                    SecondaryText = "V-Cache preferred (driver)";
                    break;
                case AffinityAction.WouldSetDriver:
                    SecondaryText = "V-Cache preferred (monitor)";
                    break;
                case AffinityAction.Restored:
                case AffinityAction.WouldRestore:
                case AffinityAction.DriverRestored:
                case AffinityAction.WouldRestoreDriver:
                    SecondaryText = "Affinities restored";
                    break;
                case AffinityAction.Error:
                    PrimaryText = display;
                    SecondaryText = "Error: " + evt.Detail;
                    break;
                default:
                    return; // Don't reset auto-hide for Migrated/Skipped/etc.
            }

            ResetAutoHide();
        });
    }

    public void OnGameDetected(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            PrimaryText = game.DisplayName ?? game.Name;
            // SecondaryText will be set by the subsequent AffinityChanged event
            ResetAutoHide();
        });
    }

    public void OnGameExited(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            PrimaryText = game.DisplayName ?? game.Name;
            SecondaryText = "Session ended";
            ResetAutoHide();
        });
    }

    public void OnModeChanged(OperationMode mode, bool gameActive)
    {
        ModeDotColor = mode switch
        {
            OperationMode.Monitor => FindBrush("AccentBlueBrush"),
            OperationMode.Optimize when gameActive => FindBrush("AccentGreenBrush"),
            OperationMode.Optimize => FindBrush("AccentPurpleBrush"),
            _ => FindBrush("AccentBlueBrush")
        };

        if (!gameActive)
        {
            PrimaryText = mode == OperationMode.Optimize ? "Optimize \u2014 ready" : "Monitoring";
            SecondaryText = "";
        }

        ResetAutoHide();
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
