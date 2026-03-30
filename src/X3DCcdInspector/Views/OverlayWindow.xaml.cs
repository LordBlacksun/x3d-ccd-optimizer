using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using X3DCcdInspector.Config;
using X3DCcdInspector.Models;
using X3DCcdInspector.ViewModels;

namespace X3DCcdInspector.Views;

public partial class OverlayWindow : Window
{
    private readonly OverlayConfig _overlayConfig;
    private OverlayViewModel? _viewModel;
    private bool _eventsSubscribed;
    private const double SlideDistance = 60;

    private string _appliedPosition;

    public OverlayWindow(OverlayConfig overlayConfig)
    {
        _overlayConfig = overlayConfig;
        _appliedPosition = overlayConfig.OverlayPosition;
        InitializeComponent();

        // Restore position — tighter bounds to handle monitor disconnect
        if (overlayConfig.Position is [var x, var y])
        {
            var left = SystemParameters.VirtualScreenLeft;
            var top = SystemParameters.VirtualScreenTop;
            var right = left + SystemParameters.VirtualScreenWidth;
            var bottom = top + SystemParameters.VirtualScreenHeight;
            if (x >= left && x <= right - 200 && y >= top && y <= bottom - 80)
            {
                Left = x;
                Top = y;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
            else
            {
                ApplyCornerPosition(overlayConfig.OverlayPosition);
            }
        }
        else
        {
            ApplyCornerPosition(overlayConfig.OverlayPosition);
        }

        // Start hidden for slide-in
        Opacity = 0;
        SlideTransform.X = SlideDistance;

        Loaded += OnLoaded;
        IsVisibleChanged += OnVisibilityChanged;
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && _overlayConfig.OverlayPosition != _appliedPosition)
        {
            _appliedPosition = _overlayConfig.OverlayPosition;
            _overlayConfig.Position = null; // Clear saved drag position
            ApplyCornerPosition(_appliedPosition);
        }
    }

    private void ApplyCornerPosition(string position)
    {
        const double margin = 10;
        var workArea = SystemParameters.WorkArea;
        // Use a reasonable default size for initial placement (SizeToContent not yet measured)
        var w = ActualWidth > 0 ? ActualWidth : 300;
        var h = ActualHeight > 0 ? ActualHeight : 80;

        switch (position)
        {
            case "TopLeft":
                Left = workArea.Left + margin;
                Top = workArea.Top + margin;
                break;
            case "BottomLeft":
                Left = workArea.Left + margin;
                Top = workArea.Bottom - h - margin;
                break;
            case "BottomRight":
                Left = workArea.Right - w - margin;
                Top = workArea.Bottom - h - margin;
                break;
            default: // TopRight
                Left = workArea.Right - w - margin;
                Top = workArea.Top + margin;
                break;
        }
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel = DataContext as OverlayViewModel;
        if (_viewModel == null) return;

        if (!_eventsSubscribed)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.PixelShiftRequested += OnPixelShift;
            ContextMenu = BuildContextMenu();
            _eventsSubscribed = true;
        }

        // Slide in on first show
        SlideIn();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsFadedOut))
        {
            if (_viewModel!.IsFadedOut)
                SlideOut();
            else
                SlideIn();
        }
    }

    private void SlideIn()
    {
        var duration = TimeSpan.FromMilliseconds(300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var slideAnim = new DoubleAnimation(0, duration) { EasingFunction = ease };
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);

        var fadeAnim = new DoubleAnimation(_overlayConfig.Opacity, duration) { EasingFunction = ease };
        BeginAnimation(OpacityProperty, fadeAnim);
    }

    private void SlideOut()
    {
        var duration = TimeSpan.FromMilliseconds(250);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var slideAnim = new DoubleAnimation(SlideDistance, duration) { EasingFunction = ease };
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);

        var fadeAnim = new DoubleAnimation(0, duration) { EasingFunction = ease };
        BeginAnimation(OpacityProperty, fadeAnim);
    }

    private void OnPixelShift(double dx, double dy)
    {
        var newLeft = Left + dx;
        var newTop = Top + dy;

        // Clamp to virtual screen (supports multi-monitor with negative offsets)
        var sl = SystemParameters.VirtualScreenLeft;
        var st = SystemParameters.VirtualScreenTop;
        var sw = SystemParameters.VirtualScreenWidth;
        var sh = SystemParameters.VirtualScreenHeight;
        newLeft = Math.Clamp(newLeft, sl, sl + sw - ActualWidth);
        newTop = Math.Clamp(newTop, st, st + sh - ActualHeight);

        Left = newLeft;
        Top = newTop;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _viewModel?.ResetAutoHide();
        DragMove();
        SavePosition();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _viewModel?.ResetAutoHide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        SavePosition();
        Hide();

        // Unsubscribe to prevent memory leak when overlay is closed
        if (_eventsSubscribed && _viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.PixelShiftRequested -= OnPixelShift;
            _eventsSubscribed = false;
        }
    }

    public void SavePosition()
    {
        _overlayConfig.Position = [(int)Left, (int)Top];
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var dashboardItem = new MenuItem { Header = "Open Dashboard" };
        dashboardItem.Click += (_, _) =>
        {
            if (Tag is Window dashboard)
            {
                dashboard.Show();
                dashboard.WindowState = WindowState.Normal;
                dashboard.Activate();
            }
        };
        menu.Items.Add(dashboardItem);

        menu.Items.Add(new Separator());

        var closeItem = new MenuItem { Header = "Close Overlay" };
        closeItem.Click += (_, _) =>
        {
            SavePosition();
            Hide();
            if (Tag is Window { DataContext: MainViewModel mainVm })
            {
                mainVm.IsOverlayVisible = false;
                mainVm.OnPropertyChanged(nameof(MainViewModel.OverlayButtonText));
            }
        };
        menu.Items.Add(closeItem);

        return menu;
    }
}
