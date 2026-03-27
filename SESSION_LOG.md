# Session Log

Development session history for X3D Dual CCD Optimizer.

---

## Current State (for new sessions — read this first)

**Version:** 1.0.0 | **Status:** Release | **Branch:** develop | **Last session:** 20

**What exists:**
- .NET 8 / C# 12 WPF application targeting `net8.0-windows` with WinForms (for NotifyIcon)
- **Three-tier processor support:** DualCcdX3D (full: affinity pinning + driver preference), SingleCcdX3D (monitoring + dashboard, no CCD steering), DualCcdStandard (affinity pinning, no driver preference). ProcessorTier enum, tier auto-detected from L3 cache topology.
- **Core engine:** CcdMapper (P/Invoke topology, 1-or-2 CCD detection), PerformanceMonitor (PDH), ProcessWatcher, GameDetector (4-tier: manual → 65-game DB → launcher scan → GPU heuristic), GpuMonitor (WMI), AffinityManager (mode+strategy+tier-aware, lock-based), VCacheDriverManager (amd3dvcache registry), GameLibraryScanner (Steam + Epic)
- **Game launcher scanning:** GameLibraryScanner scans Steam (registry → VDF/ACF parsing → exe directory scan) and Epic (JSON manifests in ProgramData). Results cached to `installed_games.json` in %APPDATA%, background rescan if >7 days stale. VDF parser handles Valve KeyValues format. Filters non-game exes (UnityCrashHandler, redist, setup, etc.). GOG skipped (requires SQLite dependency). Found 532 games on dev machine.
- **Display name resolution:** ProcessInfo.DisplayName populated at detection time from known_games.json or launcher scan. Propagated through AffinityEvent.DisplayName. All UI surfaces (status bar, CCD role labels, activity log, process router, overlay) show resolved game names. Fallback: strip .exe extension.
- **Optimization strategies:** AffinityPinning (default, SetProcessAffinityMask) or DriverPreference (AMD amd3dvcache registry interface, discovered by cocafe/vcache-tray). Strategy stored in config, selectable in Settings, gated by driver availability and tier.
- **WPF dashboard:** MVVM, dark theme, CCD panels (1 or 2 based on tier) with 4x2 core tile heatmaps, grouped process router (by CCD with game badges), activity log, animated pill toggle (Monitor/Optimize), polished UI
- **Compact overlay:** Discord-style toast/pill (auto-width 200-400px), slide-in/out animation (300ms cubic ease), semi-transparent dark (#1A1A1A at 85%), single/two-line contextual messages, opt-in CCD load bars (8px, green V-Cache + blue Freq, toggle in Settings > Overlay), OLED-safe (auto-hide, pixel shift), Ctrl+Shift+O hotkey, draggable, position persisted
- **System tray:** WinForms NotifyIcon, colored circle icons (blue/purple/green), context menu with mode + overlay + settings
- **Monitor/Optimize dual-mode:** Monitor (default, observe-only, all tiers), Optimize (active affinity or driver preference, dual-CCD only, tier-gated)
- **Settings window:** 5-tab modal dialog (General, Games, Detection, Overlay, Advanced) with live-apply. Strategy selector in General tab. Start-with-Windows via registry HKCU Run key + `--minimized` flag.
- **Dirty shutdown recovery:** RecoveryManager writes recovery.json while optimizing. Strategy-aware: AffinityPinning restores process affinities, DriverPreference restores registry default. Handles corrupted files, exited/restarted processes.
- **Config:** JSON at %APPDATA%\X3DCCDOptimizer\config.json, version 3, overlay + autoDetection + debounce + optimizeStrategy settings
- **Security audits:** Session 6 audit (2 critical, 3 high, 7 medium). Session 11 audit (3 high, 6 medium, 4 low — all 12 actionable findings fixed). Session 17 defensive coding audit (0 critical, 0 high, 2 medium, 6 low, 22 info — all 8 actionable findings fixed in session 18). Single-instance mutex, atomic file writes, config validation, protected process recovery filter, admin elevation manifest, thread safety across GameDetector/VCacheDriverManager/ProcessWatcher, WMI timeouts, registry value validation, debug logging in catch blocks, core index bounds checks.
- **Self-contained publish:** ~155MB single exe (WPF+WinForms runtime bundled)

**Key files:** `App.xaml.cs` (entry point), `Core/AffinityManager.cs` (mode+strategy-aware), `Core/VCacheDriverManager.cs` (amd3dvcache registry), `Core/GameDetector.cs` (4-tier), `Core/GameLibraryScanner.cs` (Steam+Epic scanner), `Core/RecoveryManager.cs` (crash recovery), `Core/StartupManager.cs` (registry), `ViewModels/MainViewModel.cs` (orchestrator), `ViewModels/SettingsViewModel.cs` (settings), `Views/DashboardWindow.xaml` (main UI), `Views/OverlayWindow.xaml` (overlay), `Views/SettingsWindow.xaml` (settings)

**What's next:** Phase 4 (CI/CD, trimmed build, installer, release)

**Known gotchas:**
- CACHE_RELATIONSHIP struct needs 18-byte `Reserved` field (not 2-byte) — was a real bug
- Hardcodet.NotifyIcon.Wpf is incompatible with .NET 8 — use WinForms NotifyIcon instead
- WPF + WinForms namespace conflicts — removed WinForms global using, disambiguate with full type names
- Overlay requires borderless windowed (exclusive fullscreen blocks standard overlays)
- ComboBox SelectedValue binding for default mode — don't use RadioButton with complex converters in XAML, just use ComboBox
- amd3dvcache driver registry changes may take minutes without service restart — document as known tradeoff for Driver Preference strategy
- SingleCcdX3D sets FrequencyCores=[] and FrequencyMask=IntPtr.Zero — all code referencing these must null/empty guard. CcdMapper, AffinityManager, MainViewModel (Ccd1Panel nullable), OverlayViewModel all audited and guarded.
- WPF CollectionViewSource grouping requires SortDescriptions added before data arrives — set up in constructor
- WPF relative Icon paths resolve from XAML file's namespace location, not project root — use pack URIs for embedded resources
- Steam VDF files use Valve KeyValues format (not JSON) — need custom parser; escaped backslashes in paths
- WPF ComboBox dropdown popup uses SystemColors for background/text — override WindowBrushKey, HighlightBrushKey, ControlTextBrushKey in style for dark theme
- WPF grid rows with star sizing compete for space — use Auto for fixed-content rows (CCD panels), star for scrollable areas (process router, activity log)

---

## Session 20 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Fix scrollbar layout + ComboBox contrast bugs, add opt-in CCD load bars to overlay

### What Was Done

1. **Fix: scrollbar layout** — Dashboard grid Row 1 (CCD panels) changed from `*` to `Auto` — fixed-size content doesn't need star sizing. Process Router changed from fixed `150px` to `*`. Activity Log changed to `2*`. Both scrollable areas now get properly constrained height, enabling functional scrollbars.

2. **Fix: ComboBox dark theme contrast** — Added implicit `ComboBox` and `ComboBoxItem` styles to `Themes/Controls.xaml`. Overrides `SystemColors.WindowBrushKey` (#2A2A2E), `HighlightBrushKey` (#404048), `HighlightTextBrushKey` (white), and `ControlTextBrushKey` (#E0E0E0). Dropdown popup background is now dark with readable light text.

3. **Overlay CCD load bars (opt-in)** — New `OverlayConfig.ShowLoadBars` (bool, default true). Toggle in Settings > Overlay tab: "Show CCD load bars". When enabled, two compact 8px horizontal bars appear below event text in the overlay pill — green (#1D9E75) for V-Cache CCD with label + percentage, blue (#378ADD) for Frequency CCD. Bars update from `PerformanceMonitor.SnapshotReady` at dashboard refresh rate. Single-CCD tiers show one bar. When disabled, overlay remains clean text-only toast. Re-wired `SnapshotReady` to `OverlayViewModel.OnSnapshotReady` (early-returns when bars disabled).

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| a58e029 | develop, master | fix: scrollbar layout + ComboBox dark theme contrast |
| 6de68f4 | develop, master | feat: opt-in CCD load bars in overlay |

### Files Modified (8)

```
Views/DashboardWindow.xaml — grid row sizing: CCD panels Auto, scrollable areas star
Themes/Controls.xaml — dark ComboBox + ComboBoxItem styles with SystemColors overrides
Config/AppConfig.cs — new OverlayConfig.ShowLoadBars (bool, default true)
ViewModels/SettingsViewModel.cs — ShowOverlayBars property + init/apply wiring
ViewModels/OverlayViewModel.cs — CCD load properties, OnSnapshotReady, bar visibility flags
Views/OverlayWindow.xaml — two load bar grids with conditional visibility
Views/SettingsWindow.xaml — "Show CCD load bars" checkbox in Overlay tab
App.xaml.cs — re-wire SnapshotReady to overlay + cleanup in OnExit
```

---

## Session 19 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Game launcher scanning, display name resolution, overlay redesign for 1.0

### What Was Done

1. **Fix: app.ico embedded resource** — DashboardWindow.xaml used a relative Icon path that WPF resolved to `Views/Resources/app.ico` (nonexistent in publish). Changed to pack URI + `<Resource>` embed. TrayIconManager switched from file-based loading to `Application.GetResourceStream`. Published single-file exe no longer crashes on startup.

2. **Game Launcher Scanner** — New `Core/GameLibraryScanner.cs` (446 lines). Scans Steam (registry `HKCU\Software\Valve\Steam\SteamPath` → `libraryfolders.vdf` → `appmanifest_*.acf` → exe directory scan) and Epic (JSON manifests in `C:\ProgramData\Epic\...\Manifests\*.item`). Includes a minimal VDF parser for Valve KeyValues format. Filters non-game exes (UnityCrashHandler, CrashReporter, redist, setup, installer, etc.). GOG Galaxy skipped — requires SQLite NuGet dependency, violates no-unnecessary-deps convention. Found 532 games on dev machine.

3. **Launcher scan caching** — Results saved to `installed_games.json` in `%APPDATA%\X3DCCDOptimizer\` with atomic writes. On startup: loads cache if fresh (<7 days), uses immediately. If stale, kicks off background `Task.Run` rescan. `GameDetector.UpdateLauncherGames()` hot-swaps the dictionary (volatile reference swap, safe for concurrent reads).

4. **4-tier game detection** — Added `DetectionMethod.LauncherScan` enum value. GameDetector now checks: manual list → known_games.json → launcher scan → GPU auto. Detection source shown in activity log as `[launcher]`.

5. **Display name resolution** — Added `ProcessInfo.DisplayName` (nullable). Populated at detection time in ProcessWatcher via `GameDetector.GetDisplayName()` which checks known_games.json → launcher scan → strips .exe fallback. Added `AffinityEvent.DisplayName` propagated through AffinityManager's Emit calls for game-related events. All UI surfaces updated: status bar, CCD role labels, activity log (`LogEntryViewModel`), process router (`ProcessRouterViewModel`), overlay.

6. **Overlay redesign (Discord-style toast)** — Complete rewrite of OverlayViewModel, OverlayWindow.xaml, OverlayWindow.xaml.cs. Changed from 280x160 fixed rectangle with CCD load bars to compact auto-width pill (200-400px, `SizeToContent="WidthAndHeight"`). Single line for idle ("Monitoring"), two lines for events (game name + action). Slide-in/out animation (300ms/250ms cubic ease, 60px translate + opacity fade). Semi-transparent dark background (`#D91A1A1A` = 85% opacity, `CornerRadius="20"`). Removed CCD load bars and `OnSnapshotReady` subscription — overlay now shows contextual event messages only. Kept: auto-hide timer, pixel shift, Ctrl+Shift+O hotkey, OLED-safe design, draggable, position persistence.

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| 29fc5c2 | develop, master | fix: embed app.ico as WPF resource — fixes crash in single-file publish |
| 4726eab | develop, master | feat: game launcher scanner, display name resolution, overlay redesign |

### Files Created (1)

```
Core/GameLibraryScanner.cs — Steam + Epic scanner with VDF parser and caching
```

### Files Modified (11)

```
App.xaml.cs — wire GameLibraryScanner, background rescan, remove overlay SnapshotReady
Core/AffinityManager.cs — pass DisplayName through Emit for game events
Core/GameDetector.cs — 4th tier (LauncherScan), launcher games dict, enhanced GetDisplayName
Core/ProcessWatcher.cs — populate DisplayName on ProcessInfo at detection time
Models/AffinityEvent.cs — add DisplayName property
ViewModels/MainViewModel.cs — display names in status text + CCD role labels
ViewModels/OverlayViewModel.cs — complete rewrite for toast/pill style
ViewModels/LogEntryViewModel.cs — use DisplayName in detail text
ViewModels/ProcessRouterViewModel.cs — use DisplayName for game entries
Views/OverlayWindow.xaml — complete rewrite: pill shape, auto-width, slide transform
Views/OverlayWindow.xaml.cs — slide-in/out animation, updated positioning
X3DCcdOptimizer.csproj — app.ico as embedded Resource
Views/DashboardWindow.xaml — pack URI for icon
Tray/TrayIconManager.cs — load icon from embedded resource stream
```

---

## Session 18 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Fix all Medium and Low findings from session 17 defensive coding audit

### What Was Done

1. **DEF-001 (Medium) — RelayCommand null-safe invoke** — Changed `_execute()` to `_execute?.Invoke()` in `RelayCommand.Execute()`. Prevents NullReferenceException if constructed with null delegate.

2. **DEF-009 (Medium) — Process.GetProcesses() try/catch** — Wrapped the `Process.GetProcesses()` call itself in try/catch in 3 locations: `AffinityManager.MigrateBackground`, `AffinityManager.SimulateMigrateBackground`, `RecoveryManager.RecoverAffinityPinning`. Logs warning and returns early if enumeration fails.

3. **DEF-013 (Low) — CcdMapper division guard** — Added `cacheSizes.Count > 0` ternary guard before `TotalLogicalCores / cacheSizes.Count` in WMI fallback path. Practically unreachable (line 155 throws on count==0) but belt-and-suspenders.

4. **DEF-016 (Low) — Tmp file cleanup on save failure** — Hoisted `tempPath` declaration before try block and added `File.Delete(tempPath)` in catch for both `AppConfig.Save()` and `RecoveryManager.WriteState()`. Prevents orphaned .tmp files when File.Move fails.

5. **DEF-034 (Low) — Overlay pixel shift multi-monitor fix** — Overlay clamp now uses `VirtualScreenLeft`/`VirtualScreenTop` offsets instead of hardcoded 0. Fixes pixel shift drift on multi-monitor setups where primary monitor is not at (0,0).

6. **DEF-035 (Low) — Safe JSON property access** — Replaced `GetProperty("exe")` / `GetProperty("name")` with `TryGetProperty` in both `GameDetector.LoadKnownGames()` and `SettingsViewModel` constructor. Malformed entries in known_games.json are now skipped individually instead of aborting the entire list.

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| 7b82c12 | develop, master | fix: defensive coding fixes from audit (2 medium, 6 low) |

### Files Modified (8)

```
ViewModels/RelayCommand.cs — null-safe _execute invocation
Core/AffinityManager.cs — Process.GetProcesses() try/catch (2 locations)
Core/RecoveryManager.cs — Process.GetProcesses() try/catch + .tmp cleanup
Core/CcdMapper.cs — division-by-zero guard in WMI fallback
Config/AppConfig.cs — .tmp file cleanup on save failure
Views/OverlayWindow.xaml.cs — VirtualScreenLeft/Top clamp
ViewModels/SettingsViewModel.cs — TryGetProperty for known games JSON
Core/GameDetector.cs — TryGetProperty for known games JSON
```

---

## Session 17 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Defensive coding audit — robustness, crash prevention, graceful degradation

### What Was Done

1. **Comprehensive defensive coding audit** — Read all 37 source files. Analyzed null safety, defensive casts, collection safety, arithmetic safety, resource cleanup, async/threading, external dependency resilience, startup/shutdown resilience, and edge case data handling.

2. **35 findings:** 0 critical, 0 high, 2 medium, 6 low, 22 info (already handled well). Report saved as `DEFENSIVE_AUDIT.md` (internal, not shipped).

3. **Key positive findings:** All P/Invoke handles properly cleaned up. All event invocations null-safe. All dispatcher calls use BeginInvoke (no deadlocks). No async void. Single lock per class. Config uses atomic writes. Shutdown handles partial init. Enum parsing uses safe defaults.

4. **Top 2 actionable:** DEF-009 (Process.GetProcesses() not wrapped in try/catch at enumeration level in 3 locations), DEF-001 (RelayCommand._execute not null-checked).

### Files Created (1)

```
DEFENSIVE_AUDIT.md — internal audit report
```

---

## Session 16 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Add app icon, social preview, tray icon compositing with status dots

### What Was Done

1. **App icon integration** — `logos/app.ico` copied to `src/X3DCcdOptimizer/Resources/app.ico`. Added `<ApplicationIcon>` to .csproj (embeds in exe). Set `Icon="Resources/app.ico"` on DashboardWindow.xaml for window titlebar. Icon copied to build output via `CopyToOutputDirectory`.

2. **Tray icon compositing with status dots** — Rewrote `IconGenerator.cs` to composite a colored status dot onto the app icon at runtime. Dot is ~25% of icon size (8px on 32px icon), bottom-right corner, with 1px dark border for taskbar contrast.
   - Blue dot: Monitor mode, idle
   - Purple dot: Monitor + game observed, or Optimize idle
   - Green dot: Optimize + game engaged
   - Falls back to plain colored circles if app.ico is not found.
   - `SetBaseIcon(Icon?)` called once at startup from TrayIconManager. Results cached per color.

3. **TrayIconManager rewrite** — Loads app.ico from Resources at startup, passes to IconGenerator. State-to-color mapping: `(Mode, IsGameActive)` tuple switch for dot color selection. Removed old static `AppIcon` field approach.

4. **Logo and social preview** — `logos/logo-512.png` copied to repo root as `logo-512.png` (README header) and `social-preview.png` (GitHub). README header updated with `![X3D CCD Optimizer](logo-512.png)`.

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| (pending) | develop | feat: app icon with composited status dot tray indicator |

### Files Created (3 new)

```
src/X3DCcdOptimizer/Resources/app.ico — app icon for exe, window, tray base
logo-512.png — logo for README header
social-preview.png — GitHub social preview image
```

### Files Modified (5)

```
X3DCcdOptimizer.csproj — ApplicationIcon + Resources copy to output
Views/DashboardWindow.xaml — Icon="Resources/app.ico"
Tray/IconGenerator.cs — rewritten: SetBaseIcon, CompositeIconWithDot, status dot overlay
Tray/TrayIconManager.cs — rewritten: LoadAndSetBaseIcon, mode-aware dot color mapping
README.md — logo header image
```

---

## Session 15 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** README rewrite for 1.0 release + zero-config user journey audit + fix 3 audit gaps

### What Was Done

1. **Comprehensive README rewrite** — Full 1.0 release documentation with new sections: "Why This Tool Exists" (AMD software chain problem), "Optimization Strategies" (Affinity Pinning vs Driver Preference with pros/cons/requirements), "System Requirements", "Supported Processors" (4-tier table), "Windows Settings Compatibility" (table showing what matters per strategy), "Does This Conflict with AMD's Own Optimization?", updated "Known Limitations" (Driver Preference latency, 64+ processors, AMD Application Compatibility Database). All current features accurately documented.

2. **Zero-config user journey audit** — Traced 8 complete user journeys through exact code lines: fresh install, known game, unknown game, no chipset drivers, High Performance power plan, Game Bar disabled, no GPU, Intel CPU. Found 3 gaps (ZC-001/002/003). Report saved as `ZERO_CONFIG_AUDIT.md`.

3. **ZC-003 (Medium) — Non-AMD CPU warning** — After topology detection, checks `CpuTopology.CpuModel` for "AMD". If absent, shows dialog: "X3D CCD Optimizer is designed for AMD Ryzen processors. Your CPU ({name}) is not an AMD processor." with Continue Anyway / Exit buttons. Logged at Warning level.

4. **ZC-001 (Low) — GPU heuristic rejection feedback** — New `DetectionSkipped` AffinityAction value. ProcessWatcher reports foreground processes with GPU > 0% but below threshold, once per PID per session. Shows in activity log as "[AUTO] BELOW THRESHOLD" with muted styling. Tracked PIDs reset when foreground changes.

5. **ZC-002 (Low) — Power plan warning for Driver Preference** — WMI query (`Win32_PowerPlan WHERE IsActive=True`) at startup when strategy is DriverPreference. If not Balanced, warns in status bar: "Optimize — waiting for game | Power plan '{name}' detected — Balanced recommended for Driver Preference". Silently skips if WMI fails.

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| (pending) | develop | docs: comprehensive README rewrite for 1.0 release |
| (pending) | develop | audit: zero-config user journey audit (8 journeys, 3 gaps) |
| (pending) | develop | fix: 3 zero-config audit gaps (Intel detection, GPU feedback, power plan warning) |

### Files Created (1 new)

```
ZERO_CONFIG_AUDIT.md — zero-config user journey audit (internal)
```

### Files Modified (8)

```
README.md — complete rewrite with AMD chain explanation, strategies, compatibility tables
App.xaml.cs — non-AMD CPU warning dialog, power plan WMI query, DetectionSkipped event wiring
Models/AffinityEvent.cs — DetectionSkipped enum value
Core/ProcessWatcher.cs — below-threshold reporting with per-PID dedup
ViewModels/LogEntryViewModel.cs — DetectionSkipped display case
ViewModels/OverlayViewModel.cs — DetectionSkipped prefix
ViewModels/MainViewModel.cs — PowerPlanWarning property, status bar display
SESSION_LOG.md — session 15
CHANGELOG.md — new entries
```

---

## Session 14 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Fix remaining 8 pre-release audit items (PRE-004/007/009/010/013/021/023/025)

### What Was Done

1. **PRE-004 — Deduplicate protected process lists** — New `Models/ProtectedProcesses.cs` with shared `IReadOnlySet<string>`. Both `AffinityManager` and `RecoveryManager` now reference the single source.

2. **PRE-010 — Reduce GPU query frequency when idle** — `GpuMonitor` gains `IsGameActive` flag and `_idleSkipCounter`. When no game is detected, GPU queries skip every other poll cycle (4s effective vs 2s). Wired via game detect/exit events in `App.xaml.cs`.

3. **PRE-013 — Unwire event subscriptions on shutdown** — All engine event subscriptions (`SnapshotReady`, `AffinityChanged`, `GameDetected`, `GameExited`) unwired in `App.OnExit` before Stop/Dispose calls.

4. **PRE-009 — First-run mode explanation** — `AppConfig.IsFirstRun` (`[JsonIgnore]`) set when no config.json exists. MainViewModel shows onboarding status text on first launch: "Monitor mode — observing your CPU without making changes. Switch to Optimize to pin games to V-Cache." Overwritten on first mode toggle or game detection.

5. **PRE-007 — Tooltips on all settings controls** — 26 tooltips added to every interactive control in SettingsWindow.xaml. Plain English, no jargon.

6. **PRE-023 — AutomationProperties on interactive UI elements** — `AutomationProperties.Name` added to all interactive elements across DashboardWindow, SettingsWindow, and OverlayWindow XAML.

7. **PRE-025 — AccessKeys for keyboard shortcuts** — Added to Settings tabs (`_General`, `G_ames`, `_Detection`, `_Overlay`, `Ad_vanced`) and buttons (`_Apply`, `_Reset All to Defaults`).

8. **PRE-021 — Strategy requires restart note** — "Strategy changes take effect on next launch." shown below strategy dropdown, visible only when strategy selection is available.

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| (pending) | develop | fix: remaining 8 pre-release audit items |

### Files Created (1 new)

```
src/X3DCcdOptimizer/Models/ProtectedProcesses.cs
```

### Files Modified (9)

```
Core/AffinityManager.cs — reference shared ProtectedProcesses
Core/RecoveryManager.cs — reference shared ProtectedProcesses
Core/GpuMonitor.cs — idle skip counter, IsGameActive flag
App.xaml.cs — wire GpuMonitor.IsGameActive, unwire events on shutdown
Config/AppConfig.cs — IsFirstRun property
ViewModels/MainViewModel.cs — first-run onboarding status text
Views/SettingsWindow.xaml — tooltips, AutomationProperties, AccessKeys, strategy restart note, BoolToVis converter
Views/DashboardWindow.xaml — AutomationProperties on toggle, panels, buttons
Views/OverlayWindow.xaml — AutomationProperties on window
```

---

## Session 13 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Pre-release audit + fix 5 must-fix items for 1.0

### What Was Done

1. **Comprehensive pre-release audit** — read every file in the codebase across 6 categories (security delta, usability, performance, edge cases, accessibility, release readiness). 30 findings: 0 critical, 3 high, 4 medium, 9 low, 13 info. Report saved as `PRE_RELEASE_AUDIT.md`.

2. **PRE-002 (High) — IsSingleCcd guard on SwitchToOptimize/SwitchToMonitor** — Added early return when `_topology.IsSingleCcd` is true. Prevents `MigrateBackground()` from calling `SetProcessAffinityMask(handle, IntPtr.Zero)` on single-CCD systems, which would starve all background processes of CPU time.

3. **PRE-006 (High) — Version unified to 1.0.0** — Updated 7 files: App.xaml.cs, .csproj, MainViewModel footer, TrayIconManager About dialog, Blueprint header, SESSION_LOG current state, bug report template.

4. **PRE-001 (High) — WMI fallback core mask for single-CCD** — Restructured `DetectViaWmi()` to a two-pass approach: first count L3 caches, then divide cores by actual count (was hardcoded `/2`). Single-CCD processors now get all cores in one mask.

5. **PRE-003 (Medium) — SingleCcdStandard tier** — Added `ProcessorTier.SingleCcdStandard` for non-X3D single-CCD processors (7700X, 5800X). Tier determined by 64MB L3 threshold (V-Cache >= 64MB, standard < 64MB). Updated `IsSingleCcd` to include both single-CCD tiers. Updated all 4 switch expressions (CcdPanelViewModel badge, SettingsViewModel tier description, MainViewModel status text x2).

6. **PRE-008 (Medium) — User-friendly error message** — Replaced raw exception + "dual-CCD" text with clear message: "requires AMD Ryzen processor with identifiable L3 cache topology". Directs users to log file for details.

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| (pending) | develop | audit: comprehensive pre-release audit |
| (pending) | develop | fix: 5 must-fix items from pre-release audit (v1.0.0) |

### Files Created (1 new)

```
PRE_RELEASE_AUDIT.md — full audit report (internal, not in shipping build)
```

### Files Modified (13)

```
Core/AffinityManager.cs — IsSingleCcd guard on SwitchToOptimize/SwitchToMonitor
Core/CcdMapper.cs — two-pass WMI fallback, 64MB V-Cache threshold for tier
Models/ProcessorTier.cs — added SingleCcdStandard
Models/CpuTopology.cs — IsSingleCcd covers both single-CCD tiers
App.xaml.cs — version 1.0.0, user-friendly error message
X3DCcdOptimizer.csproj — version 1.0.0
ViewModels/MainViewModel.cs — version 1.0.0, tier-aware status for SingleCcdStandard
ViewModels/CcdPanelViewModel.cs — badge for SingleCcdStandard
ViewModels/SettingsViewModel.cs — tier description for SingleCcdStandard
Tray/TrayIconManager.cs — version 1.0.0 in About dialog
X3D_CCD_OPTIMIZER_BLUEPRINT.md — version 1.0.0
SESSION_LOG.md — version 1.0.0
.github/ISSUE_TEMPLATE/bug_report.md — version 1.0.0
```

---

## Session 12 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Ryzen-wide 3-tier processor support + Process Router grouped view redesign

### What Was Done

1. **Three-tier processor support**
   - `ProcessorTier` enum: `DualCcdX3D`, `SingleCcdX3D`, `DualCcdStandard`
   - `CpuTopology`: new `Tier`, `IsSingleCcd`, `IsDualCcd` properties. `GetCcdIndex()` returns 0 for all cores on single-CCD. `FrequencyMaskHex` handles zero mask.
   - `CcdMapper`: shared `AssignTopologyFromCaches()` handles 1 or 2 L3 caches. Tier determined by cache count + size ratio. Relaxed `ValidateTopology` — `FrequencyCores` can be empty for single-CCD. Both P/Invoke and WMI paths updated.
   - `AffinityManager`: early-returns with `Skipped` event for single-CCD in both `OnGameDetected` and `OnGameExited` — no engagement, migration, or restoration attempted.
   - `App.xaml.cs`: forces Monitor mode for single-CCD, gates DriverPreference to DualCcdX3D only. Logs tier at startup.

2. **Tier-aware UI throughout**
   - `MainViewModel`: `Ccd1Panel` is nullable (null for single-CCD), all 7 references null-guarded with `?.` or `!= null`. `ShowSecondPanel` for XAML binding. `IsOptimizeEnabled` uses `IsDualCcd`. Tier-aware status text and role labels for all three tiers.
   - `CcdPanelViewModel`: badge text varies by tier — "V-Cache CCD" / "V-Cache"+"Frequency" / "CCD 0"+"CCD 1".
   - `SettingsViewModel`: `CanOptimize`, `IsStrategyAvailable`, `TierDescription` properties. `IsDriverAvailable` gated to DualCcdX3D. Mode and strategy dropdowns tier-gated in XAML.
   - `DashboardWindow.xaml`: second CCD panel hidden via `BooleanToVisibilityConverter` when `ShowSecondPanel` is false.

3. **Process Router grouped view redesign**
   - `ProcessEntryViewModel`: new `CcdGroup`, `IsGame`, `SortOrder`, `PidText`, `TypeBadgeColor` properties.
   - `ProcessRouterViewModel`: takes CCD names in constructor. `ProcessView` (`ICollectionView`) with `PropertyGroupDescription("CcdGroup")` + sort by game-first then name. `EmptyVisibility` for empty-state placeholder.
   - `DashboardWindow.xaml`: `GroupStyle` with header template (CCD name + item count), indented process entries, green "GAME" badge for game processes, "No managed processes" empty state.

4. **Full FrequencyCores/FrequencyMask/Ccd1Panel audit**
   - Traced every reference across the entire codebase (grep found 30+ references)
   - `AffinityManager.MigrateBackground` / `SimulateMigrateBackground`: unreachable for single-CCD due to early return guard
   - `SwitchToOptimize`: unreachable for single-CCD because `IsOptimizeEnabled` disables the UI toggle
   - `OverlayViewModel.OnSnapshotReady`: already safe — `c1.Length > 0 ? ... : 0` handles empty CCD1
   - `CcdPanelViewModel(topology, 1)`: never constructed for single-CCD — `Ccd1Panel` is null
   - `PerformanceMonitor.GetCcdIndex`: returns 0 for all cores on single-CCD

5. **README update** — Three-tier supported processor table (Tier 1/2/3), updated status to Pre-1.0.

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| (pending) | develop | feat: add three-tier Ryzen processor support and grouped process router |

### Files Created (1 new)

```
src/X3DCcdOptimizer/Models/ProcessorTier.cs
```

### Files Modified (12)

```
Models/CpuTopology.cs — Tier, IsSingleCcd, IsDualCcd, safe GetCcdIndex, safe FrequencyMaskHex
Core/CcdMapper.cs — AssignTopologyFromCaches, 1-CCD support, tier detection, relaxed validation
Core/AffinityManager.cs — single-CCD early return guard in OnGameDetected + OnGameExited
App.xaml.cs — tier-based mode/strategy gating at startup
ViewModels/CcdPanelViewModel.cs — tier-aware badge text
ViewModels/MainViewModel.cs — nullable Ccd1Panel, ShowSecondPanel, tier-aware status/labels, ProcessRouter with CCD names
ViewModels/SettingsViewModel.cs — CanOptimize, IsStrategyAvailable, TierDescription, tier-gated IsDriverAvailable
ViewModels/ProcessEntryViewModel.cs — CcdGroup, IsGame, SortOrder, PidText, TypeBadgeColor
ViewModels/ProcessRouterViewModel.cs — CollectionViewSource grouping, CCD names, EmptyVisibility
Views/DashboardWindow.xaml — BooleanToVisibilityConverter, hidden second panel, grouped process router
Views/SettingsWindow.xaml — tier description, tier-gated mode/strategy dropdowns
README.md — three-tier processor table, updated status
```

---

## Session 11 — 2026-03-27

**Agent:** Claude Opus 4.6 (1M context)
**Goal:** Comprehensive security audit + fix all actionable findings

### What Was Done

1. **Full codebase security audit** — read every source file (45+ files). Produced `SECURITY_AUDIT.md` with 17 findings (0 critical, 3 high, 6 medium, 4 low, 4 info). Covered registry ops, process manipulation, file I/O, WMI queries, input validation, thread safety, exception handling, admin elevation, overlay/UI, dependencies, secrets, and startup/shutdown.

2. **SEC-002 — Single-instance mutex** — Added `Global\X3DCcdOptimizer_SingleInstance` named mutex in `App.OnStartup` before any other work. Shows MessageBox and shuts down if already running.

3. **SEC-001 — Atomic file writes** — Both `AppConfig.Save()` and `RecoveryManager.WriteState()` now write to `.tmp` then `File.Move(overwrite: true)`. Prevents corruption on crash/power loss.

4. **SEC-006 — Config validation** — Added `AppConfig.Validate()` clamping all numeric values to sane ranges (polling 500–30000ms, GPU threshold 1–100%, overlay opacity 0.1–1.0, etc.). CcdOverride core indices validated 0–63. Called after `Load()` in startup.

5. **SEC-003 — Protected process filter in recovery** — `RecoverAffinityPinning()` now skips System, csrss, lsass, dwm, svchost and 10 other protected process names. Prevents malicious recovery.json from modifying critical system processes.

6. **SEC-008 — WMI timeouts** — Added 10-second timeouts to both WMI queries in `CcdMapper.cs` (Win32_CacheMemory, Win32_Processor). Prevents startup hang if WMI is stuck.

7. **SEC-010 — Admin elevation** — Changed `app.manifest` from `asInvoker` to `requireAdministrator`. App writes HKLM and sets process affinities — it must run elevated.

8. **SEC-005 — GameDetector thread safety** — Replaced auto-property `CurrentGame` with lock-protected backing field.

9. **SEC-004 — VCacheDriverManager thread-safe init** — Replaced `bool? + ??=` with `Lazy<bool>` for `IsDriverAvailable`.

10. **SEC-009 — ProcessWatcher dispose race** — Made `_disposed` volatile, added early exit at top of `Poll()`, suppressed logging during shutdown.

11. **SEC-007 — Registry value validation** — `GetCurrentPreference()` validates int is 0 or 1 (warns + returns null otherwise). `WritePreference()` throws `ArgumentOutOfRangeException` on invalid values.

12. **SEC-011 — Empty catch blocks** — Added `Log.Debug()` to all 5 silent catch blocks (ProcessWatcher ×2, App.xaml.cs, GpuMonitor, StartupManager).

13. **SEC-013 — Core index bounds** — `CcdMapper.CoresMask()` skips and warns on core indices outside 0–63 instead of silently wrapping.

### Commits

| Hash | Branch | Message |
|------|--------|---------|
| (pending) | develop | audit: comprehensive security audit of full codebase |
| (pending) | develop | fix: implement all 12 actionable security audit findings |

### Files Created (1 new)

```
SECURITY_AUDIT.md — full audit report (internal, not in shipping build)
```

### Files Modified (10)

```
App.xaml.cs — single-instance mutex, config validation call, debug logging in catch
Config/AppConfig.cs — atomic writes, Validate() method
Core/RecoveryManager.cs — atomic writes, protected process filter
Core/CcdMapper.cs — WMI timeouts, core index bounds check
Core/GameDetector.cs — lock-protected CurrentGame
Core/VCacheDriverManager.cs — Lazy<bool>, registry value validation
Core/ProcessWatcher.cs — volatile _disposed, early exit in Poll, debug logging
Core/GpuMonitor.cs — debug logging in catch
Core/StartupManager.cs — debug logging in catch
app.manifest — requireAdministrator
```

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
