using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.ViewModels;

namespace X3DCcdOptimizer.Views;

public partial class OverlayWindow : Window
{
    private readonly OverlayConfig _overlayConfig;
    private OverlayViewModel? _viewModel;
    private bool _eventsSubscribed;

    public OverlayWindow(OverlayConfig overlayConfig)
    {
        _overlayConfig = overlayConfig;
        InitializeComponent();

        // Restore position
        if (overlayConfig.Position is [var x, var y])
        {
            var left = SystemParameters.VirtualScreenLeft;
            var top = SystemParameters.VirtualScreenTop;
            var right = left + SystemParameters.VirtualScreenWidth;
            var bottom = top + SystemParameters.VirtualScreenHeight;
            if (x >= left && x < right - 50 && y >= top && y < bottom - 50)
            {
                Left = x;
                Top = y;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }
        else
        {
            // Default: top-right corner with margin
            Left = SystemParameters.PrimaryScreenWidth - Width - 20;
            Top = 20;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        Loaded += OnLoaded;
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
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.IsFadedOut))
        {
            if (_viewModel!.IsFadedOut)
                AnimateOpacity(0.0, 500);
            else
                AnimateOpacity(_overlayConfig.Opacity, 200);
        }
    }

    private void AnimateOpacity(double to, int durationMs)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, anim);
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
        newLeft = Math.Clamp(newLeft, sl, sl + sw - Width);
        newTop = Math.Clamp(newTop, st, st + sh - Height);

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

        var toggleItem = new MenuItem { Header = "Toggle Mode" };
        toggleItem.Click += (_, _) =>
        {
            if (Tag is Window { DataContext: MainViewModel mainVm })
            {
                if (mainVm.IsOptimizeEnabled)
                    mainVm.IsOptimizeMode = !mainVm.IsOptimizeMode;
            }
        };
        menu.Items.Add(toggleItem);

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
