using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using X3DCcdInspector.Config;
using X3DCcdInspector.ViewModels;

namespace X3DCcdInspector.Views;

public partial class DashboardWindow : Window
{
    private readonly AppConfig _config;
    private bool _trayBalloonShown;
    private NotifyCollectionChangedEventHandler? _logScrollHandler;

    /// <summary>Raised once when the window first minimizes to tray, to trigger a balloon tip.</summary>
    public event Action? TrayBalloonRequested;

    public DashboardWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();
        RestoreWindowState();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_config.Ui.MinimizeToTray)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            SaveWindowState();
            Hide();

            if (!_trayBalloonShown)
            {
                _trayBalloonShown = true;
                TrayBalloonRequested?.Invoke();
            }
        }
        else
        {
            // Close the app
            SaveWindowState();
            Application.Current.Shutdown();
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // Auto-scroll activity log
        if (DataContext is MainViewModel vm)
        {
            _logScrollHandler = (_, _) =>
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
            };
            ((INotifyCollectionChanged)vm.ActivityLog.Entries).CollectionChanged += _logScrollHandler;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unsubscribe to prevent memory leak
        if (_logScrollHandler != null && DataContext is MainViewModel vm)
        {
            ((INotifyCollectionChanged)vm.ActivityLog.Entries).CollectionChanged -= _logScrollHandler;
            _logScrollHandler = null;
        }

        base.OnClosed(e);
    }

    private void SaveWindowState()
    {
        if (WindowState == WindowState.Normal)
        {
            _config.Ui.WindowPosition = [(int)Left, (int)Top];
            _config.Ui.WindowSize = [(int)Width, (int)Height];
        }
    }

    private void OnExclusionItemClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: RunningProcessViewModel item }
            && DataContext is MainViewModel vm)
        {
            vm.ProcessExclusions?.ToggleExclusion(item);
        }
    }

    private void RestoreWindowState()
    {
        if (_config.Ui.WindowPosition is [var x, var y])
        {
            var left = SystemParameters.VirtualScreenLeft;
            var top = SystemParameters.VirtualScreenTop;
            var right = left + SystemParameters.VirtualScreenWidth;
            var bottom = top + SystemParameters.VirtualScreenHeight;

            if (x >= left && x < right - 100 && y >= top && y < bottom - 100)
            {
                Left = x;
                Top = y;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }

        if (_config.Ui.WindowSize is [var w, var h])
        {
            if (w >= (int)MinWidth && h >= (int)MinHeight)
            {
                Width = w;
                Height = h;
            }
        }
    }
}
