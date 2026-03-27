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

    private string _modeText = "Monitor";
    private SolidColorBrush _modeDotColor;
    private string _gameName = "No game detected";
    private SolidColorBrush _gameNameColor;
    private double _ccd0Load;
    private double _ccd1Load;
    private string _lastActionText = "";
    private double _overlayOpacity;
    private bool _isFadedOut;

    public string ModeText
    {
        get => _modeText;
        set => SetProperty(ref _modeText, value);
    }

    public SolidColorBrush ModeDotColor
    {
        get => _modeDotColor;
        set => SetProperty(ref _modeDotColor, value);
    }

    public string GameName
    {
        get => _gameName;
        set => SetProperty(ref _gameName, value);
    }

    public SolidColorBrush GameNameColor
    {
        get => _gameNameColor;
        set => SetProperty(ref _gameNameColor, value);
    }

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

    public string LastActionText
    {
        get => _lastActionText;
        set => SetProperty(ref _lastActionText, value);
    }

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
        _gameNameColor = FindBrush("TextTertiaryBrush");

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
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var ccd0 = snapshots.Where(s => s.CcdIndex == 0);
            var ccd1 = snapshots.Where(s => s.CcdIndex == 1);

            var c0 = ccd0.ToArray();
            var c1 = ccd1.ToArray();

            Ccd0Load = c0.Length > 0 ? c0.Average(s => s.LoadPercent) : 0;
            Ccd1Load = c1.Length > 0 ? c1.Average(s => s.LoadPercent) : 0;
        });
    }

    public void OnAffinityChanged(AffinityEvent evt)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var prefix = evt.Action switch
            {
                AffinityAction.Engaged => "ENGAGE",
                AffinityAction.Migrated => "MIGRATE",
                AffinityAction.Restored => "RESTORE",
                AffinityAction.WouldEngage => "[M] WOULD ENGAGE",
                AffinityAction.WouldMigrate => "[M] WOULD MIGRATE",
                AffinityAction.WouldRestore => "[M] WOULD RESTORE",
                AffinityAction.Skipped => "SKIP",
                AffinityAction.Error => "ERROR",
                AffinityAction.DriverSet => "DRIVER SET",
                AffinityAction.DriverRestored => "DRIVER RESTORE",
                AffinityAction.WouldSetDriver => "[M] DRIVER SET",
                AffinityAction.WouldRestoreDriver => "[M] DRIVER RESTORE",
                _ => ""
            };
            LastActionText = $"{prefix}: {evt.ProcessName}";
            ResetAutoHide();
        });
    }

    public void OnGameDetected(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            GameName = game.Name;
            GameNameColor = FindBrush("TextPrimaryBrush");
            ResetAutoHide();
        });
    }

    public void OnGameExited(ProcessInfo game)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            GameName = "No game detected";
            GameNameColor = FindBrush("TextTertiaryBrush");
            LastActionText = "";
            ResetAutoHide();
        });
    }

    public void OnModeChanged(OperationMode mode, bool gameActive)
    {
        ModeText = mode.ToString();
        ModeDotColor = mode switch
        {
            OperationMode.Monitor => FindBrush("AccentBlueBrush"),
            OperationMode.Optimize when gameActive => FindBrush("AccentGreenBrush"),
            OperationMode.Optimize => FindBrush("AccentPurpleBrush"),
            _ => FindBrush("AccentBlueBrush")
        };
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
