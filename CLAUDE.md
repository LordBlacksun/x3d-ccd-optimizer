# CLAUDE.md — X3D Dual CCD Optimizer

## Project Overview

X3D Dual CCD Optimizer is a lightweight open-source Windows application with two operating modes for AMD dual-CCD Ryzen processors:

- **Monitor Mode (default):** Real-time CCD dashboard showing per-core load, frequency, process-to-CCD mapping, and game detection — without touching any process affinity. Works on any dual-CCD Ryzen.
- **Optimize Mode (opt-in):** Everything Monitor does, plus active CPU affinity management — pins games to the V-Cache CCD and migrates background processes to the frequency CCD. Requires confirmed V-Cache detection (X3D processors only).

The app also includes a compact always-on-top overlay for single-monitor gaming, GPU-based automatic game detection, and a 65-game known games database.

## Architecture

The app has three layers:
- **Core Engine** — topology detection, performance monitoring, game detection, GPU monitoring, mode-aware affinity management
- **UI Layer** — WPF dashboard window, compact overlay window, system tray (WinForms NotifyIcon)
- **Configuration** — JSON config with game lists, exclusions, overlay settings, auto-detection parameters

All design decisions and module specs are in `X3D_CCD_OPTIMIZER_BLUEPRINT.md` at the project root. This is the single source of truth. Read it before making architectural changes.

### Operating Modes

The application operates in Monitor or Optimize mode at all times. Mode is stored in config as `operationMode` and persists across restarts.

**Mode-independent modules** (run identically in both modes):
- `CcdMapper` — topology detection, `HasVCache` output
- `PerformanceMonitor` — per-core load/frequency collection via PDH
- `ProcessWatcher` — process polling, game detection events, GPU heuristic integration
- `GameDetector` — three-tier game identification (manual → known DB → GPU heuristic)
- `GpuMonitor` — per-process GPU 3D engine utilization via WMI

**Mode-aware module:**
- `AffinityManager` — checks `operationMode` before every `SetProcessAffinityMask` call
  - **Monitor mode:** emits `WouldEngage`, `WouldMigrate`, `WouldRestore` events. Never calls Win32 affinity APIs.
  - **Optimize mode:** emits `Engaged`, `Migrated`, `Restored` events. Calls `SetProcessAffinityMask`.

The event stream structure is identical in both modes — only the `AffinityAction` type and whether Win32 APIs are called differ.

**Mode switching mid-game:**
- Monitor → Optimize: immediately engages (pins game, migrates background)
- Optimize → Monitor: immediately restores all affinities to original values

**Optimize toggle gating:** Disabled when `CpuTopology.HasVCache == false`. Config fallback to Monitor with warning.

### Game Detection Pipeline

Three-tier priority:
1. **Manual list** (config `manualGames`) — highest priority, instant match
2. **Known games database** (`Data/known_games.json`, 65 entries) — case-insensitive exe match
3. **GPU heuristic** (via `GpuMonitor`) — lowest priority, requires foreground + GPU > threshold for 5s

Detection source shown in activity log: `[manual]`, `[database]`, `[auto-detected, GPU: XX%]`

## Tech Stack

- **Language:** C# 12 / .NET 8
- **Target:** `net8.0-windows` (Windows-specific APIs required)
- **UI:** WPF (dashboard + overlay) + WinForms (NotifyIcon for tray)
- **Win32 API:** P/Invoke via `DllImport` in `Native/` folder
- **Performance counters:** PDH (Performance Data Helper) via P/Invoke
- **GPU monitoring:** WMI `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine`
- **WMI:** `System.Management` for CPU topology fallback, GPU detection, core counts
- **Config:** `System.Text.Json`, stored at `%APPDATA%\X3DCCDOptimizer\config.json`
- **Logging:** Serilog with console + file sinks
- **Distribution:** Self-contained single-file publish (`dotnet publish -r win-x64 --self-contained`)

## Project Structure

```
x3d-ccd-optimizer/
├── src/X3DCcdOptimizer/
│   ├── App.xaml + App.xaml.cs         # WPF entry point, engine wiring, hotkey registration
│   ├── app.manifest                   # PerMonitorV2 DPI awareness
│   ├── Core/                          # Engine modules
│   │   ├── CcdMapper.cs              # CCD topology detection (HasVCache, physical core count)
│   │   ├── PerformanceMonitor.cs     # Per-core load/freq via PDH (thread-safe disposal)
│   │   ├── ProcessWatcher.cs         # Process polling + game detection + GPU debounce
│   │   ├── GameDetector.cs           # Three-tier: manual → known DB → GPU heuristic
│   │   ├── GpuMonitor.cs            # Per-process GPU 3D utilization via WMI
│   │   └── AffinityManager.cs       # Mode-aware affinity management (lock-based thread safety)
│   ├── ViewModels/                    # MVVM ViewModels
│   │   ├── ViewModelBase.cs          # INotifyPropertyChanged base
│   │   ├── RelayCommand.cs           # ICommand implementation
│   │   ├── MainViewModel.cs          # Orchestrator — mode toggle, status, session timer
│   │   ├── CcdPanelViewModel.cs      # Per-CCD panel with core tiles
│   │   ├── CoreTileViewModel.cs      # Per-core load/frequency/color
│   │   ├── ActivityLogViewModel.cs   # Ring buffer of log entries (max 200)
│   │   ├── LogEntryViewModel.cs      # Single log entry with color/style
│   │   ├── ProcessRouterViewModel.cs # Process-to-CCD assignment table
│   │   ├── ProcessEntryViewModel.cs  # Single process entry
│   │   └── OverlayViewModel.cs       # Overlay: CCD averages, auto-hide, pixel shift
│   ├── Views/                         # WPF XAML views
│   │   ├── DashboardWindow.xaml/.cs  # Main dashboard (5-row grid, close-to-tray)
│   │   ├── CcdPanel.xaml/.cs         # CCD UserControl with core grid
│   │   ├── CoreTile.xaml/.cs         # Core tile with load bar
│   │   └── OverlayWindow.xaml/.cs    # Compact overlay (OLED-safe, draggable, auto-hide)
│   ├── Themes/                        # WPF resource dictionaries
│   │   ├── DarkTheme.xaml            # Colors, brushes, gradients
│   │   ├── Typography.xaml           # Font families, text styles
│   │   └── Controls.xaml             # Card, toggle, tile, button styles
│   ├── Converters/                    # WPF value converters
│   │   ├── LoadColorConverter.cs     # Load% → brush
│   │   ├── LoadBarWidthConverter.cs  # Load% + parent width → bar width
│   │   └── BoolToFontStyleConverter.cs
│   ├── Tray/                          # System tray
│   │   ├── TrayIconManager.cs        # WinForms NotifyIcon lifecycle + context menu
│   │   └── IconGenerator.cs          # Programmatic colored circle icons
│   ├── Config/AppConfig.cs           # JSON config model (version 3, overlay, auto-detection)
│   ├── Logging/AppLogger.cs          # Serilog setup (console + rolling file)
│   ├── Native/                        # P/Invoke declarations
│   │   ├── Kernel32.cs               # Affinity, topology APIs
│   │   ├── User32.cs                 # Foreground window, RegisterHotKey
│   │   ├── Pdh.cs                    # Performance counter APIs
│   │   └── Structs.cs                # Native struct definitions
│   ├── Models/                        # Shared data types
│   │   ├── CpuTopology.cs            # HasVCache, TotalPhysicalCores, TotalLogicalCores
│   │   ├── CoreSnapshot.cs
│   │   ├── AffinityEvent.cs          # 8-value AffinityAction enum + event record
│   │   └── OperationMode.cs          # Monitor/Optimize enum
│   └── Data/known_games.json         # 65 game executables
├── tests/X3DCcdOptimizer.Tests/
├── X3DCcdOptimizer.sln
├── X3D_CCD_OPTIMIZER_BLUEPRINT.md    # Full project spec — READ THIS
└── README.md
```

## Build Phases

- **Phase 1 (COMPLETE):** Core engine with console output. Topology detection, affinity management, per-core monitoring, manual game list. Fixed CACHE_RELATIONSHIP struct padding (18-byte Reserved field).
- **Phase 2 (COMPLETE):** WPF dashboard with Monitor/Optimize toggle, dark theme, per-core heatmap, process router, activity log, system tray with mode-aware icons.
- **Phase 2.5 (COMPLETE):** OLED-safe compact overlay, full code audit (2 critical + 3 high + 7 medium issues fixed), GPU heuristic auto-detection with 65-game database and debounce.
- **Phase 3 (NEXT):** Settings window, start-with-Windows, per-game profiles.
- **Phase 4:** CI/CD, trimmed single-file build, installer, release.

## Coding Conventions

- **Nullable reference types** are enabled. Respect them.
- **All P/Invoke calls** must have `SetLastError = true` and proper error checking via `Marshal.GetLastWin32Error()`.
- **All process operations** must be wrapped in try/catch. Never crash on a single process failure. Access denied is expected for system processes — log and skip.
- **Events over callbacks.** Modules communicate via C# events (e.g., `GameDetected`, `AffinityChanged`, `SnapshotReady`).
- **IDisposable** for anything holding unmanaged resources (PDH query handles, timers, icons).
- **Log every significant action** at INFO level, every failure at WARNING or ERROR.
- **No unnecessary dependencies.** Prefer built-in .NET APIs and P/Invoke over third-party NuGet packages.
- **Case-insensitive** process name matching everywhere.
- **AffinityManager must check `operationMode` before every `SetProcessAffinityMask` call.** In Monitor mode, emit `Would*` events instead of calling Win32 APIs. Never skip this check.
- **CACHE_RELATIONSHIP struct** must have `[MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)] byte[] Reserved` — not a 2-byte ushort. The 18-byte reserved field places `GroupMask` at the correct offset 32.
- **Overlay cannot render over exclusive fullscreen.** This is a Windows limitation, not a bug. Document it, don't try to work around it.
- **Thread safety:** `AffinityManager` uses `lock(_syncLock)` for all state mutations. `PerformanceMonitor` uses `lock(_disposeLock)` to prevent timer/Dispose races. All UI updates from background threads use `Dispatcher.BeginInvoke`.
- **Config load/save** wrapped in try/catch — corrupted JSON falls back to defaults, write failures are logged but don't crash.

## Key Technical Details

### CCD Topology Detection
Uses `GetLogicalProcessorInformationEx` with `RelationshipCache` to enumerate L3 caches per CCD. V-Cache CCD has 96MB L3 vs 32MB on the standard CCD. Detection is by cache size, NOT by CCD number. WMI fallback via `Win32_CacheMemory`. Manual override available in config. Outputs `HasVCache` bool and `TotalPhysicalCores` (from WMI `Win32_Processor.NumberOfCores`).

### Affinity Management (Mode-Aware)

The AffinityManager checks `operationMode` before every affinity operation:

- **Monitor mode:** Emits `WouldEngage`, `WouldMigrate`, `WouldRestore` events. No Win32 APIs invoked.
- **Optimize mode:** Calls `SetProcessAffinityMask`. Stores original masks for restoration.

**AffinityAction enum (8 values):**
```csharp
public enum AffinityAction
{
    Engaged, Migrated, Restored, Skipped, Error,
    WouldEngage, WouldMigrate, WouldRestore
}
```

### Performance Monitoring
PDH counters for per-core load and frequency. Timer-driven at 1Hz. Thread-safe disposal with `lock(_disposeLock)`.

### Game Detection Priority
1. Manual list (config) — highest priority, always wins
2. Known games database (known_games.json, 65 entries) — community-maintained
3. GPU heuristic (WMI per-process 3D utilization) — debounce: 5s to detect, 10s to exit

### GPU Monitoring
WMI `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine` queries per-process 3D engine utilization. Falls back gracefully if counters unavailable. Only queries the foreground process to minimize overhead.

### Overlay (OLED-Safe)
280x160 transparent always-on-top window. Auto-hides after 10s idle (configurable). Pixel shift every 3 minutes. Hotkey: Ctrl+Shift+O. Position persisted in config.

## Commands

```bash
# Build
dotnet build

# Run
dotnet run --project src/X3DCcdOptimizer

# Run tests
dotnet test

# Publish self-contained single file
dotnet publish src/X3DCcdOptimizer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Important Notes

- **Monitor mode is the default.** It works on any dual-CCD AMD Ryzen processor. Optimize mode requires V-Cache confirmation and explicit user opt-in.
- **Overlay requires borderless windowed or windowed mode.** Exclusive fullscreen takes over the display adapter — no standard Windows overlay can render on top.
- This is a **Windows-only** tool. Linux has a proper kernel-level solution (`amd_x3d_vcache` driver).
- This is **not a kernel driver**. It works at the userspace process affinity level only.
- It **supplements** AMD's chipset drivers, not replaces them.
- The dashboard is the centrepiece. The overlay is for single-monitor gaming.
- **License is GPL v2.** All contributions must be compatible.
