# CLAUDE.md -- X3D Dual CCD Optimizer

## Project Overview

X3D Dual CCD Optimizer is a lightweight open-source Windows application with two operating modes for AMD dual-CCD Ryzen processors:

- **Monitor Mode (default):** Real-time CCD dashboard showing per-core load, frequency, process-to-CCD mapping, and game detection -- without touching any process affinity. Works on any dual-CCD Ryzen.
- **Optimize Mode (opt-in):** Everything Monitor does, plus active CPU affinity management -- pins games to the V-Cache CCD and migrates background processes to the frequency CCD. Requires confirmed V-Cache detection (X3D processors only).

The app also includes a compact always-on-top overlay for single-monitor gaming, GPU-based automatic game detection, Game Library scanning (Steam/Epic/GOG) with opt-in box art downloads, ETW-based near-instant process detection, a Settings window, start-with-Windows support, and an About dialog.

**Dual-CCD only.** The application requires a dual-CCD AMD Ryzen processor. Single-CCD and non-AMD processors receive a friendly exit dialog explaining the requirement. There is no degraded single-CCD mode.

## Architecture

The app has three layers:
- **Core Engine** -- topology detection, performance monitoring, game detection, GPU monitoring, game library scanning, artwork download, mode-aware affinity management, V-Cache driver interface
- **UI Layer** -- WPF dashboard window (with tabbed lower section: Activity Log, Process Router, Game Library), compact overlay window, settings window, about dialog, system tray (WinForms NotifyIcon)
- **Configuration + Storage** -- JSON config with game lists, exclusions, overlay settings, auto-detection parameters; LiteDB database for scanned games

All design decisions and module specs are in `X3D_CCD_OPTIMIZER_BLUEPRINT.md` at the project root. This is the single source of truth. Read it before making architectural changes.

### Processor Tier Model

The `ProcessorTier` enum has 4 values for classification:

```csharp
public enum ProcessorTier
{
    DualCcdX3D,         // Supported -- full Monitor + Optimize
    SingleCcdX3D,       // NOT supported -- friendly exit dialog
    SingleCcdStandard,  // NOT supported -- friendly exit dialog
    DualCcdStandard     // Supported -- Monitor only (no V-Cache, Optimize toggle disabled)
}
```

The `IsSupported()` extension method gates application behavior. Only `DualCcdX3D` and `DualCcdStandard` return true. Non-AMD processors are detected and shown a separate exit dialog. The four enum values are retained for compatibility and future use.

### Operating Modes

The application operates in Monitor or Optimize mode at all times. Mode is stored in config as `operationMode` and persists across restarts.

**Mode-independent modules** (run identically in both modes):
- `CcdMapper` -- topology detection, `HasVCache` output, `ProcessorTier` classification
- `PerformanceMonitor` -- per-core load/frequency collection via PDH
- `ProcessWatcher` -- ETW-first process detection with polling fallback, game detection events, GPU heuristic integration
- `ProcessEventWatcher` -- ETW kernel process start/stop subscription for near-instant detection
- `GameDetector` -- three-tier game identification (manual -> library scan -> GPU heuristic)
- `GpuMonitor` -- per-process GPU 3D engine utilization via WMI
- `GameLibraryScanner` -- Steam/Epic/GOG library scanning
- `GameDatabase` -- LiteDB persistent storage for scanned games
- `ArtworkDownloader` -- Steam CDN box art downloads (opt-in)

**Mode-aware modules:**
- `AffinityManager` -- checks `operationMode` before every `SetProcessAffinityMask` call
  - **Monitor mode:** emits `WouldEngage`, `WouldMigrate`, `WouldRestore` events. Never calls Win32 affinity APIs.
  - **Optimize mode:** emits `Engaged`, `Migrated`, `Restored` events. Calls `SetProcessAffinityMask`.
- `VCacheDriverManager` -- reads/writes AMD amd3dvcache driver registry preferences for CCD scheduling

The event stream structure is identical in both modes -- only the `AffinityAction` type and whether Win32 APIs are called differ.

**Mode switching mid-game:**
- Monitor -> Optimize: immediately engages (pins game, migrates background)
- Optimize -> Monitor: immediately restores all affinities to original values

**Optimize toggle gating:** Disabled when `CpuTopology.HasVCache == false`. Config fallback to Monitor with warning.

### Game Detection Pipeline

Three-tier priority:
1. **Manual list** (config `manualGames`) -- highest priority, instant match
2. **Library scan** (Steam/Epic/GOG via `GameLibraryScanner` + `GameDatabase`) -- scanned games stored in LiteDB
3. **GPU heuristic** (via `GpuMonitor`) -- lowest priority, requires foreground + GPU > threshold for 5s

Detection source shown in activity log: `[manual]`, `[library]`, `[auto-detected, GPU: XX%]`

### Polling Intervals

- `ProcessWatcher`: ETW-first (near-instant), polling fallback at 15s; without ETW: 4s idle, 2s active (configurable; default `pollingIntervalMs=2000`, idle multiplier = 2x)
- `PerformanceMonitor`: 1s (configurable via `dashboardRefreshMs`)
- `AffinityManager` re-migration: 5s
- UI debouncing: skip dispatch if no core changed by >1% load or >50 MHz

## Tech Stack

- **Language:** C# 12 / .NET 8
- **Target:** `net8.0-windows` (Windows-specific APIs required)
- **UI:** WPF (dashboard + overlay + settings + about) + WinForms (NotifyIcon for tray)
- **Win32 API:** P/Invoke via `DllImport` in `Native/` folder
- **Performance counters:** PDH (Performance Data Helper) via P/Invoke
- **GPU monitoring:** WMI `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine`
- **WMI:** `System.Management` for CPU topology fallback, GPU detection, core counts
- **Config:** `System.Text.Json`, stored at `%APPDATA%\X3DCCDOptimizer\config.json`
- **Game database:** LiteDB 5.0.21, stored at `%APPDATA%\X3DCCDOptimizer\user_games.db`
- **GOG detection:** Microsoft.Data.Sqlite 8.0.0 for reading GOG Galaxy's SQLite database
- **Logging:** Serilog with console + file sinks
- **Distribution:** Self-contained single-file publish (`dotnet publish -r win-x64 --self-contained`)

### NuGet Dependencies

- `LiteDB` 5.0.21 -- game library persistent storage
- `Microsoft.Data.Sqlite` 8.0.0 -- GOG Galaxy database reading
- `Microsoft.Diagnostics.Tracing.TraceEvent` 3.1.30 -- ETW kernel process events
- `Serilog` 4.2.0 -- structured logging
- `Serilog.Sinks.Console` 6.0.0 -- console log output
- `Serilog.Sinks.File` 6.0.0 -- rolling file log output
- `System.Management` 8.0.0 -- WMI queries

## Project Structure

```
x3d-ccd-optimizer/
├── src/X3DCcdOptimizer/
│   ├── App.xaml + App.xaml.cs         # WPF entry point, engine wiring, hotkey registration
│   ├── app.manifest                   # PerMonitorV2 DPI awareness
│   ├── X3DCcdOptimizer.csproj         # .NET 8, WPF+WinForms, NuGet refs
│   ├── Core/                          # Engine modules
│   │   ├── CcdMapper.cs              # CCD topology detection (HasVCache, ProcessorTier)
│   │   ├── PerformanceMonitor.cs     # Per-core load/freq via PDH (thread-safe disposal)
│   │   ├── ProcessWatcher.cs         # Process polling + game detection + GPU debounce
│   │   ├── GameDetector.cs           # Three-tier: manual → library scan → GPU heuristic
│   │   ├── GpuMonitor.cs            # Per-process GPU 3D utilization via WMI
│   │   ├── AffinityManager.cs       # Mode-aware affinity management (lock-based thread safety)
│   │   ├── VCacheDriverManager.cs   # AMD amd3dvcache driver registry interface
│   │   ├── GameLibraryScanner.cs    # Steam/Epic/GOG library scanning
│   │   ├── GameDatabase.cs          # LiteDB storage wrapper for scanned games
│   │   ├── ArtworkDownloader.cs     # Steam CDN box art download (opt-in)
│   │   ├── StartupManager.cs        # Start-with-Windows registry management
│   │   ├── ProcessEventWatcher.cs   # ETW kernel process start/stop events
│   │   └── RecoveryManager.cs       # Crash recovery for affinity state
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
│   │   ├── OverlayViewModel.cs       # Overlay: CCD averages, auto-hide, pixel shift
│   │   ├── SettingsViewModel.cs      # Settings window ViewModel
│   │   └── GameLibraryViewModel.cs  # Game Library tab ViewModel
│   ├── Views/                         # WPF XAML views
│   │   ├── DashboardWindow.xaml/.cs  # Main dashboard (tabbed lower section, close-to-tray)
│   │   ├── CcdPanel.xaml/.cs         # CCD UserControl with core grid
│   │   ├── CoreTile.xaml/.cs         # Core tile with load bar
│   │   ├── OverlayWindow.xaml/.cs    # Compact overlay (OLED-safe, draggable, auto-hide)
│   │   ├── SettingsWindow.xaml/.cs   # Settings dialog
│   │   ├── ProcessPickerWindow.xaml/.cs # Process picker for manual game list
│   │   └── AboutWindow.xaml/.cs      # About dialog
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
│   ├── Config/AppConfig.cs           # JSON config model (overlay, auto-detection, polling)
│   ├── Logging/AppLogger.cs          # Serilog setup (console + rolling file)
│   ├── Native/                        # P/Invoke declarations
│   │   ├── Kernel32.cs               # Affinity, topology APIs
│   │   ├── User32.cs                 # Foreground window, RegisterHotKey
│   │   ├── Pdh.cs                    # Performance counter APIs
│   │   └── Structs.cs                # Native struct definitions
│   ├── Models/                        # Shared data types
│   │   ├── CpuTopology.cs            # HasVCache, TotalPhysicalCores, TotalLogicalCores
│   │   ├── ProcessorTier.cs          # 4-value enum + IsSupported() extension
│   │   ├── CoreSnapshot.cs
│   │   ├── AffinityEvent.cs          # 13-value AffinityAction enum + event record
│   │   ├── OperationMode.cs          # Monitor/Optimize enum
│   │   ├── OptimizeStrategy.cs       # AffinityPinning/DriverPreference enum
│   │   ├── RecoveryState.cs          # Crash recovery serialization model
│   │   ├── ProtectedProcesses.cs     # System processes excluded from affinity changes
│   │   ├── ScannedGame.cs            # LiteDB model for scanned games
│   │   └── GameProfile.cs            # Per-game strategy override
│   ├── Data/BackgroundAppSuggestions.cs # Curated background app display names
│   └── Resources/app.ico             # Application icon
├── tests/X3DCcdOptimizer.Tests/
├── X3DCcdOptimizer.sln
├── X3D_CCD_OPTIMIZER_BLUEPRINT.md    # Full project spec — READ THIS
└── README.md
```

## Build Phases

- **Phase 1 (COMPLETE):** Core engine with console output. Topology detection, affinity management, per-core monitoring, manual game list. Fixed CACHE_RELATIONSHIP struct padding (18-byte Reserved field).
- **Phase 2 (COMPLETE):** WPF dashboard with Monitor/Optimize toggle, dark theme, per-core heatmap, process router, activity log, system tray with mode-aware icons.
- **Phase 2.5 (COMPLETE):** OLED-safe compact overlay, full code audit (2 critical + 3 high + 7 medium issues fixed), GPU heuristic auto-detection with 65-game database and debounce.
- **Phase 3 (COMPLETE):** Settings window, start-with-Windows, library scanning (Steam/Epic/GOG via ACF/VDF/JSON/SQLite+registry), LiteDB game database, Game Library tab, About dialog, opt-in box art downloads from Steam CDN, dual-CCD only architecture with friendly exit dialogs, V-Cache driver registry interface, crash recovery manager, performance audit.
- **Phase 4 (NEXT):** CI/CD, trimmed single-file build, installer, release, per-game profiles.

## Coding Conventions

- **Nullable reference types** are enabled. Respect them.
- **All P/Invoke calls** must have `SetLastError = true` and proper error checking via `Marshal.GetLastWin32Error()`.
- **All process operations** must be wrapped in try/catch. Never crash on a single process failure. Access denied is expected for system processes -- log and skip.
- **Events over callbacks.** Modules communicate via C# events (e.g., `GameDetected`, `AffinityChanged`, `SnapshotReady`).
- **IDisposable** for anything holding unmanaged resources (PDH query handles, timers, icons, LiteDB).
- **Log every significant action** at INFO level, every failure at WARNING or ERROR.
- **Minimal dependencies.** Prefer built-in .NET APIs and P/Invoke over third-party NuGet packages. LiteDB and Microsoft.Data.Sqlite are the only non-logging dependencies.
- **Case-insensitive** process name matching everywhere.
- **AffinityManager must check `operationMode` before every `SetProcessAffinityMask` call.** In Monitor mode, emit `Would*` events instead of calling Win32 APIs. Never skip this check.
- **CACHE_RELATIONSHIP struct** must have `[MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)] byte[] Reserved` -- not a 2-byte ushort. The 18-byte reserved field places `GroupMask` at the correct offset 32.
- **Overlay cannot render over exclusive fullscreen.** This is a Windows limitation, not a bug. Document it, don't try to work around it.
- **Thread safety:** `AffinityManager` uses `lock(_syncLock)` for all state mutations. `PerformanceMonitor` uses `lock(_disposeLock)` to prevent timer/Dispose races. All UI updates from background threads use `Dispatcher.BeginInvoke`.
- **Config load/save** wrapped in try/catch -- corrupted JSON falls back to defaults, write failures are logged but don't crash.
- **Protected processes** (`ProtectedProcesses.Names`) are never modified by affinity operations. The list includes system-critical processes and the application itself.

## Key Technical Details

### CCD Topology Detection
Uses `GetLogicalProcessorInformationEx` with `RelationshipCache` to enumerate L3 caches per CCD. V-Cache CCD has 96MB L3 vs 32MB on the standard CCD. Detection is by cache size, NOT by CCD number. WMI fallback via `Win32_CacheMemory`. Manual override available in config. Outputs `HasVCache` bool, `ProcessorTier` classification, and `TotalPhysicalCores` (from WMI `Win32_Processor.NumberOfCores`).

### Unsupported Processor Handling
Non-AMD and single-CCD processors are detected at startup. The application shows a friendly MessageBox dialog explaining the requirement and exits cleanly. No degraded mode is offered -- the CCD scheduling problem this tool solves does not exist on single-CCD processors.

### Affinity Management (Mode-Aware)

The AffinityManager checks `operationMode` before every affinity operation:

- **Monitor mode:** Emits `WouldEngage`, `WouldMigrate`, `WouldRestore` events. No Win32 APIs invoked.
- **Optimize mode:** Calls `SetProcessAffinityMask`. Stores original masks for restoration.

**AffinityAction enum (13 values):**
```csharp
public enum AffinityAction
{
    Engaged, Migrated, Restored, Skipped, Error,
    WouldEngage, WouldMigrate, WouldRestore,
    DriverSet, DriverRestored,
    WouldSetDriver, WouldRestoreDriver,
    DetectionSkipped
}
```

The `Driver*` actions correspond to `VCacheDriverManager` operations on the AMD amd3dvcache driver registry. `DetectionSkipped` is emitted when game detection is skipped (e.g., process in exclusion list).

### V-Cache Driver Interface
Reads and writes the AMD amd3dvcache driver registry at `HKLM\SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences`. `DefaultType` REG_DWORD: 0 = PREFER_FREQ (driver default), 1 = PREFER_CACHE. Driver availability is lazily detected and cached.

### Optimize Strategy
Two strategies available via `OptimizeStrategy` enum:
- `AffinityPinning` -- sets process affinity masks via Win32 API
- `DriverPreference` -- toggles V-Cache driver registry preference

### Performance Monitoring
PDH counters for per-core load and frequency. Timer-driven at 1s default (configurable via `dashboardRefreshMs`). Thread-safe disposal with `lock(_disposeLock)`. UI dispatch is debounced: skipped if no core changed by >1% load or >50 MHz.

### Game Detection Priority
1. Manual list (config) -- highest priority, always wins
2. Library scan (GameDatabase/LiteDB) -- Steam/Epic/GOG scanned games
3. GPU heuristic (WMI per-process 3D utilization) -- debounce: 5s to detect, 10s to exit

### Game Library Scanning
`GameLibraryScanner` discovers installed games from three launchers:
- **Steam:** Parses ACF manifest files and VDF library folders config to find install directories
- **Epic Games:** Reads JSON manifest files from the Epic launcher's manifests directory
- **GOG Galaxy:** Queries GOG's SQLite database and Windows registry for installed games

Scanned results are stored as `ScannedGame` records in LiteDB. The scanner uses `SelectBestExe` to pick one executable per game directory, scoring by name match, root directory preference, and file size. Non-game executables (installers, crash reporters, redistributables, anti-cheat launchers) are filtered via prefix/suffix/exact-match skip lists. Directory enumeration uses `IgnoreInaccessible` to gracefully skip protected anti-cheat folders (EasyAntiCheat, BattlEye). Scanning runs on a background thread and is safe to invoke repeatedly -- results replace previous entries per source.

### Game Database (LiteDB)
Persistent storage at `%APPDATA%\X3DCCDOptimizer\user_games.db`. Stores `ScannedGame` records with fields: ProcessName, DisplayName, Source (steam/epic/gog), InstallPath, SteamAppId, ArtworkPath, FirstSeen, LastSeen. Indexed on ProcessName and Source. Thread-safe for concurrent reads via LiteDB's shared connection mode.

### Artwork Downloads
`ArtworkDownloader` fetches game box art from Steam's public CDN using SteamAppId. Strictly opt-in -- user must enable artwork downloads in Settings. Images are cached at `%APPDATA%\X3DCCDOptimizer\artwork\`. Requests are throttled to be polite to the CDN. Only Steam games with known AppIds can have artwork.

### GPU Monitoring
WMI `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine` queries per-process 3D engine utilization. Falls back gracefully if counters unavailable. Only queries the foreground process to minimize overhead.

### Overlay (OLED-Safe)
280x160 transparent always-on-top window. Auto-hides after 10s idle (configurable). Pixel shift every 3 minutes. Hotkey: Ctrl+Shift+O. Position persisted in config.

### Crash Recovery
`RecoveryManager` persists affinity state to a JSON file. On startup, if a previous session was interrupted while Optimize mode was engaged, the application can restore original process affinities. Recovery state includes the engaged game, modified process PIDs, and their original affinity masks.

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
- **Dual-CCD only.** Single-CCD and non-AMD processors get a friendly exit dialog. There is no degraded mode.
- **Overlay requires borderless windowed or windowed mode.** Exclusive fullscreen takes over the display adapter -- no standard Windows overlay can render on top.
- This is a **Windows-only** tool. Linux has a proper kernel-level solution (`amd_x3d_vcache` driver).
- This is **not a kernel driver**. It works at the userspace process affinity level only.
- It **supplements** AMD's chipset drivers, not replaces them.
- The dashboard is the centrepiece. The overlay is for single-monitor gaming.
- **License is GPL v2.** All contributions must be compatible.
