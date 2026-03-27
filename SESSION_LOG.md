# Session Log

Development session history for X3D Dual CCD Optimizer.

---

## Session 6 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Phase 2.5 — Overlay, code audit, GPU auto-detection

### What Was Done

1. **Compact always-on-top overlay with OLED protection** (`53292d0`)
   - 280×160 transparent overlay: mode indicator, game name, CCD0/CCD1 load bars, last action
   - Auto-hide: fades to 0% opacity after 10s idle, fades back on event/mouse-enter
   - Pixel shift: random 1-5px nudge every 3 minutes, clamped to screen bounds
   - Hotkey: Ctrl+Shift+O via RegisterHotKey P/Invoke (fails gracefully)
   - Toggle from dashboard footer, tray menu, overlay right-click context menu
   - Position saved to config across restarts
   - Removed temporary Validate button from dashboard
   - Config: `overlay.enabled`, `autoHideSeconds`, `pixelShiftMinutes`, `opacity`, `position`

2. **Full codebase security and safety audit** (`e49c341`)
   - **Critical fixes:** Config Load handles corrupted JSON (fallback to defaults), Config Save wrapped in try/catch, global DispatcherUnhandledException + AppDomain.UnhandledException handlers
   - **High fixes:** PerformanceMonitor lock(_disposeLock) prevents timer/Dispose race, OverlayWindow guards against duplicate event subscriptions on re-show
   - **Medium fixes:** Multi-monitor position restore uses VirtualScreenLeft/Top, Process handle leak fixed with `using`, Win32Exception caught in game exit check, GetProcessAffinityMask failure logged, Icon HICON handle freed with DestroyIcon after Clone, App.OnExit only saves position when Normal state
   - **Low fixes:** volatile on _disposed/_firstCollectionDone

3. **GPU heuristic auto-detection** (`bbbcad6`)
   - Three-tier detection: manual list → known games DB (65 entries) → GPU heuristic
   - GpuMonitor.cs: WMI Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine queries per-process 3D utilization
   - Debounce: 5s foreground+GPU above threshold to detect, 10s below threshold to exit
   - Falls back gracefully if GPU counters unavailable
   - known_games.json: 65 game executables (AAA, indie, competitive, sim)
   - Exclusion list expanded to 18 entries (browsers, creative apps, editors)
   - Detection source in log: `[manual]`, `[database]`, `[auto-detected, GPU: XX%]`
   - Config: `detectionDelaySeconds`, `exitDelaySeconds` in autoDetection

4. **Housekeeping** (`44d2834`)
   - Removed build prompt files from repo, added `*_PROMPT.md` and `publish/` to .gitignore

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| `44d2834` | develop | chore: remove build prompts from repo, add to gitignore |
| `53292d0` | develop | feat: add compact always-on-top mini overlay with OLED burn-in protection |
| `e49c341` | develop | audit: fix all issues found in full codebase security and safety audit |
| `bbbcad6` | develop | feat: add GPU heuristic auto-detection with debounce and expanded game database |
| `cfd9814` | master | Phase 2.5: Overlay, audit fixes, GPU auto-detection (merge) |

### Files Created (5 new)

```
src/X3DCcdOptimizer/Core/GpuMonitor.cs
src/X3DCcdOptimizer/Data/known_games.json
src/X3DCcdOptimizer/ViewModels/OverlayViewModel.cs
src/X3DCcdOptimizer/Views/OverlayWindow.xaml + .cs
```

### Files Modified (14)

```
.gitignore, App.xaml.cs, AppConfig.cs, AffinityManager.cs, GameDetector.cs,
ProcessWatcher.cs, PerformanceMonitor.cs, User32.cs, IconGenerator.cs,
TrayIconManager.cs, MainViewModel.cs, ViewModelBase.cs, DashboardWindow.xaml,
DashboardWindow.xaml.cs, X3DCcdOptimizer.csproj
```

---

## Session 5 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Dashboard UI visual polish

### What Was Done

1. **Core tiles — visual centrepiece:**
   - Load percentage now in Cascadia Mono at 26px (tabular-lining monospace)
   - Core index and frequency labels in mono 10px for consistency
   - Added thin green load progress bar at bottom of each tile (2px, proportional to load %)
   - Created `LoadBarWidthConverter` (IMultiValueConverter) for bar width binding

2. **CCD panels — identity:**
   - Green accent left edge stripe on V-Cache panel, blue on Frequency panel (3px, 50% opacity)
   - Increased internal padding and spacing between header/role/grid sections

3. **Status bar — depth and life:**
   - Gradient overlay (`StatusBarOverlay` brush) — slightly lighter at centre for depth
   - Pulsing status dot animation (opacity 1.0→0.35, 1.2s sine cycle, forever)
   - Session timer in mono font for tabular alignment
   - Increased border radius to 10px, padding to 14,10

4. **Pill toggle — tactile feedback:**
   - Thumb gets `DropShadowEffect` (BlurRadius=6, color matches accent) — physically raised look
   - Shadow color animates blue↔green alongside the thumb color
   - Scale bounce on click: press=0.95, release=1.04→1.0 (CubicEase out)
   - Track background darkened for better thumb contrast

5. **Activity log — readability:**
   - Alternating row shading via `AlternationCount="2"` + `RowAltBrush` (#1C1C20)
   - Fixed-width columns (68px timestamp, 185px action, * detail) — perfect alignment
   - Row items get rounded background + padding for visual grouping

6. **Overall atmosphere:**
   - Footer gets a fading gradient separator line (transparent→subtle→transparent)
   - Section spacing increased from 4px to 8px throughout
   - Card padding increased from 12 to 14px
   - `SectionHeader` font now uses Segoe UI Variable Display (optical sizing at 18px+)
   - Core tile load colors slightly adjusted for richer tints
   - Added gradient brushes: `VCacheEdgeBrush`, `FrequencyEdgeBrush`, `FooterSeparatorBrush`

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| (this commit) | develop | ui: polish dashboard visuals |

### Files Modified (6) + Created (1)

```
Themes/DarkTheme.xaml — gradient brushes, row alt color, glow colors, footer separator
Themes/Typography.xaml — BigNumber now Cascadia Mono 26px, SectionHeader uses Variable Display
Themes/Controls.xaml — toggle shadow + bounce, increased tile/card dimensions
Views/CoreTile.xaml — mono font labels, load progress bar with MultiBinding
Views/CcdPanel.xaml — accent left edge stripe, increased spacing
Views/DashboardWindow.xaml — pulsing dot, gradient overlay, fixed-width log columns, alt rows, footer separator
Converters/LoadBarWidthConverter.cs — NEW, maps load% + parent width to bar width
```

---

## Session 4 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Phase 2 build — WPF dashboard + Monitor/Optimize dual-mode implementation

### What Was Done

1. **Fixed CACHE_RELATIONSHIP struct** — `Reserved` field was `ushort` (2 bytes) instead of `byte[18]` (18 bytes), causing `GroupMask` to be read from wrong offset. Topology detection now works correctly on the 7950X3D.

2. **Engine changes for dual-mode:**
   - `AffinityManager` rewritten as mode-aware with `lock(_syncLock)` for thread safety
   - Monitor mode emits `WouldEngage`/`WouldMigrate`/`WouldRestore` without calling Win32 APIs
   - `SwitchToOptimize()`/`SwitchToMonitor()` for mid-game mode switching
   - `CpuTopology.HasVCache` computed property gates the Optimize toggle
   - `AppConfig.operationMode` field (default: `"monitor"`), config version bumped to 3
   - New `OperationMode` enum (`Monitor`, `Optimize`)

3. **WPF dashboard (24 new files):**
   - MVVM architecture: ViewModels subscribe to engine events, marshal to UI via `Dispatcher.BeginInvoke`
   - Dark theme with color system: BgPrimary #1A1A1E, accents for modes (blue=Monitor, green=Optimize, purple=idle)
   - Two CCD panels with 4×2 core tile grids, load-based background colors (idle/moderate/hot)
   - Animated pill toggle for Monitor/Optimize switching with sliding thumb and color transition
   - Process router table showing affinity assignments with CCD badges
   - Activity log with color-coded entries, italic `[MONITOR]` styling for simulated actions
   - System tray via WinForms `NotifyIcon` (WPF has no built-in tray; Hardcodet package was incompatible with .NET 8)
   - Programmatic icon generation using `System.Drawing` — colored circles for each state
   - Close-to-tray, double-click-to-open, right-click context menu with mode toggle
   - Window position/size persistence via config
   - DPI-aware via PerMonitorV2 app manifest

4. **Project migration:** Console → WPF (`WinExe`), added `UseWPF` + `UseWindowsForms` (for NotifyIcon), removed `Program.cs`, added `App.xaml`/`App.xaml.cs` entry point with `ShutdownMode="OnExplicitShutdown"`.

5. **Build issues resolved:**
   - Hardcodet.NotifyIcon.Wpf incompatible with .NET 8 → switched to WinForms NotifyIcon
   - WPF SDK missing `System.IO` implicit using → added explicit usings
   - WPF + WinForms namespace conflicts (Application, MessageBox, UserControl, FontStyle) → disambiguated with full type names and removed WinForms global using

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| `7d5e679` | develop | fix: correct CACHE_RELATIONSHIP struct padding |
| `988fdde` | master | cherry-pick of 7d5e679 |
| `e43dd71` | master | Merge develop into master |
| `df6cc22` | develop | Phase 2: WPF dashboard + Monitor/Optimize dual-mode system |

### Files Created (24 new)

```
src/X3DCcdOptimizer/App.xaml + App.xaml.cs
src/X3DCcdOptimizer/app.manifest
src/X3DCcdOptimizer/Models/OperationMode.cs
src/X3DCcdOptimizer/Themes/DarkTheme.xaml, Typography.xaml, Controls.xaml
src/X3DCcdOptimizer/ViewModels/ViewModelBase.cs, RelayCommand.cs, MainViewModel.cs
src/X3DCcdOptimizer/ViewModels/CcdPanelViewModel.cs, CoreTileViewModel.cs
src/X3DCcdOptimizer/ViewModels/ActivityLogViewModel.cs, LogEntryViewModel.cs
src/X3DCcdOptimizer/ViewModels/ProcessRouterViewModel.cs, ProcessEntryViewModel.cs
src/X3DCcdOptimizer/Views/DashboardWindow.xaml + .cs
src/X3DCcdOptimizer/Views/CcdPanel.xaml + .cs, CoreTile.xaml + .cs
src/X3DCcdOptimizer/Converters/LoadColorConverter.cs, BoolToFontStyleConverter.cs
src/X3DCcdOptimizer/Tray/TrayIconManager.cs, IconGenerator.cs
```

### Files Modified (5)

```
src/X3DCcdOptimizer/X3DCcdOptimizer.csproj — WPF, WinForms, version 0.2.0
src/X3DCcdOptimizer/Core/AffinityManager.cs — mode-aware, thread-safe
src/X3DCcdOptimizer/Models/AffinityEvent.cs — WouldEngage/WouldMigrate/WouldRestore
src/X3DCcdOptimizer/Models/CpuTopology.cs — HasVCache property
src/X3DCcdOptimizer/Config/AppConfig.cs — operationMode, version 3
```

### Files Deleted (1)

```
src/X3DCcdOptimizer/Program.cs — replaced by App.xaml
```

---

## Session 3 — 2026-03-26

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Add Monitor/Optimize dual-mode system to project documentation

### What Was Done

1. **Blueprint rewrite (v0.3.0)** — Rewrote `X3D_CCD_OPTIMIZER_BLUEPRINT.md` from scratch incorporating the Monitor/Optimize dual-mode system throughout:
   - New Section 3.1 Operating Modes — full Monitor vs Optimize specification
   - Mode-aware AffinityManager with `WouldEngage`/`WouldMigrate`/`WouldRestore` simulated events
   - `HasVCache` bool on CpuTopology to gate the Optimize toggle
   - `operationMode` config field (default: `"monitor"`), config version bumped to 3
   - Dashboard mode toggle, blue dashed vs green solid CCD borders, muted `[MONITOR]` log styling
   - Tray icon states: Blue (Monitor), Purple (Optimize idle), Green (Optimize active), Yellow, Red
   - Updated risk register, compatibility split by mode, parking health check in futures
   - Phase 2 renamed to "Monitor Mode + Dashboard Window"

2. **CLAUDE.md rewrite** — Complete rewrite reflecting dual-mode architecture, mode-aware coding conventions, updated AffinityAction enum, and mode-gating rules.

3. **CONTRIBUTING.md rewrite** — Added AI-assisted development disclosure, highlighted code review as high-value contribution for AI-generated codebases, updated for dual-mode conventions.

4. **SECURITY.md created** — Documented minimal attack surface (no network, no kernel, no credentials, no IPC, standard user only), what the app accesses, vulnerability reporting process, Monitor mode as safety default.

5. **README.md updated** — Added "How This Was Built" section with honest AI-assisted development disclosure.

6. **Cherry-picked docs onto master** — Both doc commits cherry-picked onto master and pushed.

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| `d88259a` | develop | docs: add Monitor/Optimize dual-mode system to blueprint and CLAUDE.md (v0.3.0) |
| `ef1a852` | develop | docs: add CONTRIBUTING.md, SECURITY.md, update README with development disclosure |
| `0c4d0de` | master | cherry-pick of d88259a |
| `681588b` | master | cherry-pick of ef1a852 |

### Design Decisions

- **Monitor mode is the default.** Users observe before enabling control. Builds trust, avoids anticheat risk, widens audience to all dual-CCD Ryzen owners.
- **AffinityManager is the only mode-aware module.** All other engine modules run identically in both modes. Clean, testable mode boundary.
- **Mode switch mid-game is immediate.** No "wait until game exits" behaviour.
- **Optimize toggle is hardware-gated.** `HasVCache == false` means the toggle is greyed out. Config override falls back to Monitor with a warning.

---

## Session 2 — 2026-03-25

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Project scaffolding and community files

### What Was Done

1. Added GPL v2 license
2. Added initial CONTRIBUTING.md guidelines

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| `3555b5f` | develop | Add GPL v2 license |
| `ebbe594` | develop | Add contributing guidelines |

---

## Session 1 — 2026-03-25

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Build Phase 1 of X3D Dual CCD Optimizer from blueprint specs

### What Was Built

Complete Phase 1 foundation — a console-mode .NET 8 application that detects AMD X3D dual-CCD topology, monitors per-core performance, watches for games, and manages CPU affinity.

### Steps Completed

### Step 0: Project Setup
- Created `X3DCcdOptimizer.sln` with two projects
- `src/X3DCcdOptimizer/` — main project (`net8.0-windows`, Exe)
- `tests/X3DCcdOptimizer.Tests/` — test project (xUnit)
- NuGet: Serilog, Serilog.Sinks.Console, Serilog.Sinks.File, System.Management
- Added `.gitignore` for .NET

### Step 1: Native P/Invoke Signatures (`Native/`)
- `Kernel32.cs` — SetProcessAffinityMask, GetProcessAffinityMask, GetLogicalProcessorInformationEx, OpenProcess, CloseHandle + access constants
- `User32.cs` — GetForegroundWindow, GetWindowThreadProcessId
- `Pdh.cs` — PdhOpenQuery, PdhAddEnglishCounter, PdhCollectQueryData, PdhGetFormattedCounterValue, PdhCloseQuery
- `Structs.cs` — GROUP_AFFINITY, CACHE_RELATIONSHIP, SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX, PDH_FMT_COUNTERVALUE

### Step 2: Models (`Models/`)
- `CpuTopology.cs` — VCacheMask/FrequencyMask (IntPtr), core arrays, CPU model, L3 sizes, hex mask formatting
- `CoreSnapshot.cs` — per-core record: index, CCD, load%, frequency, temperature
- `AffinityEvent.cs` — action enum (Engaged/Migrated/Restored/Skipped/Error) + event record

### Step 3: Configuration (`Config/AppConfig.cs`)
- Full JSON config model matching blueprint Section 4.9
- System.Text.Json Load/Save to `%APPDATA%\X3DCCDOptimizer\config.json`
- Auto-creates directory and default config on first run
- Default game list: Elite Dangerous, FFXIV, Stellaris, RE4, Helldivers 2, Star Citizen

### Step 4: Logger (`Logging/AppLogger.cs`)
- Serilog with console + rolling file sinks
- File sink: 10MB max, daily rolling, in `%APPDATA%\X3DCCDOptimizer\logs\`
- Format: `[HH:mm:ss LVL] Message`

### Step 5: CCD Mapper (`Core/CcdMapper.cs`) — Most Critical Module
- Primary detection: `GetLogicalProcessorInformationEx` with RelationCache
- Parses variable-length buffer with manual pointer arithmetic
- Identifies V-Cache CCD by larger L3 (96MB vs 32MB)
- WMI fallback via `Win32_CacheMemory` if P/Invoke fails
- Config override fallback if both fail
- CPU model query via WMI `Win32_Processor`

### Step 6: Performance Monitor (`Core/PerformanceMonitor.cs`)
- PDH counters for per-core load (`% Processor Utility`) and frequency (`Processor Frequency`)
- Fallback to `% Processor Time` if Utility counter unavailable
- Timer-based collection with configurable interval
- Handles PDH's two-collection requirement for delta counters
- IDisposable for proper PDH handle cleanup

### Step 7: Game Detector (`Core/GameDetector.cs`)
- Manual list matching from config (case-insensitive)
- Handles both with/without `.exe` extension
- Tracks `CurrentGame` state

### Step 8: Process Watcher (`Core/ProcessWatcher.cs`)
- Timer-based polling at configurable interval
- Foreground window tracking via GetForegroundWindow + GetWindowThreadProcessId
- GameDetected/GameExited events
- Checks if tracked game has exited before scanning for new ones
- IDisposable

### Step 9: Affinity Manager (`Core/AffinityManager.cs`)
- On game detected: pins game to VCacheMask, migrates background to FrequencyMask
- Stores original masks for restoration
- Protected process list: hardcoded system processes + config list
- On game exit: restores all modified processes
- All SetProcessAffinityMask calls wrapped in try/catch
- Emits AffinityEvent for every action

### Step 10: Program.cs — Console Entry Point
- Wires all modules together in correct order
- Prints topology info, per-core status table, game events, affinity changes
- Ctrl+C graceful shutdown with CancellationTokenSource
- Disposes all resources in finally block

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Files Created

```
.gitignore
X3DCcdOptimizer.sln
src/X3DCcdOptimizer/X3DCcdOptimizer.csproj
src/X3DCcdOptimizer/Program.cs
src/X3DCcdOptimizer/Native/Kernel32.cs
src/X3DCcdOptimizer/Native/User32.cs
src/X3DCcdOptimizer/Native/Pdh.cs
src/X3DCcdOptimizer/Native/Structs.cs
src/X3DCcdOptimizer/Models/CpuTopology.cs
src/X3DCcdOptimizer/Models/CoreSnapshot.cs
src/X3DCcdOptimizer/Models/AffinityEvent.cs
src/X3DCcdOptimizer/Config/AppConfig.cs
src/X3DCcdOptimizer/Logging/AppLogger.cs
src/X3DCcdOptimizer/Core/CcdMapper.cs
src/X3DCcdOptimizer/Core/PerformanceMonitor.cs
src/X3DCcdOptimizer/Core/GameDetector.cs
src/X3DCcdOptimizer/Core/ProcessWatcher.cs
src/X3DCcdOptimizer/Core/AffinityManager.cs
tests/X3DCcdOptimizer.Tests/X3DCcdOptimizer.Tests.csproj
```

## Notes

- .NET 8 SDK was not installed — installed via `winget install Microsoft.DotNet.SDK.8` during session
- No unit tests written yet (test project scaffolded but empty)
- App requires an AMD X3D dual-CCD processor to fully function; on non-X3D systems, CCD detection will fail (config override available)
- Affinity changes work best when run as Administrator; standard user mode will skip system processes with access denied
