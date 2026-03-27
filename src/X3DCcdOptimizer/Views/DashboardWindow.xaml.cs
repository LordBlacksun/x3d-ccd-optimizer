using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.ViewModels;

namespace X3DCcdOptimizer.Views;

public partial class DashboardWindow : Window
{
    private readonly AppConfig _config;

    public DashboardWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();
        RestoreWindowState();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        SaveWindowState();
        Hide();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // Auto-scroll activity log
        if (DataContext is MainViewModel vm)
        {
            ((INotifyCollectionChanged)vm.ActivityLog.Entries).CollectionChanged += (_, _) =>
            {
                if (LogListBox.Items.Count > 0)
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                }
            };
        }
    }

    private void SaveWindowState()
    {
        if (WindowState == WindowState.Normal)
        {
            _config.Ui.WindowPosition = [(int)Left, (int)Top];
            _config.Ui.WindowSize = [(int)Width, (int)Height];
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
