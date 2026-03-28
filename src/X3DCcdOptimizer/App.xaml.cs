using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Serilog;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Core;
using X3DCcdOptimizer.Logging;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.Native;
using X3DCcdOptimizer.ViewModels;
using X3DCcdOptimizer.Views;
using X3DCcdOptimizer.Tray;

namespace X3DCcdOptimizer;

public partial class App : System.Windows.Application
{
    private const string Version = "1.0.0";
    private const int HotkeyId = 9001;

    private AppConfig _config = null!;
    private CpuTopology _topology = null!;
    private PerformanceMonitor? _perfMon;
    private ProcessWatcher? _processWatcher;
    private AffinityManager? _affinityManager;
    private GameDetector? _gameDetector;
    private GpuMonitor? _gpuMonitor;
    private MainViewModel? _mainViewModel;
    private OverlayViewModel? _overlayViewModel;
    private DashboardWindow? _dashboardWindow;
    private OverlayWindow? _overlayWindow;
    private TrayIconManager? _trayManager;
    private Mutex? _singleInstanceMutex;
    private bool _hotkeyRegistered;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance enforcement (SEC-002)
        _singleInstanceMutex = new Mutex(true, @"Global\X3DCcdOptimizer_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("X3D CCD Optimizer is already running.",
                "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Admin elevation check — must run elevated for affinity management
        if (!IsRunningAsAdministrator())
        {
            var result = MessageBox.Show(
                "X3D CCD Optimizer requires administrator privileges to manage CPU affinity on running processes.\n\n" +
                "Would you like to restart with elevated permissions?",
                "Administrator Required",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                    if (exePath != null)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exePath,
                            Verb = "runas",
                            UseShellExecute = true
                        });
                    }
                }
                catch (Win32Exception)
                {
                    // User cancelled UAC prompt
                }
            }

            Shutdown();
            return;
        }

        // Global exception handlers
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Fatal(ex, "Unhandled domain exception");
        };

        _config = AppConfig.Load();
        _config.Validate();
        AppLogger.Initialize(_config.Logging.Level);
        Log.Information("X3D Dual CCD Optimizer v{Version}", Version);

        // First-launch admin trust dialog
        if (!_config.HasDismissedAdminDialog)
        {
            ShowAdminTrustDialog();
            _config.HasDismissedAdminDialog = true;
            _config.Save();
        }

        // Recover from dirty shutdown (before anything else)
        RecoveryManager.RecoverFromDirtyShutdown();

        try
        {
            _topology = CcdMapper.Detect(_config);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to detect CCD topology");
            MessageBox.Show(
                "This application requires an AMD Ryzen processor with identifiable L3 cache topology.\n\n" +
                "It is not compatible with Intel or older AMD processors.\n\n" +
                "If you believe this is an error, check the log file for details.",
                "X3D CCD Optimizer — Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        Log.Information("CPU: {Model} | Tier: {Tier}", _topology.CpuModel, _topology.Tier);

        // Non-AMD CPU warning (ZC-003)
        if (!_topology.CpuModel.Contains("AMD", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Non-AMD CPU detected: {Model}", _topology.CpuModel);
            var result = MessageBox.Show(
                $"X3D CCD Optimizer is designed for AMD Ryzen processors.\n\n" +
                $"Your CPU ({_topology.CpuModel}) is not an AMD processor. " +
                $"The monitoring features will work, but optimization features are not applicable to your hardware.\n\n" +
                $"Continue anyway?",
                "X3D CCD Optimizer — Non-AMD CPU Detected",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                Shutdown();
                return;
            }
        }

        var mode = _config.GetOperationMode();

        // Tier-based mode gating
        if (_topology.IsSingleCcd && mode == OperationMode.Optimize)
        {
            Log.Warning("Single-CCD processor — forcing Monitor mode (no CCD steering possible)");
            mode = OperationMode.Monitor;
        }
        else if (mode == OperationMode.Optimize && !_topology.IsDualCcd)
        {
            Log.Warning("Config says Optimize but topology doesn't support it — falling back to Monitor");
            mode = OperationMode.Monitor;
        }

        var strategy = _config.GetOptimizeStrategy();
        if (strategy == OptimizeStrategy.DriverPreference && !VCacheDriverManager.IsDriverAvailable)
        {
            Log.Warning("Config says DriverPreference but amd3dvcache driver not detected — falling back to AffinityPinning");
            strategy = OptimizeStrategy.AffinityPinning;
        }
        if (strategy == OptimizeStrategy.DriverPreference && _topology.Tier != ProcessorTier.DualCcdX3D)
        {
            Log.Warning("DriverPreference only available for dual-CCD X3D — falling back to AffinityPinning");
            strategy = OptimizeStrategy.AffinityPinning;
        }

        // Power plan check for Driver Preference (ZC-002)
        string? powerPlanWarning = null;
        if (strategy == OptimizeStrategy.DriverPreference)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\cimv2\power", "SELECT ElementName FROM Win32_PowerPlan WHERE IsActive=True");
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                foreach (var obj in searcher.Get())
                {
                    var planName = obj["ElementName"]?.ToString() ?? "";
                    if (!planName.Contains("Balanced", StringComparison.OrdinalIgnoreCase))
                    {
                        powerPlanWarning = $"Power plan '{planName}' detected \u2014 Balanced recommended for Driver Preference";
                        Log.Warning("Active power plan is '{Plan}' — Balanced recommended for Driver Preference strategy", planName);
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Power plan query failed: {Error}", ex.Message);
            }
        }

        // Scan installed game launchers (Steam, Epic)
        var launcherGames = GameLibraryScanner.LoadOrScan();

        // Engine
        _perfMon = new PerformanceMonitor(_topology, _config.DashboardRefreshMs);
        _gpuMonitor = new GpuMonitor();
        _gameDetector = new GameDetector(_config.ManualGames, _config.ExcludedProcesses, launcherGames);
        _affinityManager = new AffinityManager(_topology, _config.ProtectedProcesses, mode, strategy);
        _processWatcher = new ProcessWatcher(
            _gameDetector, _config.PollingIntervalMs, _config.AutoDetection.RequireForeground,
            _config.AutoDetection.Enabled, _config.AutoDetection.GpuThresholdPercent,
            _config.AutoDetection.DetectionDelaySeconds, _config.AutoDetection.ExitDelaySeconds,
            _gpuMonitor);

        // ViewModels
        _mainViewModel = new MainViewModel(
            _topology, _perfMon, _processWatcher, _gameDetector, _affinityManager, _config,
            powerPlanWarning);
        _overlayViewModel = new OverlayViewModel(_topology, _config.Overlay);

        // Dashboard
        _dashboardWindow = new DashboardWindow(_config);
        _dashboardWindow.DataContext = _mainViewModel;

        // Overlay
        _overlayWindow = new OverlayWindow(_config.Overlay);
        _overlayWindow.DataContext = _overlayViewModel;
        _overlayWindow.Tag = _dashboardWindow; // Store reference without Owner (avoids must-show-first requirement)

        // Overlay visibility toggle
        _mainViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsOverlayVisible))
            {
                if (_mainViewModel.IsOverlayVisible)
                {
                    _overlayWindow.Show();
                    _overlayViewModel.ResetAutoHide();
                }
                else
                {
                    _overlayWindow.Hide();
                }
            }
            else if (e.PropertyName is nameof(MainViewModel.CurrentMode) or nameof(MainViewModel.IsGameActive))
            {
                _overlayViewModel.OnModeChanged(_mainViewModel.CurrentMode, _mainViewModel.IsGameActive);
            }
        };

        // Tray
        _trayManager = new TrayIconManager(_mainViewModel, _dashboardWindow, _config, _overlayWindow);

        // Wire engine events
        _perfMon.SnapshotReady += _mainViewModel.OnSnapshotReady;
        _perfMon.SnapshotReady += _overlayViewModel.OnSnapshotReady;
        _affinityManager.AffinityChanged += _mainViewModel.OnAffinityChanged;
        _affinityManager.AffinityChanged += _overlayViewModel.OnAffinityChanged;
        _processWatcher.DetectionSkipped += _mainViewModel.OnAffinityChanged;
        _processWatcher.GameDetected += _affinityManager.OnGameDetected;
        _processWatcher.GameDetected += _mainViewModel.OnGameDetected;
        _processWatcher.GameDetected += _overlayViewModel.OnGameDetected;
        _processWatcher.GameDetected += game => { if (_gpuMonitor != null) _gpuMonitor.IsGameActive = true; };
        _processWatcher.GameExited += _affinityManager.OnGameExited;
        _processWatcher.GameExited += _mainViewModel.OnGameExited;
        _processWatcher.GameExited += _overlayViewModel.OnGameExited;
        _processWatcher.GameExited += game => { if (_gpuMonitor != null) _gpuMonitor.IsGameActive = false; };

        // Start engines
        _perfMon.Start();
        _processWatcher.Start();

        Log.Information("Monitoring started. Mode: {Mode}. Strategy: {Strategy}. Polling: {Interval}ms. Manual games: {Count}.",
            mode, strategy, _config.PollingIntervalMs, _gameDetector.GameCount);

        // Background rescan if launcher cache is stale (>7 days)
        if (GameLibraryScanner.IsCacheStale())
        {
            Task.Run(() =>
            {
                try
                {
                    var freshGames = GameLibraryScanner.ScanAll();
                    GameLibraryScanner.SaveCache(freshGames);
                    _gameDetector.UpdateLauncherGames(freshGames);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Background launcher rescan failed");
                }
            });
        }

        // Register hotkey (after window is created so we have an HWND)
        _dashboardWindow.SourceInitialized += (_, _) => RegisterOverlayHotkey();

        var startMinimized = _config.Ui.StartMinimized ||
            (e.Args.Length > 0 && e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase));
        if (!startMinimized)
            _dashboardWindow.Show();

        if (_config.Overlay.Enabled)
        {
            _mainViewModel.IsOverlayVisible = true;
            _mainViewModel.OnPropertyChanged(nameof(MainViewModel.OverlayButtonText));
        }
    }

    private void RegisterOverlayHotkey()
    {
        try
        {
            var hwnd = new WindowInteropHelper(_dashboardWindow!).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);

            _hotkeyRegistered = User32.RegisterHotKey(
                hwnd, HotkeyId,
                User32.MOD_CONTROL | User32.MOD_SHIFT | User32.MOD_NOREPEAT,
                User32.VK_O);

            if (_hotkeyRegistered)
                Log.Information("Overlay hotkey registered: Ctrl+Shift+O");
            else
                Log.Warning("Failed to register overlay hotkey (Ctrl+Shift+O) — may be in use by another app");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register overlay hotkey");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            if (_mainViewModel != null)
            {
                _mainViewModel.IsOverlayVisible = true;
                _mainViewModel.OnPropertyChanged(nameof(MainViewModel.OverlayButtonText));
                _overlayViewModel?.ResetAutoHide();
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Unwire events before disposing to prevent callbacks into disposed objects
        if (_perfMon != null && _mainViewModel != null)
        {
            _perfMon.SnapshotReady -= _mainViewModel.OnSnapshotReady;
            if (_overlayViewModel != null)
                _perfMon.SnapshotReady -= _overlayViewModel.OnSnapshotReady;
        }
        if (_affinityManager != null && _mainViewModel != null && _overlayViewModel != null)
        {
            _affinityManager.AffinityChanged -= _mainViewModel.OnAffinityChanged;
            _affinityManager.AffinityChanged -= _overlayViewModel.OnAffinityChanged;
        }
        if (_processWatcher != null && _affinityManager != null && _mainViewModel != null && _overlayViewModel != null)
        {
            _processWatcher.DetectionSkipped -= _mainViewModel.OnAffinityChanged;
            _processWatcher.GameDetected -= _affinityManager.OnGameDetected;
            _processWatcher.GameDetected -= _mainViewModel.OnGameDetected;
            _processWatcher.GameDetected -= _overlayViewModel.OnGameDetected;
            _processWatcher.GameExited -= _affinityManager.OnGameExited;
            _processWatcher.GameExited -= _mainViewModel.OnGameExited;
            _processWatcher.GameExited -= _overlayViewModel.OnGameExited;
        }

        _processWatcher?.Stop();
        _processWatcher?.Dispose();
        _perfMon?.Stop();
        _perfMon?.Dispose();
        _gpuMonitor?.Dispose();
        _affinityManager?.Dispose();

        // Clean shutdown — clear recovery state
        RecoveryManager.OnDisengage();

        _overlayViewModel?.StopTimers();

        // Unregister hotkey
        if (_hotkeyRegistered && _dashboardWindow != null)
        {
            try
            {
                var hwnd = new WindowInteropHelper(_dashboardWindow).Handle;
                if (hwnd != IntPtr.Zero)
                    User32.UnregisterHotKey(hwnd, HotkeyId);
            }
            catch (Exception ex) { Log.Debug(ex, "Failed to unregister hotkey during shutdown"); }
        }

        // Save state
        if (_mainViewModel != null)
            _config.OperationMode = _mainViewModel.CurrentMode.ToString().ToLowerInvariant();

        if (_dashboardWindow != null && _dashboardWindow.WindowState == WindowState.Normal)
        {
            _config.Ui.WindowPosition = [(int)_dashboardWindow.Left, (int)_dashboardWindow.Top];
            _config.Ui.WindowSize = [(int)_dashboardWindow.Width, (int)_dashboardWindow.Height];
        }

        _overlayWindow?.SavePosition();
        _config.Overlay.Enabled = _mainViewModel?.IsOverlayVisible ?? false;

        _config.Save();

        _trayManager?.Dispose();

        Log.Information("X3D Dual CCD Optimizer stopped.");
        AppLogger.Shutdown();

        base.OnExit(e);
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void ShowAdminTrustDialog()
    {
        var dialog = new Window
        {
            Title = "Why does this app need Admin rights?",
            Width = 520,
            Height = 380,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = TryFindResource("BgPrimaryBrush") as System.Windows.Media.Brush
                ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1E))
        };

        var panel = new StackPanel { Margin = new Thickness(24) };

        var primaryBrush = TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
        var secondaryBrush = TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.LightGray;
        var tertiaryBrush = TryFindResource("TextTertiaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;

        panel.Children.Add(new TextBlock
        {
            Text = "X3D CCD Optimizer requires administrator privileges to set CPU affinity on running processes. Without elevation, Windows blocks affinity changes on most applications.",
            FontSize = 13,
            Foreground = primaryBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "This app is:",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = primaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var points = new[]
        {
            "\u2022  Fully open source \u2014 audit every line at github.com/LordBlacksun/x3d-ccd-optimizer",
            "\u2022  Private by default \u2014 no telemetry, no tracking, no network connections",
            "\u2022  Minimal \u2014 only reads process lists and sets CPU affinity, nothing else"
        };

        foreach (var point in points)
        {
            panel.Children.Add(new TextBlock
            {
                Text = point,
                FontSize = 12,
                Foreground = secondaryBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2)
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Admin rights are used exclusively in AffinityManager.cs and CcdMapper.cs.",
            FontSize = 11,
            Foreground = tertiaryBrush,
            Margin = new Thickness(0, 16, 0, 20),
            TextWrapping = TextWrapping.Wrap
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var viewSourceButton = new Button
        {
            Content = "View Source on GitHub",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        viewSourceButton.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/LordBlacksun/x3d-ccd-optimizer",
                UseShellExecute = true
            });
        };

        var continueButton = new Button
        {
            Content = "I Understand, Continue",
            Padding = new Thickness(12, 6, 12, 6),
            IsDefault = true
        };
        continueButton.Click += (_, _) => dialog.Close();

        buttonPanel.Children.Add(viewSourceButton);
        buttonPanel.Children.Add(continueButton);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;
        dialog.ShowDialog();
    }
}
