using System.ComponentModel;
using System.Windows;
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
    private const string Version = "0.2.0";
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
    private bool _hotkeyRegistered;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        AppLogger.Initialize(_config.Logging.Level);
        Log.Information("X3D Dual CCD Optimizer v{Version}", Version);

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
                $"Failed to detect CCD topology:\n\n{ex.Message}\n\nThis may not be an AMD dual-CCD processor.",
                "X3D CCD Optimizer — Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        Log.Information("CPU: {Model}", _topology.CpuModel);
        Log.Information("CCD0 (V-Cache): Cores {Cores}, L3: {L3}MB, Mask: {Mask}",
            string.Join(",", _topology.VCacheCores), _topology.VCacheL3SizeMB, _topology.VCacheMaskHex);
        Log.Information("CCD1 (Frequency): Cores {Cores}, L3: {L3}MB, Mask: {Mask}",
            string.Join(",", _topology.FrequencyCores), _topology.StandardL3SizeMB, _topology.FrequencyMaskHex);

        var mode = _config.GetOperationMode();
        if (mode == OperationMode.Optimize && !_topology.HasVCache)
        {
            Log.Warning("Config says Optimize but no V-Cache detected — falling back to Monitor");
            mode = OperationMode.Monitor;
        }

        var strategy = _config.GetOptimizeStrategy();
        if (strategy == OptimizeStrategy.DriverPreference && !VCacheDriverManager.IsDriverAvailable)
        {
            Log.Warning("Config says DriverPreference but amd3dvcache driver not detected — falling back to AffinityPinning");
            strategy = OptimizeStrategy.AffinityPinning;
        }

        // Engine
        _perfMon = new PerformanceMonitor(_topology, _config.DashboardRefreshMs);
        _gpuMonitor = new GpuMonitor();
        _gameDetector = new GameDetector(_config.ManualGames, _config.ExcludedProcesses);
        _affinityManager = new AffinityManager(_topology, _config.ProtectedProcesses, mode, strategy);
        _processWatcher = new ProcessWatcher(
            _gameDetector, _config.PollingIntervalMs, _config.AutoDetection.RequireForeground,
            _config.AutoDetection.Enabled, _config.AutoDetection.GpuThresholdPercent,
            _config.AutoDetection.DetectionDelaySeconds, _config.AutoDetection.ExitDelaySeconds,
            _gpuMonitor);

        // ViewModels
        _mainViewModel = new MainViewModel(
            _topology, _perfMon, _processWatcher, _gameDetector, _affinityManager, _config);
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
        _processWatcher.GameDetected += _affinityManager.OnGameDetected;
        _processWatcher.GameDetected += _mainViewModel.OnGameDetected;
        _processWatcher.GameDetected += _overlayViewModel.OnGameDetected;
        _processWatcher.GameExited += _affinityManager.OnGameExited;
        _processWatcher.GameExited += _mainViewModel.OnGameExited;
        _processWatcher.GameExited += _overlayViewModel.OnGameExited;

        // Start engines
        _perfMon.Start();
        _processWatcher.Start();

        Log.Information("Monitoring started. Mode: {Mode}. Strategy: {Strategy}. Polling: {Interval}ms. Manual games: {Count}.",
            mode, strategy, _config.PollingIntervalMs, _gameDetector.GameCount);

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
        _processWatcher?.Stop();
        _processWatcher?.Dispose();
        _perfMon?.Stop();
        _perfMon?.Dispose();
        _gpuMonitor?.Dispose();

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
            catch { /* shutting down */ }
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
}
