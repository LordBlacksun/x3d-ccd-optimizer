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
    private GameDatabase? _gameDb;
    private Mutex? _singleInstanceMutex;
    private bool _hotkeyRegistered;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance enforcement (SEC-002)
        _singleInstanceMutex = new Mutex(true, @"Global\{B7F3A2E1-5D4C-4E8B-9F1A-3C6D8E2B7A50}", out var createdNew);
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
                // Release singleton mutex BEFORE launching elevated instance
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;

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

            Environment.Exit(0);
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

        // Non-AMD CPU — exit with friendly message
        if (!_topology.CpuModel.Contains("AMD", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Non-AMD CPU detected: {Model}. Exiting.", _topology.CpuModel);
            ShowUnsupportedProcessorDialog(_topology);
            Shutdown();
            return;
        }

        // Single-CCD — exit with friendly message
        if (!_topology.IsSupported)
        {
            Log.Information("Single-CCD processor detected ({Tier}). Not supported — exiting.", _topology.Tier);
            ShowUnsupportedProcessorDialog(_topology);
            Shutdown();
            return;
        }

        var mode = _config.GetOperationMode();

        // Tier-aware default strategy (on first run, set optimal default)
        if (_config.IsFirstRun)
        {
            var defaultStrategy = _topology.Tier switch
            {
                ProcessorTier.DualCcdX3D when VCacheDriverManager.IsDriverAvailable => "driverPreference",
                _ => "affinityPinning"
            };
            _config.OptimizeStrategy = defaultStrategy;
            Log.Information("First run — default strategy set to {Strategy} for {Tier}", defaultStrategy, _topology.Tier);
        }

        var strategy = _config.GetOptimizeStrategy();
        if (strategy == OptimizeStrategy.DriverPreference && !VCacheDriverManager.IsDriverAvailable)
        {
            Log.Warning("Config says DriverPreference but amd3dvcache driver not detected — falling back to AffinityPinning");
            strategy = OptimizeStrategy.AffinityPinning;
        }
        if (strategy == OptimizeStrategy.DriverPreference &&
            _topology.Tier is not ProcessorTier.DualCcdX3D)
        {
            Log.Warning("DriverPreference requires X3D processor — falling back to AffinityPinning");
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

        // Offer new default exclusions if any are missing from user's config
        var newExclusions = _config.GetNewDefaultExclusions();
        if (newExclusions.Count > 0 && !_config.IsFirstRun)
        {
            if (ShowNewExclusionsPrompt(newExclusions))
            {
                _config.ExcludedProcesses.AddRange(newExclusions);
                _config.Save();
                Log.Information("User accepted {Count} new default exclusions", newExclusions.Count);
            }
        }

        // Game library database (LiteDB)
        _gameDb = new GameDatabase();
        _gameDb.MigrateFromJsonCache();
        _gameDb.Deduplicate();
        var launcherGames = _gameDb.ToDictionary();

        // Engine
        _perfMon = new PerformanceMonitor(_topology, _config.DashboardRefreshMs);
        _gpuMonitor = new GpuMonitor();
        _gameDetector = new GameDetector(_config.ManualGames, _config.ExcludedProcesses, launcherGames, _config.BackgroundApps);
        _affinityManager = new AffinityManager(_topology, _config.ProtectedProcesses, mode, strategy, _config.BackgroundApps);
        _affinityManager.UpdateGameProfiles(_config.GameProfiles);
        _processWatcher = new ProcessWatcher(
            _gameDetector, _config.PollingIntervalMs, _config.AutoDetection.RequireForeground,
            _config.AutoDetection.Enabled, _config.AutoDetection.GpuThresholdPercent,
            _config.AutoDetection.DetectionDelaySeconds, _config.AutoDetection.ExitDelaySeconds,
            _gpuMonitor);

        // ViewModels
        _mainViewModel = new MainViewModel(
            _topology, _perfMon, _processWatcher, _gameDetector, _affinityManager, _config,
            powerPlanWarning);
        _mainViewModel.InitGameLibrary(_gameDb, _config.ExcludedProcesses);
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

        // Library scan — consent-gated
        if (_config.LibraryScanConsent == null)
        {
            // First time — ask user
            var consent = ShowLibraryScanConsentDialog();
            if (consent.HasValue)
            {
                _config.LibraryScanConsent = consent.Value;
                _config.Save();
            }
            // consent == null means "Skip" (ask again next time)
        }

        if (_config.LibraryScanConsent == true)
        {
            RunLibraryScan();
        }

        // Optional update check (off by default, no more than once per 24h)
        if (_config.CheckForUpdates)
        {
            var shouldCheck = true;
            if (DateTime.TryParse(_config.LastUpdateCheckUtc, out var lastCheck))
                shouldCheck = (DateTime.UtcNow - lastCheck).TotalHours >= 24;

            if (shouldCheck)
            {
                Task.Run(async () =>
                {
                    var newVersion = await UpdateChecker.CheckForUpdate();
                    _config.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
                    _config.Save();

                    if (newVersion != null)
                    {
                        Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            if (_mainViewModel != null)
                                _mainViewModel.UpdateText = $"v{newVersion} available";
                        });
                    }
                });
            }
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
        _gameDb?.Dispose();

        // Clean shutdown — clear recovery state
        RecoveryManager.OnDisengage();

        _overlayViewModel?.StopTimers();
        _mainViewModel?.ProcessRouter.Dispose();

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

    /// <summary>
    /// Shows a prompt offering new default exclusions. Returns true if user accepts.
    /// </summary>
    private bool ShowNewExclusionsPrompt(List<string> newExclusions)
    {
        var list = string.Join("\n", newExclusions.Select(e => $"  \u2022  {e}"));
        var result = MessageBox.Show(
            $"New default exclusions are available. These processes use GPU but aren't games, " +
            $"and would cause false detections:\n\n{list}\n\n" +
            $"Add them to your exclusion list?",
            "X3D CCD Optimizer \u2014 Updated Exclusions",
            MessageBoxButton.YesNo, MessageBoxImage.Information);
        return result == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Shows a consent dialog for library scanning. Returns true (Scan Now), false (Don't Ask Again), or null (Skip).
    /// </summary>
    private bool? ShowLibraryScanConsentDialog()
    {
        var dialog = new Window
        {
            Title = "X3D CCD Optimizer \u2014 Game Library Scan",
            Width = 460,
            Height = 260,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = TryFindResource("BgPrimaryBrush") as System.Windows.Media.Brush
                ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1E))
        };

        bool? result = null;
        var panel = new StackPanel { Margin = new Thickness(24) };
        var primaryBrush = TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
        var secondaryBrush = TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.LightGray;

        panel.Children.Add(new TextBlock
        {
            Text = "Would you like to scan your game libraries?",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = primaryBrush,
            Margin = new Thickness(0, 0, 0, 12)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "This scans your local Steam, Epic Games, and GOG Galaxy install directories to automatically recognize your games. No network connections are made \u2014 this only reads files on your computer.\n\nYou can always trigger a scan later from Settings \u2192 Detection.",
            FontSize = 12,
            Foreground = secondaryBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var dontAskButton = new Button
        {
            Content = "Don\u2019t Ask Again",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        dontAskButton.Click += (_, _) => { result = false; dialog.Close(); };

        var skipButton = new Button
        {
            Content = "Skip",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        skipButton.Click += (_, _) => { result = null; dialog.Close(); };

        var scanButton = new Button
        {
            Content = "Scan Now",
            Padding = new Thickness(12, 6, 12, 6),
            IsDefault = true
        };
        scanButton.Click += (_, _) => { result = true; dialog.Close(); };

        buttonPanel.Children.Add(dontAskButton);
        buttonPanel.Children.Add(skipButton);
        buttonPanel.Children.Add(scanButton);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;
        dialog.ShowDialog();
        return result;
    }

    private void RunLibraryScan()
    {
        Task.Run(async () =>
        {
            try
            {
                var scanned = GameLibraryScanner.ScanAll();
                _gameDb?.ReplaceGames(scanned);
                _gameDetector?.UpdateLauncherGames(_gameDb?.ToDictionary() ?? new());

                Application.Current?.Dispatcher.BeginInvoke(() =>
                    _mainViewModel?.GameLibrary?.Refresh());

                if (_config.EnableArtworkDownload && _gameDb != null)
                {
                    await ArtworkDownloader.DownloadAllMissing(_gameDb);
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                        _mainViewModel?.GameLibrary?.Refresh());
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background library scan failed");
            }
        });
    }

    private void ShowUnsupportedProcessorDialog(CpuTopology topology)
    {
        string title;
        string message;

        if (!topology.CpuModel.Contains("AMD", StringComparison.OrdinalIgnoreCase))
        {
            title = "X3D CCD Optimizer \u2014 Unsupported Processor";
            message = $"This tool is designed for AMD Ryzen dual-CCD processors.\n\n" +
                $"Your processor ({topology.CpuModel}) was not recognized as a supported chip.";
        }
        else if (topology.Tier == ProcessorTier.SingleCcdX3D)
        {
            title = "X3D CCD Optimizer \u2014 Single-CCD Processor";
            message = $"Your {topology.CpuModel} has V-Cache on all cores \u2014 every core already has " +
                $"access to the full cache.\n\n" +
                $"The CCD scheduling problem this tool solves only exists on dual-CCD processors. " +
                $"Your chip doesn\u2019t need this, and that\u2019s a good thing!";
        }
        else
        {
            title = "X3D CCD Optimizer \u2014 Single-CCD Processor";
            message = $"Your {topology.CpuModel} has a single CCD.\n\n" +
                $"This tool optimizes game scheduling across two CCDs and is not applicable " +
                $"to single-CCD processors.";
        }

        var dialog = new Window
        {
            Title = title,
            Width = 480,
            Height = 280,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = TryFindResource("BgPrimaryBrush") as System.Windows.Media.Brush
                ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1E))
        };

        var panel = new StackPanel { Margin = new Thickness(24) };
        var primaryBrush = TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
        var secondaryBrush = TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.LightGray;

        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = primaryBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "For more information, visit our Wiki on GitHub.",
            FontSize = 11,
            Foreground = secondaryBrush,
            Margin = new Thickness(0, 0, 0, 20)
        });

        var closeButton = new Button
        {
            Content = "Close",
            Padding = new Thickness(16, 6, 16, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };
        closeButton.Click += (_, _) => dialog.Close();
        panel.Children.Add(closeButton);

        dialog.Content = panel;
        dialog.ShowDialog();
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
            "\u2022  Private by default \u2014 no telemetry, no tracking, no network connections. Optional game artwork downloads connect only to Steam\u2019s public CDN \u2014 no data is ever sent.",
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
            Margin = new Thickness(0, 16, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = "For best results with Driver Preference mode, set CPPC Dynamic Preferred Cores to \u2018Driver\u2019 in your BIOS. See our Wiki for setup details.",
            FontSize = 11,
            Foreground = secondaryBrush,
            Margin = new Thickness(0, 0, 0, 20),
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
