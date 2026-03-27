# Pre-Release Audit Report — X3D Dual CCD Optimizer

**Date:** 2026-03-27
**Auditor:** Claude Opus 4.6 (1M context)
**Scope:** Full codebase — every source file, view, config, theme, doc, and data file
**Commit:** `bdf8ef4` (develop branch)
**Purpose:** Final review before 1.0 public release

---

## 1. Security Delta (New Code Since First Audit)

### PRE-001 — WMI fallback assumes dual-CCD when building core masks
**Category:** Security
**Severity:** High
**File:** `Core/CcdMapper.cs:157`

```csharp
int coresPerCcd = topology.TotalLogicalCores / 2;
```

**Description:** `DetectViaWmi()` hardcodes `TotalLogicalCores / 2` when building the mask for each L3 cache entry. If WMI returns only 1 L3 cache (single-CCD processor), the mask will only cover half the cores. This means `VCacheCores` would list only 8 of 16 threads on a 7800X3D, and 8 threads would have no CCD assignment — `GetCcdIndex()` would return 1 (frequency CCD) for cores not in the mask, but there IS no frequency CCD. The P/Invoke path doesn't have this bug (it gets the exact mask from the OS).

**Impact:** On a single-CCD processor where P/Invoke fails and WMI is used, half the cores would be invisible to the dashboard. Core tiles for the missing cores would show no updates.

**Recommended fix:** Compute `coresPerCcd` from the actual number of L3 caches found:
```csharp
int ccdCount = Math.Max(1, /* count from foreach */);
int coresPerCcd = topology.TotalLogicalCores / ccdCount;
```
Or use `l3Caches.Count` after the loop to compute mask width (requires restructuring the loop).

---

### PRE-002 — SwitchToOptimize/SwitchToMonitor missing single-CCD guard
**Category:** Security
**Severity:** High
**File:** `Core/AffinityManager.cs:151-177, 179-200`

```csharp
public void SwitchToOptimize()
{
    // ... no IsSingleCcd check ...
    if (_engaged && _currentGame != null)
    {
        EngageGame(_currentGame);
        MigrateBackground(_currentGame.Pid);  // Uses FrequencyMask = IntPtr.Zero!
    }
}
```

**Description:** `OnGameDetected` has a single-CCD early return (line 73), but `SwitchToOptimize()` and `SwitchToMonitor()` do not. If `SwitchToOptimize()` is called programmatically while a game is engaged on a single-CCD processor, `MigrateBackground()` would call `SetProcessAffinityMask(handle, IntPtr.Zero)` — setting every background process to a zero affinity mask, starving them of all CPU time.

Currently gated by `IsOptimizeEnabled=false` in the UI, but this is defense-in-depth: the engine should not rely on the UI to prevent unsafe operations.

**Impact:** If the UI gate is bypassed (future code change, scripting, or edge case), all background processes freeze.

**Recommended fix:** Add guard at the top of both methods:
```csharp
public void SwitchToOptimize()
{
    lock (_syncLock)
    {
        if (Mode == OperationMode.Optimize || _topology.IsSingleCcd)
            return;
        // ...
    }
}
```

---

### PRE-003 — SingleCcdX3D tier assigned to non-X3D single-CCD processors
**Category:** Security
**Severity:** Medium
**File:** `Core/CcdMapper.cs:178-189`

```csharp
if (l3Caches.Count == 1)
{
    topology.Tier = ProcessorTier.SingleCcdX3D;
    // ...
}
```

**Description:** Any processor with exactly 1 L3 cache is classified as `SingleCcdX3D`. This includes non-X3D single-CCD processors (Ryzen 7 7700X, 5800X, etc.) which have no V-Cache. The tier name is misleading and `HasVCache` would return false (since `StandardL3SizeMB=0` and `VCacheL3SizeMB > 0 * 2` is true for any nonzero value). The app would label CCD 0 as "V-Cache CCD" even though it has no V-Cache.

**Impact:** Non-X3D single-CCD users see "V-Cache CCD" badge and "Monitor — single V-Cache CCD" status. Cosmetically wrong but functionally harmless (Monitor-only, no affinity changes).

**Recommended fix:** Check L3 size against a V-Cache threshold (e.g., 64MB+ is V-Cache):
```csharp
if (l3Caches.Count == 1)
{
    var entry = l3Caches[0];
    topology.Tier = entry.SizeMB >= 64
        ? ProcessorTier.SingleCcdX3D
        : ProcessorTier.SingleCcdStandard;  // new enum value
}
```
Or simpler: rename the badge to just "CCD 0" for single-CCD when `VCacheL3SizeMB < 64`.

---

### PRE-004 — Duplicate protected process lists
**Category:** Security
**Severity:** Low
**Files:** `Core/AffinityManager.cs:18-24`, `Core/RecoveryManager.cs:91-96`

**Description:** The hardcoded protected process list is duplicated in two locations. If one is updated and the other is not, they diverge — a process could be protected during normal operation but not during recovery (or vice versa).

**Impact:** Inconsistent protection across normal operation and crash recovery paths.

**Recommended fix:** Extract to a shared static readonly set in a common location (e.g., `Models/ProtectedProcesses.cs` or a static property on `AffinityManager`).

---

### PRE-005 — All SEC-001 through SEC-013 fixes verified intact
**Category:** Security
**Severity:** Info
**Files:** Multiple

All 12 fixes from the first security audit are confirmed present and unregressed:
- SEC-001: Atomic file writes — `AppConfig.Save()` line 202, `RecoveryManager.WriteState()` line 267
- SEC-002: Single-instance mutex — `App.xaml.cs` line 42
- SEC-003: Protected process filter in recovery — `RecoveryManager.cs` line 139
- SEC-004: `Lazy<bool>` for VCacheDriverManager — line 23
- SEC-005: Lock-protected GameDetector.CurrentGame — lines 23-27
- SEC-006: Config validation — `AppConfig.Validate()` line 212
- SEC-007: Registry value validation — `VCacheDriverManager.cs` lines 43, 97
- SEC-008: WMI timeouts — `CcdMapper.cs` lines 147, 227
- SEC-009: Volatile `_disposed` in ProcessWatcher — line 17
- SEC-010: `requireAdministrator` in manifest — line 7
- SEC-011: Debug logging in catch blocks — all 5 locations
- SEC-013: Core index bounds check — `CcdMapper.CoresMask()` line 258

---

## 2. Usability / UX

### PRE-006 — Version numbers inconsistent across codebase
**Category:** Release
**Severity:** High
**Files:** `App.xaml.cs:19`, `X3DCcdOptimizer.csproj:10`, `TrayIconManager.cs:134`, `MainViewModel.cs:133`, `README.md`, `CLAUDE.md`, `Blueprint`

**Description:** Multiple version numbers exist:
- `App.xaml.cs`, `.csproj`, `TrayIconManager`, `MainViewModel`: all say `0.2.0`
- `README.md`: says "Pre-1.0"
- `Blueprint`: says `0.4.0`
- `SESSION_LOG.md`: says `0.5.0`

**Impact:** User sees "v0.2.0" in the footer and About dialog, but documentation references different versions. Confusing and unprofessional for a release.

**Recommended fix:** Pick a single version (suggest `1.0.0` for release), update all 4 code references + docs.

---

### PRE-007 — No tooltips on any settings control
**Category:** Usability
**Severity:** Medium
**File:** `Views/SettingsWindow.xaml`

**Description:** None of the settings controls have `ToolTip` properties. A non-technical user encountering "GPU threshold: 50%" or "Detection delay: 5s" or "Pixel shift every: 3 min" has no idea what these mean. The strategy dropdown ("Affinity Pinning" vs "Driver Preference") is particularly opaque.

**Impact:** Users may misconfigure settings without understanding the consequences, or avoid changing settings they don't understand.

**Recommended fix:** Add `ToolTip` to every slider and dropdown with a one-sentence explanation.

---

### PRE-008 — Intel CPU shows developer-oriented error dialog
**Category:** Usability
**Severity:** Medium
**File:** `App.xaml.cs:77-82`

```csharp
MessageBox.Show(
    $"Failed to detect CCD topology:\n\n{ex.Message}\n\n" +
    "This may not be an AMD dual-CCD processor.",
    "X3D CCD Optimizer — Startup Error", ...);
```

**Description:** On Intel CPUs (or single-CCD AMD with P/Invoke + WMI failure), the error message includes the raw exception message which may be technical jargon. The message says "dual-CCD" when the app now supports single-CCD too.

**Impact:** Non-technical users see a confusing error. Intel users see "This may not be an AMD dual-CCD processor" which is correct but could be friendlier.

**Recommended fix:** Update message to: "This application requires an AMD Ryzen processor with identifiable L3 cache topology. It is not compatible with Intel or older AMD processors."

---

### PRE-009 — No first-run onboarding or mode explanation
**Category:** Usability
**Severity:** Low
**Files:** Multiple

**Description:** When a user first launches the app, they see the dashboard in Monitor mode with no explanation of what Monitor vs Optimize means, what the CCD panels represent, or what the green/blue colors signify. The tray icon colors (blue/purple/green) are also unexplained.

**Impact:** New users are confused about what they're looking at and what they should do. Power users figure it out; casual users may uninstall.

**Recommended fix:** Consider a brief tooltip or status bar message on first launch: "Running in Monitor mode — observing your CPU without making changes. Switch to Optimize to pin games to V-Cache." Or add a Help menu item linking to README.

---

## 3. Performance

### PRE-010 — WMI GPU query fetches all engines, filters in C#
**Category:** Performance
**Severity:** Low
**File:** `Core/GpuMonitor.cs:36-38`

```csharp
$"SELECT UtilizationPercentage, Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine"
```

**Description:** Every 2 seconds, this query returns ALL GPU engine entries for ALL processes, then filters by PID and engine type in C#. On systems with many GPU-accelerated processes (50+ browser tabs, Discord, etc.), this returns hundreds of WMI objects. WQL WHERE cannot filter by instance name substring, so this is the standard pattern — but it runs on the polling thread every cycle.

**Impact:** Measurable CPU overhead on systems with many GPU processes. Unlikely to cause frame drops but adds to background CPU usage.

**Recommended fix:** Informational. Consider reducing GPU query frequency to every other poll cycle, or caching results for 4 seconds instead of 2.

---

### PRE-011 — Known games lookup is O(1) via Dictionary
**Category:** Performance
**Severity:** Info
**File:** `Core/GameDetector.cs:18`

**Description:** `_knownGames` is a `Dictionary<string, string>` with case-insensitive comparer. Lookup is O(1). Even with 200+ entries, performance impact is negligible. Manual games use `HashSet<string>` — also O(1). This is correctly implemented.

---

### PRE-012 — All timers properly disposed on shutdown
**Category:** Performance
**Severity:** Info
**File:** `App.xaml.cs:237-241`

**Description:** Verified: `ProcessWatcher.Dispose()` stops and disposes timer. `PerformanceMonitor.Dispose()` stops timer and closes PDH query under lock. `GpuMonitor.Dispose()` sets disposed flag. `OverlayViewModel.StopTimers()` stops both auto-hide and pixel shift timers. `TrayIconManager.Dispose()` unsubscribes events and hides icon. No leaked timers.

---

### PRE-013 — Event subscriptions not unsubscribed on shutdown
**Category:** Performance
**Severity:** Low
**File:** `App.xaml.cs:164-173`

```csharp
_perfMon.SnapshotReady += _mainViewModel.OnSnapshotReady;
_perfMon.SnapshotReady += _overlayViewModel.OnSnapshotReady;
// ... etc ...
```

**Description:** Engine event subscriptions (`SnapshotReady`, `AffinityChanged`, `GameDetected`, `GameExited`) are wired in `OnStartup` but never unwired in `OnExit`. Since the engines are stopped and disposed before the app exits, and the entire process terminates, this has no practical impact. However, during shutdown there's a brief window where a timer callback could fire into a partially-disposed ViewModel.

**Impact:** Theoretical NullReferenceException during shutdown (caught by global handler). No practical consequence.

**Recommended fix:** Low priority. Unwire events before disposing engines in `OnExit`, or add null checks in event handlers.

---

## 4. Edge Cases

### PRE-014 — Intel CPU: useful error shown, app exits cleanly
**Category:** Edge Case
**Severity:** Info

**Description:** Tested path: `CcdMapper.Detect()` → P/Invoke fails (no AMD L3 topology) → WMI fails (no AMD cache layout) → no config override → throws `InvalidOperationException`. `App.OnStartup` catches it, shows MessageBox, calls `Shutdown()`. Verified: clean exit path, no leaked resources (mutex released, no engines started yet).

---

### PRE-015 — VM with no L3 cache info: same as Intel path
**Category:** Edge Case
**Severity:** Info

**Description:** Virtual machines typically don't expose L3 cache topology via `GetLogicalProcessorInformationEx`. Same error path as Intel CPU — graceful shutdown with error dialog.

---

### PRE-016 — Game launches and exits within 1 second
**Category:** Edge Case
**Severity:** Info

**Description:** ProcessWatcher polls every 2 seconds (default). If a game launches and exits between polls, it's never detected. This is by design — sub-second games aren't meaningful optimization targets.

---

### PRE-017 — Game crashes (process disappears)
**Category:** Edge Case
**Severity:** Info

**Description:** `ProcessWatcher.Poll()` lines 67-75: `Process.GetProcessById()` throws `ArgumentException` when PID no longer exists. Caught and treated as game exit. Auto-detected games get exit delay; manual/database games exit immediately. Recovery.json is cleaned up via `AffinityManager.OnGameExited()` → `RecoveryManager.OnDisengage()`.

---

### PRE-018 — Multiple games running simultaneously
**Category:** Edge Case
**Severity:** Info

**Description:** First detected game wins. `OnGameDetected` sets `_engaged=true` and subsequent detections are ignored ("Already engaged — ignoring duplicate game detection"). The second game runs unmanaged. This is intentional and documented behavior.

---

### PRE-019 — known_games.json missing or empty
**Category:** Edge Case
**Severity:** Info

**Description:** `GameDetector.LoadKnownGames()` returns empty dictionary if file missing, empty, or invalid JSON. App continues with manual list and GPU heuristic only. Logged at Warning level.

---

### PRE-020 — Recovery.json from older version (missing `strategy` field)
**Category:** Edge Case
**Severity:** Info

**Description:** `RecoveryState.Strategy` defaults to `"affinityPinning"` if the field is missing in JSON (System.Text.Json default behavior). Backward-compatible.

---

### PRE-021 — User changes strategy in settings while game is engaged
**Category:** Edge Case
**Severity:** Low
**File:** `ViewModels/SettingsViewModel.cs:237`

**Description:** Strategy is written to `_config.OptimizeStrategy` on Apply, but `AffinityManager` stores strategy as a readonly field set at construction. The change takes effect on next app restart, not immediately. This is intentional (per Session 10 design decision) but not communicated to the user.

**Impact:** User changes strategy, expects immediate effect, sees no change until restart.

**Recommended fix:** Show a note in settings: "Strategy changes take effect on next launch."

---

### PRE-022 — System with 64+ logical processors
**Category:** Edge Case
**Severity:** Low
**File:** `Core/PerformanceMonitor.cs:137`

```csharp
string loadPath = $@"\Processor Information(0,{i})\% Processor Utility";
```

**Description:** PDH counter paths hardcode processor group 0. Systems with >64 logical processors use multiple processor groups. Cores in group 1+ would have no load/frequency data. Current consumer AMD X3D processors max out at 32 threads (well under 64), so this doesn't affect the target audience.

**Impact:** None for current hardware. Forward-compatibility note for future Threadripper X3D.

---

## 5. Accessibility

### PRE-023 — No AutomationProperties on any UI element
**Category:** Accessibility
**Severity:** Medium
**Files:** All XAML views

**Description:** No `AutomationProperties.Name` or `AutomationProperties.HelpText` attributes on any interactive element (buttons, toggles, sliders, checkboxes). Screen readers (Narrator, NVDA, JAWS) cannot describe the purpose of controls.

**Impact:** Blind or low-vision users cannot use the app. This affects a small percentage of gamers but is a basic accessibility standard.

**Recommended fix:** Add `AutomationProperties.Name` to all interactive elements. Priority: mode toggle, settings controls, tray menu items.

---

### PRE-024 — No high contrast theme
**Category:** Accessibility
**Severity:** Low
**Files:** `Themes/DarkTheme.xaml`

**Description:** The app uses a custom dark theme exclusively. Windows High Contrast mode is not detected or respected. The `SystemColors` resource keys are not used. Users who need high contrast for visibility get the standard dark theme regardless.

**Impact:** High contrast mode users may find the UI difficult to read.

**Recommended fix:** Low priority for gaming audience. Consider detecting `SystemParameters.HighContrast` and switching to system colors in a future version.

---

### PRE-025 — Keyboard navigation works for standard controls but no AccessKeys defined
**Category:** Accessibility
**Severity:** Low
**Files:** All XAML views

**Description:** Tab navigation works for standard WPF controls (buttons, checkboxes, sliders). However, no `AccessKey` (Alt+letter underline) is defined for any button or menu item. The mode toggle (ToggleButton) is keyboard-accessible via Tab+Space. Settings tabs are keyboard-navigable.

**Impact:** Power keyboard users can navigate but lack accelerator shortcuts.

---

## 6. Release Readiness

### PRE-026 — SECURITY_AUDIT.md tracked in git, ships with build
**Category:** Release
**Severity:** Low
**File:** `SECURITY_AUDIT.md`

**Description:** `SECURITY_AUDIT.md` and this file (`PRE_RELEASE_AUDIT.md`) are in the repo root. They won't be included in the compiled output (they're not in `src/`), but they ARE tracked in git and will be visible on GitHub. The security audit contains detailed vulnerability information.

**Impact:** Anyone reading the repo can see exact vulnerability patterns and attack scenarios. This is acceptable for open-source (security through transparency), but worth noting.

**Recommended fix:** Informational. The fixes are already applied, so the audit is historical documentation, not a live vulnerability list.

---

### PRE-027 — Debug code: no Console.WriteLine or hardcoded test values found
**Category:** Release
**Severity:** Info

**Description:** Grep for `Console.Write`, `#if DEBUG`, `TODO`, `FIXME`, `HACK` across all source files:
- No `Console.WriteLine` found
- No `#if DEBUG` blocks
- No `TODO`, `FIXME`, or `HACK` comments
- No hardcoded test values or bypass flags

---

### PRE-028 — NuGet packages pinned to exact versions
**Category:** Release
**Severity:** Info
**File:** `X3DCcdOptimizer.csproj:31-34`

```xml
<PackageReference Include="Serilog" Version="4.2.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
<PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
<PackageReference Include="System.Management" Version="8.0.0" />
```

All 4 packages pinned to exact versions. No floating ranges. Reproducible builds.

---

### PRE-029 — License file present and correct
**Category:** Release
**Severity:** Info

**Description:** `LICENSE` contains complete GPL v2 text (339 lines). Properly formatted.

---

### PRE-030 — .gitignore clean
**Category:** Release
**Severity:** Info

**Description:** `.gitignore` covers .NET build artifacts, IDE files, NuGet packages, publish output, OS junk, and build prompt files. No audit files excluded (they're tracked intentionally). No `*.exe`, `.env`, or credential patterns missing.

---

## Executive Summary

### Findings by Category

| Category | Critical | High | Medium | Low | Info |
|----------|----------|------|--------|-----|------|
| Security | 0 | 2 | 1 | 1 | 1 |
| Usability | 0 | 1 | 2 | 1 | 0 |
| Performance | 0 | 0 | 0 | 2 | 2 |
| Edge Case | 0 | 0 | 0 | 2 | 7 |
| Accessibility | 0 | 0 | 1 | 2 | 0 |
| Release | 0 | 0 | 0 | 1 | 3 |
| **Total** | **0** | **3** | **4** | **9** | **13** |

### Go/No-Go Recommendation

**CONDITIONAL GO** — Release after fixing the 3 High-severity items.

The app has zero critical vulnerabilities, solid security posture (all 12 SEC fixes intact), and handles edge cases gracefully. The High items are: a defense-in-depth gap that could be triggered by future code changes (PRE-002), a WMI fallback bug affecting rare single-CCD-via-WMI scenarios (PRE-001), and version number chaos (PRE-006). None are exploitable by end users today, but PRE-002 is a time bomb.

### Top 5 Must-Fix Before Release

1. **PRE-002** (High) — Add `IsSingleCcd` guard to `SwitchToOptimize()`/`SwitchToMonitor()`. Prevents zero-mask affinity catastrophe.
2. **PRE-006** (High) — Unify version numbers to `1.0.0` across all 4 code references + docs.
3. **PRE-001** (High) — Fix WMI fallback core mask calculation for single-CCD processors.
4. **PRE-003** (Medium) — Don't label non-X3D single-CCD as "V-Cache CCD". Use cache size threshold or add `SingleCcdStandard` tier.
5. **PRE-008** (Medium) — Improve error message for non-AMD CPUs.

### Top 5 Nice-to-Have for 1.1

1. **PRE-007** (Medium) — Add tooltips to all settings controls.
2. **PRE-023** (Medium) — Add `AutomationProperties.Name` to interactive UI elements.
3. **PRE-009** (Low) — First-run onboarding or mode explanation.
4. **PRE-021** (Low) — Note in settings that strategy changes require restart.
5. **PRE-010** (Low) — Reduce GPU query frequency when idle.
