using System.Windows;
using Serilog;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Core;
using X3DCcdOptimizer.Logging;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.ViewModels;
using X3DCcdOptimizer.Views;
using X3DCcdOptimizer.Tray;

namespace X3DCcdOptimizer;

public partial class App : System.Windows.Application
{
    private const string Version = "0.2.0";

    private AppConfig _config = null!;
    private CpuTopology _topology = null!;
    private PerformanceMonitor? _perfMon;
    private ProcessWatcher? _processWatcher;
    private AffinityManager? _affinityManager;
    private GameDetector? _gameDetector;
    private MainViewModel? _mainViewModel;
    private DashboardWindow? _dashboardWindow;
    private TrayIconManager? _trayManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load config
        _config = AppConfig.Load();

        // Initialize logger
        AppLogger.Initialize(_config.Logging.Level);
        Log.Information("X3D Dual CCD Optimizer v{Version}", Version);

        // Detect topology
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

        // Determine operation mode
        var mode = _config.GetOperationMode();
        if (mode == OperationMode.Optimize && !_topology.HasVCache)
        {
            Log.Warning("Config says Optimize but no V-Cache detected — falling back to Monitor");
            mode = OperationMode.Monitor;
        }

        // Create engine instances
        _perfMon = new PerformanceMonitor(_topology, _config.DashboardRefreshMs);
        _gameDetector = new GameDetector(_config.ManualGames);
        _affinityManager = new AffinityManager(_topology, _config.ProtectedProcesses, mode);
        _processWatcher = new ProcessWatcher(
            _gameDetector, _config.PollingIntervalMs, _config.AutoDetection.RequireForeground);

        // Create ViewModel
        _mainViewModel = new MainViewModel(
            _topology, _perfMon, _processWatcher, _gameDetector, _affinityManager, _config);

        // Create dashboard window
        _dashboardWindow = new DashboardWindow(_config);
        _dashboardWindow.DataContext = _mainViewModel;

        // Create tray icon
        _trayManager = new TrayIconManager(_mainViewModel, _dashboardWindow, _config);

        // Wire engine events
        _perfMon.SnapshotReady += _mainViewModel.OnSnapshotReady;
        _affinityManager.AffinityChanged += _mainViewModel.OnAffinityChanged;
        _processWatcher.GameDetected += _affinityManager.OnGameDetected;
        _processWatcher.GameDetected += _mainViewModel.OnGameDetected;
        _processWatcher.GameExited += _affinityManager.OnGameExited;
        _processWatcher.GameExited += _mainViewModel.OnGameExited;

        // Start engines
        _perfMon.Start();
        _processWatcher.Start();

        Log.Information("Monitoring started. Mode: {Mode}. Polling: {Interval}ms. Manual games: {Count}.",
            mode, _config.PollingIntervalMs, _gameDetector.GameCount);

        // Show dashboard
        if (!_config.Ui.StartMinimized)
            _dashboardWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _processWatcher?.Stop();
        _processWatcher?.Dispose();
        _perfMon?.Stop();
        _perfMon?.Dispose();

        // Save config state
        if (_mainViewModel != null)
        {
            _config.OperationMode = _mainViewModel.CurrentMode.ToString().ToLowerInvariant();
        }

        if (_dashboardWindow != null)
        {
            _config.Ui.WindowPosition = [(int)_dashboardWindow.Left, (int)_dashboardWindow.Top];
            _config.Ui.WindowSize = [(int)_dashboardWindow.Width, (int)_dashboardWindow.Height];
        }

        _config.Save();

        _trayManager?.Dispose();

        Log.Information("X3D Dual CCD Optimizer stopped.");
        AppLogger.Shutdown();

        base.OnExit(e);
    }
}
