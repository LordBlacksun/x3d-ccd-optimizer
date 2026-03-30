using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using X3DCcdInspector.Config;
using X3DCcdInspector.ViewModels;
using X3DCcdInspector.Views;
using WinForms = System.Windows.Forms;

namespace X3DCcdInspector.Tray;

public class TrayIconManager : IDisposable
{
    private readonly WinForms.NotifyIcon _trayIcon;
    private readonly MainViewModel _viewModel;
    private readonly Window _dashboardWindow;
    private readonly Window? _overlayWindow;
    private readonly AppConfig _config;

    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _overlayItem;

    public TrayIconManager(MainViewModel viewModel, Window dashboardWindow, AppConfig config, Window? overlayWindow = null)
    {
        _viewModel = viewModel;
        _dashboardWindow = dashboardWindow;
        _overlayWindow = overlayWindow;
        _config = config;

        LoadAndSetBaseIcon();

        _statusItem = new WinForms.ToolStripMenuItem(viewModel.StatusText) { Enabled = false };
        _overlayItem = new WinForms.ToolStripMenuItem(viewModel.IsOverlayVisible ? "Hide Overlay" : "Show Overlay");

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = GetCurrentIcon(),
            Text = TruncateTooltip(viewModel.StatusText),
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _trayIcon.DoubleClick += (_, _) => ShowDashboard();

        if (dashboardWindow is DashboardWindow dw)
        {
            dw.TrayBalloonRequested += () =>
            {
                _trayIcon.BalloonTipTitle = "X3D CCD Inspector";
                _trayIcon.BalloonTipText = "The app is still running in the system tray. Right-click the tray icon to exit.";
                _trayIcon.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _trayIcon.ShowBalloonTip(3000);
            };
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsGameActive) or nameof(MainViewModel.StatusText))
        {
            _trayIcon.Icon = GetCurrentIcon();
            _trayIcon.Text = TruncateTooltip(_viewModel.StatusText);
            _statusItem.Text = _viewModel.StatusText;
        }
        else if (e.PropertyName == nameof(MainViewModel.IsOverlayVisible))
        {
            _overlayItem.Text = _viewModel.IsOverlayVisible ? "Hide Overlay" : "Show Overlay";
        }
    }

    private System.Drawing.Icon GetCurrentIcon()
    {
        var colorName = _viewModel.IsGameActive ? "green" : "blue";
        return IconGenerator.GetIcon(colorName);
    }

    private static void LoadAndSetBaseIcon()
    {
        try
        {
            var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/app.ico"));
            if (sri != null)
            {
                var icon = new System.Drawing.Icon(sri.Stream);
                IconGenerator.SetBaseIcon(icon);
            }
        }
        catch { }
    }

    private WinForms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new WinForms.ContextMenuStrip();

        menu.Items.Add(_statusItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var dashboardItem = new WinForms.ToolStripMenuItem("Open Dashboard");
        dashboardItem.Click += (_, _) =>
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(ShowDashboard);
        menu.Items.Add(dashboardItem);

        _overlayItem.Click += (_, _) =>
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                _viewModel.IsOverlayVisible = !_viewModel.IsOverlayVisible;
                _viewModel.OnPropertyChanged(nameof(MainViewModel.OverlayButtonText));
            });
        menu.Items.Add(_overlayItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var settingsItem = new WinForms.ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) =>
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                _viewModel.OpenSettingsCommand.Execute(null));
        menu.Items.Add(settingsItem);

        var logItem = new WinForms.ToolStripMenuItem("View Log File...");
        logItem.Click += (_, _) =>
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "X3DCCDInspector", "logs");
            if (Directory.Exists(logDir))
                Process.Start("explorer.exe", logDir);
        };
        menu.Items.Add(logItem);

        var aboutItem = new WinForms.ToolStripMenuItem("About");
        aboutItem.Click += (_, _) =>
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                System.Windows.MessageBox.Show(
                    "X3D CCD Inspector v1.0.0\n\nReal-time CCD visibility and control for AMD Ryzen processors.\n\nGPL v2 \u2014 github.com/LordBlacksun/x3d-ccd-optimizer",
                    "About", MessageBoxButton.OK, MessageBoxImage.Information));
        menu.Items.Add(aboutItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
                System.Windows.Application.Current.Shutdown());
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ShowDashboard()
    {
        _dashboardWindow.Show();
        _dashboardWindow.WindowState = WindowState.Normal;
        _dashboardWindow.Activate();
    }

    private static string TruncateTooltip(string text)
    {
        return text.Length > 127 ? text[..127] : text;
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }
}
