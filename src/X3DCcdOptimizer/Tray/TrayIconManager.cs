using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.ViewModels;
using WinForms = System.Windows.Forms;

namespace X3DCcdOptimizer.Tray;

public class TrayIconManager : IDisposable
{
    private readonly WinForms.NotifyIcon _trayIcon;
    private readonly MainViewModel _viewModel;
    private readonly Window _dashboardWindow;
    private readonly Window? _overlayWindow;
    private readonly AppConfig _config;

    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _monitorItem;
    private readonly WinForms.ToolStripMenuItem _optimizeItem;
    private readonly WinForms.ToolStripMenuItem _overlayItem;

    public TrayIconManager(MainViewModel viewModel, Window dashboardWindow, AppConfig config, Window? overlayWindow = null)
    {
        _viewModel = viewModel;
        _dashboardWindow = dashboardWindow;
        _overlayWindow = overlayWindow;
        _config = config;

        // Load app icon and set as base for compositing
        LoadAndSetBaseIcon();

        _statusItem = new WinForms.ToolStripMenuItem(viewModel.StatusText) { Enabled = false };
        _monitorItem = new WinForms.ToolStripMenuItem("Mode: Monitor");
        _optimizeItem = new WinForms.ToolStripMenuItem("Mode: Optimize") { Enabled = viewModel.IsOptimizeEnabled };
        _overlayItem = new WinForms.ToolStripMenuItem(viewModel.IsOverlayVisible ? "Hide Overlay" : "Show Overlay");

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = GetCurrentIcon(),
            Text = TruncateTooltip(viewModel.StatusText),
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _trayIcon.DoubleClick += (_, _) => ShowDashboard();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentMode) or nameof(MainViewModel.IsGameActive) or nameof(MainViewModel.StatusText))
        {
            _trayIcon.Icon = GetCurrentIcon();
            _trayIcon.Text = TruncateTooltip(_viewModel.StatusText);
            _statusItem.Text = _viewModel.StatusText;
            _monitorItem.Checked = _viewModel.CurrentMode == OperationMode.Monitor;
            _optimizeItem.Checked = _viewModel.CurrentMode == OperationMode.Optimize;
        }
        else if (e.PropertyName == nameof(MainViewModel.IsOverlayVisible))
        {
            _overlayItem.Text = _viewModel.IsOverlayVisible ? "Hide Overlay" : "Show Overlay";
        }
    }

    /// <summary>
    /// Returns app icon with colored status dot composited in the bottom-right corner.
    /// Blue = Monitor idle, Purple = Monitor + game observed, Green = Optimize + game engaged.
    /// </summary>
    private System.Drawing.Icon GetCurrentIcon()
    {
        var colorName = (_viewModel.CurrentMode, _viewModel.IsGameActive) switch
        {
            (OperationMode.Optimize, true) => "green",
            (OperationMode.Monitor, true) => "purple",
            (OperationMode.Optimize, false) => "purple",
            _ => "blue"
        };
        return IconGenerator.GetIcon(colorName);
    }

    private static void LoadAndSetBaseIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
            if (File.Exists(iconPath))
            {
                var icon = new System.Drawing.Icon(iconPath);
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

        _monitorItem.Checked = _viewModel.CurrentMode == OperationMode.Monitor;
        _monitorItem.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                _viewModel.CurrentMode = OperationMode.Monitor);

        _optimizeItem.Checked = _viewModel.CurrentMode == OperationMode.Optimize;
        _optimizeItem.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                _viewModel.CurrentMode = OperationMode.Optimize);

        menu.Items.Add(_monitorItem);
        menu.Items.Add(_optimizeItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var dashboardItem = new WinForms.ToolStripMenuItem("Open Dashboard");
        dashboardItem.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(ShowDashboard);
        menu.Items.Add(dashboardItem);

        _overlayItem.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _viewModel.IsOverlayVisible = !_viewModel.IsOverlayVisible;
                _viewModel.OnPropertyChanged(nameof(MainViewModel.OverlayButtonText));
            });
        menu.Items.Add(_overlayItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var settingsItem = new WinForms.ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                _viewModel.OpenSettingsCommand.Execute(null));
        menu.Items.Add(settingsItem);

        var logItem = new WinForms.ToolStripMenuItem("View Log File...");
        logItem.Click += (_, _) =>
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "X3DCCDOptimizer", "logs");
            if (Directory.Exists(logDir))
                Process.Start("explorer.exe", logDir);
        };
        menu.Items.Add(logItem);

        var aboutItem = new WinForms.ToolStripMenuItem("About");
        aboutItem.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                System.Windows.MessageBox.Show(
                    "X3D Dual CCD Optimizer v1.0.0\n\nMonitor and optimize CCD affinity for AMD Ryzen processors.\n\nGPL v2 — github.com/LordBlacksun/x3d-ccd-optimizer",
                    "About", MessageBoxButton.OK, MessageBoxImage.Information));
        menu.Items.Add(aboutItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
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
