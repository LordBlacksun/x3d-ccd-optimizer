# Session Log

Development session history for X3D Dual CCD Optimizer.

---

## Current State (for new sessions — read this first)

**Version:** 0.4.0 | **Status:** Phase 3 in progress | **Branch:** develop | **Last session:** 10

**What exists:**
- .NET 8 / C# 12 WPF application targeting `net8.0-windows` with WinForms (for NotifyIcon)
- **Core engine:** CcdMapper (P/Invoke topology), PerformanceMonitor (PDH), ProcessWatcher, GameDetector (3-tier: manual → 65-game DB → GPU heuristic), GpuMonitor (WMI), AffinityManager (mode-aware + strategy-aware, lock-based), VCacheDriverManager (amd3dvcache registry)
- **Optimization strategies:** AffinityPinning (default, SetProcessAffinityMask) or DriverPreference (AMD amd3dvcache registry interface, discovered by cocafe/vcache-tray). Strategy stored in config, selectable in Settings, gated by driver availability.
- **WPF dashboard:** MVVM, dark theme, two CCD panels with 4x2 core tile heatmaps, process router, activity log, animated pill toggle (Monitor/Optimize), polished UI (Cascadia Mono numbers, load bars, accent edges, pulsing dot, gradient separator)
- **Compact overlay:** 280x160 always-on-top, OLED-safe (auto-hide 10s, pixel shift 3min), Ctrl+Shift+O hotkey, draggable, position persisted
- **System tray:** WinForms NotifyIcon, colored circle icons (blue/purple/green), context menu with mode + overlay + settings
- **Monitor/Optimize dual-mode:** Monitor (default, observe-only, any dual-CCD Ryzen), Optimize (active affinity or driver preference, X3D only, HasVCache-gated)
- **Settings window:** 5-tab modal dialog (General, Games, Detection, Overlay, Advanced) with live-apply. Strategy selector in General tab. Start-with-Windows via registry HKCU Run key + `--minimized` flag.
- **Dirty shutdown recovery:** RecoveryManager writes recovery.json while optimizing. Strategy-aware: AffinityPinning restores process affinities, DriverPreference restores registry default. Handles corrupted files, exited/restarted processes.
- **Config:** JSON at %APPDATA%\X3DCCDOptimizer\config.json, version 3, overlay + autoDetection + debounce + optimizeStrategy settings
- **Code audit done:** 2 critical, 3 high, 7 medium issues fixed (config safety, global exception handler, thread-safe disposal, handle leaks, multi-monitor positions)
- **Self-contained publish:** ~155MB single exe (WPF+WinForms runtime bundled)

**Key files:** `App.xaml.cs` (entry point), `Core/AffinityManager.cs` (mode+strategy-aware), `Core/VCacheDriverManager.cs` (amd3dvcache registry), `Core/GameDetector.cs` (3-tier), `Core/RecoveryManager.cs` (crash recovery), `Core/StartupManager.cs` (registry), `ViewModels/MainViewModel.cs` (orchestrator), `ViewModels/SettingsViewModel.cs` (settings), `Views/DashboardWindow.xaml` (main UI), `Views/OverlayWindow.xaml` (overlay), `Views/SettingsWindow.xaml` (settings)

**What's next:** Ryzen-wide support (single CCD + symmetric dual CCD tiers), Process Router grouped view redesign, Phase 4 (CI/CD, trimmed build, installer)

**Known gotchas:**
- CACHE_RELATIONSHIP struct needs 18-byte `Reserved` field (not 2-byte) — was a real bug
- Hardcodet.NotifyIcon.Wpf is incompatible with .NET 8 — use WinForms NotifyIcon instead
- WPF + WinForms namespace conflicts — removed WinForms global using, disambiguate with full type names
- Overlay requires borderless windowed (exclusive fullscreen blocks standard overlays)
- ComboBox SelectedValue binding for default mode — don't use RadioButton with complex converters in XAML, just use ComboBox
- amd3dvcache driver registry changes may take minutes without service restart — document as known tradeoff for Driver Preference strategy

---

## Session 10 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Add AMD V-Cache driver registry preference as alternative optimization strategy

### What Was Done

1. **New OptimizeStrategy enum and VCacheDriverManager**
   - `OptimizeStrategy.cs`: `AffinityPinning` (default) | `DriverPreference` enum
   - `VCacheDriverManager.cs`: static class wrapping `HKLM\SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences` registry reads/writes. `IsDriverAvailable` (cached), `SetCachePreferred()`, `RestoreDefault()`, `GetCurrentPreference()`. Uses `Microsoft.Win32.Registry` (no new NuGet). Header comment credits cocafe/vcache-tray for discovering the registry interface.

2. **Strategy-aware AffinityManager**
   - Constructor takes `OptimizeStrategy` parameter
   - `OnGameDetected`: DriverPreference calls `EngageGameViaDriver()` (sets DefaultType=1), no background migration. AffinityPinning: unchanged.
   - `OnGameExited`: DriverPreference calls `RestoreDriver()` (sets DefaultType=0). AffinityPinning: unchanged.
   - `SwitchToOptimize`/`SwitchToMonitor`: strategy dispatch for mid-game mode switching
   - Monitor mode: emits `WouldSetDriver`/`WouldRestoreDriver` events for DriverPreference simulation

3. **Strategy-aware recovery**
   - `RecoveryState` gains `strategy` field (backward-compatible default `"affinityPinning"`)
   - `RecoveryManager.OnEngage()` accepts strategy parameter, stores in recovery.json
   - `RecoverFromDirtyShutdown`: if DriverPreference, restores registry default instead of process affinities

4. **12-value AffinityAction enum**
   - Added: `DriverSet`, `DriverRestored`, `WouldSetDriver`, `WouldRestoreDriver`
   - All ViewModels updated: LogEntryViewModel (colors/text), ProcessRouterViewModel (badge "V-Cache (Driver)"), OverlayViewModel (action prefixes)

5. **Strategy-aware dashboard status**
   - Optimize + DriverPreference: "Optimize — {game} V-Cache preferred (driver)"
   - Optimize + AffinityPinning: "Optimize — {game} pinned to V-Cache CCD" (unchanged)
   - CCD panel role labels: "V-Cache Preferred" vs "Gaming" based on strategy

6. **Settings UI — strategy selector**
   - ComboBox in General tab: "Affinity Pinning (default)" / "Driver Preference (AMD V-Cache)"
   - Disabled when driver not installed; warning text shown
   - `optimizeStrategy` config field, `GetOptimizeStrategy()` helper

7. **App.xaml.cs wiring**
   - Strategy resolution at startup with driver-unavailable fallback to AffinityPinning
   - Strategy logged at startup alongside mode

8. **README acknowledgements**
   - Added cocafe/vcache-tray credit for discovering the AMD V-Cache driver registry interface

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| (pending) | develop | feat: add AMD V-Cache driver registry preference as alternative optimization strategy |

### Files Created (2 new)

```
src/X3DCcdOptimizer/Models/OptimizeStrategy.cs
src/X3DCcdOptimizer/Core/VCacheDriverManager.cs
```

### Files Modified (13)

```
Models/AffinityEvent.cs — 4 new AffinityAction values
Models/RecoveryState.cs — strategy field
Config/AppConfig.cs — optimizeStrategy property + GetOptimizeStrategy()
Core/AffinityManager.cs — strategy parameter, driver dispatch, 4 new methods
Core/RecoveryManager.cs — strategy-aware OnEngage + recovery (driver vs affinity)
ViewModels/LogEntryViewModel.cs — 4 new action mappings + monitor check
ViewModels/ProcessRouterViewModel.cs — driver action cases
ViewModels/OverlayViewModel.cs — driver action prefixes
ViewModels/MainViewModel.cs — strategy-aware status text + role labels
ViewModels/SettingsViewModel.cs — strategy property, driver availability
Views/SettingsWindow.xaml — strategy ComboBox + driver warning
App.xaml.cs — strategy resolution + fallback + logging
README.md — cocafe acknowledgement
```

---

## Session 9 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Pre-1.0 build — dirty shutdown recovery, settings window

### What Was Done

1. **Dirty shutdown recovery** (`f189230`)
   - `RecoveryManager.cs`: writes `recovery.json` to `%APPDATA%` while affinities are engaged, listing all modified processes with original masks
   - On next launch: if recovery.json exists, resets all listed processes to full CPU affinity (all cores)
   - Handles corrupted JSON (delete and continue), exited processes (skip), restarted processes (match by name)
   - `RecoveryState.cs`: data model for recovery JSON (engaged, timestamp, game, modified processes)
   - AffinityManager integration: `OnEngage()` on game detect, `AddModifiedProcess()` on each migration, `OnDisengage()` on clean restore/exit
   - App.xaml.cs calls `RecoverFromDirtyShutdown()` before normal startup

2. **Settings window with live-apply** (`f81cade`)
   - 5-tab modal SettingsWindow: General, Games, Detection, Overlay, Advanced
   - **General:** start-with-Windows (registry HKCU Run key), default mode, start minimized, notifications, polling/refresh interval sliders
   - **Games:** manual game list with add/remove, read-only known games DB view (65 entries)
   - **Detection:** GPU auto-detect toggle, threshold/delay sliders, foreground requirement, exclusion list management
   - **Overlay:** enable toggle, opacity/auto-hide/pixel-shift sliders
   - **Advanced:** log level dropdown, open log/config folders, reset to defaults
   - OK/Cancel/Apply buttons — Apply writes to config and takes effect immediately
   - `StartupManager.cs`: registry HKCU Run key management with `--minimized` flag
   - App.xaml.cs handles `--minimized` command-line argument
   - Accessible from dashboard footer "Settings" button and tray context menu

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| `f189230` | develop | feat: add dirty shutdown recovery — restores CPU affinities after crash |
| `f81cade` | develop | feat: add settings window with live-apply and start-with-Windows support |
| `303d3e6` | master | merge of develop |

### Files Created (6 new)

```
src/X3DCcdOptimizer/Core/RecoveryManager.cs
src/X3DCcdOptimizer/Core/StartupManager.cs
src/X3DCcdOptimizer/Models/RecoveryState.cs
src/X3DCcdOptimizer/ViewModels/SettingsViewModel.cs
src/X3DCcdOptimizer/Views/SettingsWindow.xaml + .cs
```

### Files Modified (5)

```
App.xaml.cs — recovery on startup, --minimized flag
Core/AffinityManager.cs — RecoveryManager integration
ViewModels/MainViewModel.cs — OpenSettingsCommand
Views/DashboardWindow.xaml — Settings + Overlay buttons in footer
Tray/TrayIconManager.cs — Settings menu item
```

### Not completed (deferred to next session)

- Task 3: Ryzen-wide support (single CCD, symmetric dual CCD tiers)
- Task 4: Process Router grouped view redesign

---

## Session 8 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Comprehensive documentation update and repo prettification (v0.4.0)

### What Was Done

1. **Blueprint v0.4.0** (`158761a`) — Complete rewrite. Phases 1, 2, 2.5 marked complete. Added GpuMonitor, overlay, tray modules to architecture. Updated project structure, config schema (version 3 + overlay), risk register, compatibility. Phase 3/4 scoped. Author changed to LordBlacksun throughout.

2. **CLAUDE.md rewrite** — Full rewrite with overlay, GPU monitor, 3-tier detection pipeline, updated project structure (all ViewModels/Views/Converters/Tray/Themes), coding conventions (CACHE_RELATIONSHIP padding, thread safety, config safety), 8-value AffinityAction enum.

3. **README.md rewrite** — Updated features (overlay, GPU auto-detect, dual-mode), status (Phase 2.5 complete), added configuration section (manual games, auto-detection, overlay), known limitations (fullscreen, 155MB exe, GPU counters), expanded roadmap.

4. **CONTRIBUTING.md update** — Added known_games.json contribution workflow with exact format, issue template links, updated code guidelines and project structure.

5. **GitHub issue templates** — Created `.github/ISSUE_TEMPLATE/bug_report.md` (OS, CPU, mode, logs, screenshots) and `game_request.md` (game title, exe name, launcher, V-Cache notes).

6. **Footer core/thread fix** (`ac8d968`) — Added `TotalPhysicalCores` to CpuTopology from WMI `NumberOfCores`. Footer shows "16 cores | 32 threads" instead of "32 cores".

7. **README known limitations** (`c83f429`) — Overlay requires borderless windowed mode.

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| `c83f429` | develop | docs: add known limitations section to README |
| `ac8d968` | develop | fix: display correct physical core and thread count in footer |
| `158761a` | develop | docs: comprehensive documentation update and repo prettification (v0.4.0) |

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
| `c83f429` | develop | docs: add known limitations section to README |
| `a5361cb` | master | merge of c83f429 |

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
