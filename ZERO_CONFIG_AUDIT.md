# Zero-Config User Journey Audit

**Date:** 2026-03-27
**Auditor:** Claude Opus 4.6 (1M context)
**Scope:** Trace the complete zero-config path through every code file, citing exact lines.

---

## Journey 1: Fresh Install, First Launch

**Scenario:** No config.json exists. User double-clicks exe on a 7950X3D.

### Step-by-step through App.OnStartup (`App.xaml.cs`):

1. **Line 42:** Creates `Global\X3DCcdOptimizer_SingleInstance` mutex. First launch, so `createdNew=true`.
2. **Lines 52-61:** Global exception handlers registered.
3. **Line 63:** `AppConfig.Load()` — no config.json at `%APPDATA%\X3DCCDOptimizer\config.json` (`AppConfig.cs:177` — `File.Exists` returns false). Falls through to line 191: `CreateDefault()` creates new `AppConfig` with all defaults. **Line 192:** `IsFirstRun = true` is set. **Line 193:** `Save()` writes the new config to disk.
4. **Line 64:** `Validate()` clamps all values — all defaults are within range, no changes.
5. **Line 65:** Logger initialized at "Information" level (default).
6. **Line 69:** `RecoveryManager.RecoverFromDirtyShutdown()` — no recovery.json exists, returns immediately (`RecoveryManager.cs:37`).
7. **Line 73:** `CcdMapper.Detect()` — P/Invoke detects 2 L3 caches (96MB + 32MB). `AssignTopologyFromCaches` (`CcdMapper.cs:180`) classifies as `DualCcdX3D` (96 > 32*2). Topology fully populated.
8. **Line 90:** `GetOperationMode()` returns `Monitor` (default `"monitor"` in config, `AppConfig.cs:104`).
9. **Lines 93-97:** Single-CCD check — false for 7950X3D. No mode override.
10. **Line 104:** `GetOptimizeStrategy()` returns `AffinityPinning` (default `"affinityPinning"`, `AppConfig.cs:107`).
11. **Lines 105-113:** Driver checks — VCacheDriverManager.IsDriverAvailable may or may not be true depending on chipset drivers. If false, no change (already AffinityPinning).
12. **Lines 117-125:** Engine components created: PerformanceMonitor (1000ms), GpuMonitor, GameDetector (6 manual + 65 known = 71 games), AffinityManager (Monitor mode, AffinityPinning), ProcessWatcher (2000ms polling, GPU threshold 50%, detection delay 5s, exit delay 10s).
13. **Lines 128-130:** ViewModels created.
14. **Line 133:** DashboardWindow created.
15. **Line 164-165:** `MainViewModel` constructor: `IsOptimizeEnabled = true` (IsDualCcd), `ShowSecondPanel = true`.
16. **Line 164:** First-run check: `config.IsFirstRun && topology.IsDualCcd` → **true**. StatusText set to: **"Monitor mode — observing your CPU without making changes. Switch to Optimize to pin games to V-Cache."**
17. **Lines 180-181:** Engines start. ProcessWatcher begins polling every 2s. PerformanceMonitor begins collecting every 1s.
18. **Lines 191-192:** `StartMinimized` is false (default). Dashboard window shown.

### What the user sees:

- Dashboard appears (860x740, dark theme)
- Status bar shows blue background with: **"Monitor mode — observing your CPU without making changes. Switch to Optimize to pin games to V-Cache."**
- Two CCD panels (CCD0 "V-Cache" green badge, CCD1 "Frequency" blue badge) with live core heatmaps
- Empty process router with "No managed processes" placeholder
- Empty activity log
- Footer: "v1.0.0 | AMD Ryzen 9 7950X3D | 16 cores | 32 threads | Polling: 2000ms"
- System tray: blue circle icon, tooltip "Monitor mode — observing..."
- Mode toggle shows "Monitor" (left position), enabled for toggling

**Verdict: PASS.** First-run experience is clear and informative. The onboarding message explains what's happening and what to do next.

---

## Journey 2: Known Game Launches (Cyberpunk 2077)

**Scenario:** User starts Cyberpunk 2077 (`Cyberpunk2077.exe` is in `known_games.json` line 12).

### Detection path:

1. **ProcessWatcher.Poll()** runs every 2000ms (`ProcessWatcher.cs:59`).
2. **Line 146:** Iterates `Process.GetProcesses()`.
3. **Line 151:** `_detector.CheckGame("Cyberpunk2077")` — checks manual list first (`GameDetector.cs:46`), not found. Checks known DB (`GameDetector.cs:50`): `_knownGames.ContainsKey("Cyberpunk2077.exe")` → **match**. Returns `DetectionMethod.Database`.
4. **Line 155:** Foreground check — default `_requireForeground=true`. Process must be the foreground window. If Cyberpunk is focused, passes. If still loading (splash screen not focused), skipped until next poll.
5. **Lines 165-171:** `ProcessInfo` created with `DetectionSource = "[database]"`.
6. **Line 177:** `GameDetected` event fired.

**Detection speed:** Next poll after Cyberpunk becomes foreground. Worst case 2 seconds (one poll interval). No debounce for manual/database games — immediate on detection.

### In Monitor mode (default):

7. **AffinityManager.OnGameDetected** (`AffinityManager.cs:58`): `_engaged = true`.
8. **Line 73:** Single-CCD check — false. Continues.
9. **Line 86-96:** Mode is Monitor → calls `SimulateEngage()` and `SimulateMigrateBackground()`.
10. **SimulateEngage** (`AffinityManager.cs:343`): Emits `WouldEngage` event: `"→ CCD0 (V-Cache, mask 0x00FF)"`.
11. **SimulateMigrateBackground** (`AffinityManager.cs:349-398`): Emits up to 5 individual `WouldMigrate` events + summary.

### Activity log shows:
```
[HH:mm:ss] [MONITOR] WOULD ENGAGE    Cyberpunk2077.exe → CCD0 (V-Cache, mask 0x00FF)
[HH:mm:ss] [MONITOR] WOULD MIGRATE   discord.exe → CCD1 (Frequency, mask 0xFF00)
[HH:mm:ss] [MONITOR] WOULD MIGRATE   chrome.exe → CCD1 (Frequency, mask 0xFF00)
... (up to 5 individual + summary)
```

All in italic, 0.7 opacity (simulated actions).

### In Optimize mode (if user toggles):

Same detection, but AffinityManager calls `EngageGame()` (`AffinityManager.cs:204`) and `MigrateBackground()` (`AffinityManager.cs:250`). Activity log shows `ENGAGE` and `MIGRATE` in green, full opacity.

**Verdict: PASS.** Detection is fast, behavior is transparent, activity log clearly shows what happened.

---

## Journey 3: Unknown Game Launches

**Scenario:** User starts an indie game not in known_games.json.

### GPU heuristic path:

1. **ProcessWatcher.Poll()** line 146: Scans all processes. `CheckGame()` returns null for the indie game.
2. **Lines 188-192:** Falls through to GPU auto-detection. `_autoDetectEnabled` is true (default), `_gpuMonitor != null`, and `foregroundPid > 0`.
3. **TryAutoDetect** (`ProcessWatcher.cs:201`): Opens the foreground process.
4. **Line 207:** Checks `_detector.IsExcluded()` — if the game name isn't in the exclusion list, continues.
5. **Line 214:** `_gpuMonitor.GetGpuUsage(foregroundPid)` queries WMI.

### If GPU usage >= 50% (default threshold):

6. **Line 221-227:** First poll: `_autoDetectCandidatePid != foregroundPid`, so starts tracking. Stores PID and timestamp. Returns — no detection yet.
7. Next polls: candidate PID matches, checks elapsed time (`line 230`). Must exceed `_detectionDelaySec` (default 5 seconds).
8. After 5+ seconds of continuous GPU >= 50% while foreground: **Lines 234-247:** Creates `ProcessInfo` with `DetectionSource = "[auto-detected, GPU: XX%]"`. Fires `GameDetected`.

**Total detection time:** 5 seconds (detection delay) + up to 2 seconds (poll interval alignment) = **5-7 seconds**.

### If GPU usage < 50%:

**Line 216:** `ResetAutoDetectState()` called. Game is never detected via GPU heuristic. Falls back to: not detected at all unless user adds it to manual list via Settings.

### Gap identified:
**ZC-001:** There's no feedback to the user that a foreground application was considered and rejected (GPU below threshold). A user playing a low-GPU indie game might wonder why it's not being detected. The activity log shows nothing.

**Verdict: MOSTLY PASS.** GPU heuristic works correctly for GPU-heavy games. Low-GPU games require manual addition. No user feedback about rejection is a minor gap.

---

## Journey 4: No AMD Chipset Drivers

**Scenario:** `amd3dvcache` service not installed.

### At startup:

1. **App.xaml.cs:105:** `VCacheDriverManager.IsDriverAvailable` is checked. `CheckDriverInstalled()` (`VCacheDriverManager.cs:81-92`) tries `Registry.LocalMachine.OpenSubKey(RegKeyPath)`. Key doesn't exist → returns false. Cached via `Lazy<bool>`.
2. If config says `"driverPreference"`: **line 107** logs warning and falls back to `AffinityPinning`. No crash, no error dialog.

### In settings:

3. **SettingsViewModel.cs:49:** `IsDriverAvailable` returns `false` (VCacheDriverManager.IsDriverAvailable is false).
4. **SettingsViewModel.cs:48:** `IsStrategyAvailable` returns `true` (topology is DualCcd). The strategy ComboBox is enabled.
5. **SettingsWindow.xaml:46:** The "Driver Preference" ComboBoxItem has `IsEnabled="{Binding IsDriverAvailable}"` — it's **grayed out** within the dropdown.
6. **SettingsWindow.xaml:50-52:** Warning message visible: "AMD V-Cache driver not detected — Driver Preference unavailable" in amber text.

**Verdict: PASS.** Dropdown is enabled (user can see both options), but the Driver Preference item is individually disabled. Warning message is clear and visible.

---

## Journey 5: High Performance Power Plan

**Scenario:** User is on High Performance instead of Balanced.

### Code search for power plan detection:

```
Grep for: PowerPlan, power plan, Balanced, High Performance, powercfg, GUID
Result: No matches in any .cs file.
```

**The app does not detect or warn about power plan settings.** It has no power plan awareness at all.

### Impact:

- **Affinity Pinning:** Fully works regardless of power plan (`App.xaml.cs:99` — no power plan check). `SetProcessAffinityMask` is an OS-level API unrelated to power management.
- **Driver Preference:** May be less effective. The amd3dvcache driver interacts with core parking, which High Performance disables. The driver's PREFER_CACHE setting still takes effect at the scheduler level, but without core parking the frequency CCD cores don't get parked — they just have lower priority for new thread scheduling.

### Does the README mention this?

**Yes.** The Windows Settings Compatibility table shows: Power Plan → "Any" for Affinity Pinning, "Balanced recommended" for Driver Preference. The Driver Preference requirements section also notes BIOS CPPC should be set to Driver.

### Gap identified:
**ZC-002:** No runtime warning when Driver Preference strategy is active and power plan is not Balanced. The README documents it, but the app itself is silent. Power plan detection would require WMI query `Win32_PowerPlan` — straightforward but not implemented.

**Verdict: PARTIAL PASS.** Affinity Pinning works fine. Driver Preference works but may be less effective. README documents it correctly. No in-app warning.

---

## Journey 6: Game Bar Disabled

**Scenario:** User has Xbox Game Bar disabled or uninstalled.

### Code search for Game Bar dependency:

```
Grep for: Game.?Bar, GameBar, game.?bar, xbox, Xbox, KGL
Result: No matches in any .cs file.
```

**Zero code dependency on Xbox Game Bar.** Game detection is entirely self-contained:
- Manual list (`GameDetector.cs:46`)
- Known games database (`GameDetector.cs:50`)
- GPU heuristic via WMI (`GpuMonitor.cs:36-38`, `ProcessWatcher.cs:201-251`)

### Does the README make this clear?

**Yes.** The Windows Settings Compatibility table shows "Xbox Game Bar: Not needed" for both strategies. The "Why This Tool Exists" section explains that Game Bar is part of AMD's chain that this tool replaces. The Affinity Pinning pros list explicitly states "No Xbox Game Bar dependency."

**Verdict: PASS.** No Game Bar dependency anywhere. README is clear about this.

---

## Journey 7: No GPU (Integrated Only or Missing Counters)

**Scenario:** System has no discrete GPU, or GPU perf counters are unavailable.

### At startup:

1. **App.xaml.cs:118:** `new GpuMonitor()` is created.
2. **GpuMonitor constructor** (`GpuMonitor.cs:15-22`): Calls `TestGpuCounters()`.
3. **TestGpuCounters** (`GpuMonitor.cs:74-96`): Queries `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine`. If the WMI class doesn't exist or returns no results → returns false.
4. **Line 21:** Logs `"GPU performance counters unavailable — auto-detection disabled"` at Warning level.
5. `_available` set to false.

### Impact on game detection:

6. **ProcessWatcher constructor** (`ProcessWatcher.cs:36`): `_autoDetectEnabled = autoDetectEnabled && gpuMonitor?.IsAvailable == true`. Since `IsAvailable` is false, `_autoDetectEnabled` is set to **false**.
7. **ProcessWatcher.Poll line 188:** `_autoDetectEnabled` check — false, so GPU heuristic is never attempted.
8. **Manual list and known DB still work** — `ProcessWatcher.cs:146-186` iterates processes and checks against `_detector.CheckGame()`. This path has no GPU dependency.

### Does the app crash?

**No.** `GetGpuUsage()` (`GpuMonitor.cs:30`) checks `!_available` first and returns 0 immediately. All GPU code paths are guarded.

**Verdict: PASS.** Graceful degradation. GPU auto-detection disabled, manual + known DB detection fully functional. Warning logged.

---

## Journey 8: Intel CPU

**Scenario:** User runs the app on an Intel i9-14900K.

### At startup:

1. **App.xaml.cs:73:** `CcdMapper.Detect(_config)` called.
2. **CcdMapper.DetectViaPInvoke** (`CcdMapper.cs:63-97`): `GetLogicalProcessorInformationEx` with `RelationCache` — returns L3 cache data. Intel has a single shared L3 cache (typically 1 entry). `ParseL3Caches` returns 1 entry.
3. **AssignTopologyFromCaches** (`CcdMapper.cs:182-193`): `l3Caches.Count == 1`. Sets tier to `SingleCcdStandard` (Intel L3 is typically 24-36MB, well under 64MB threshold at line 186).
4. **ValidateTopology** (`CcdMapper.cs:266`): `VCacheCores` has entries (all Intel cores mapped to the single L3). Passes validation.
5. Returns successfully — **does NOT throw**.

### Wait — this means Intel CPUs don't hit the error dialog?

**Correct.** The current code would classify an Intel CPU as `SingleCcdStandard` and show the dashboard in Monitor mode with a single CCD panel labeled "CCD 0". This isn't wrong per se — the monitoring features (per-core heatmap, overlay) actually work on Intel via PDH counters. But the app name and branding ("X3D Dual CCD Optimizer") would be confusing.

### When DOES the error dialog show?

Only when `CcdMapper.Detect()` **throws** — which happens when:
- P/Invoke fails AND WMI fails AND no config override exists
- `ValidateTopology` fails (VCacheCores is empty)

On modern Intel CPUs with L3 cache data available, none of these failures occur.

### Gap identified:
**ZC-003:** Intel CPUs with detectable L3 caches silently pass through as `SingleCcdStandard`. The user sees a functioning app with "CCD 0" label and "Monitor — single CCD" status. While technically harmless (Monitor mode, no affinity changes), it's misleading for an AMD-branded tool. The improved error message from PRE-008 (`App.xaml.cs:78-82`) is only shown when detection throws an exception, not when it succeeds on a non-AMD CPU.

Consider adding an AMD CPU model name check after topology detection (grep CPU model for "AMD" or "Ryzen") and showing a warning or exiting if not found.

**Verdict: FAIL for Intel-specific experience.** The app doesn't crash, but it also doesn't tell the Intel user they're on the wrong platform. It just works in a reduced-feature mode with confusing branding.

---

## Summary of Gaps

| ID | Journey | Severity | Description |
|----|---------|----------|-------------|
| ZC-001 | 3 | Low | No user feedback when a foreground app is rejected by GPU heuristic (below threshold) |
| ZC-002 | 5 | Low | No runtime warning when Driver Preference is active on non-Balanced power plan |
| ZC-003 | 8 | Medium | Intel CPUs with L3 cache data pass through as SingleCcdStandard instead of showing error or warning |

### Recommendations:

**ZC-003 (Medium):** Add an AMD CPU check after topology detection. `CpuTopology.CpuModel` is populated from WMI (`CcdMapper.GetCpuInfo()` line 215). Check if it contains "AMD" or "Ryzen". If not, either show a warning dialog or exit with the PRE-008 error message.

**ZC-001 (Low):** Consider adding a subtle log entry when a foreground process exceeds some GPU usage but is below threshold, e.g., "Foreground process {name} using {gpu}% GPU (below {threshold}% threshold)".

**ZC-002 (Low):** Future enhancement. Requires WMI power plan detection which adds complexity for a documentation-covered edge case.
