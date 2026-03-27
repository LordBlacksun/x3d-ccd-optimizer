# X3D Dual CCD Optimizer — Project Blueprint

**Version:** 0.4.0
**Author:** LordBlacksun
**License:** GPL v2
**Repository:** github.com/LordBlacksun/x3d-ccd-optimizer
**Status:** Phase 2.5 complete

---

## 1. Problem Statement

AMD Ryzen dual-CCD X3D processors (7950X3D, 7900X3D, 9950X3D, 9900X3D) feature an asymmetric architecture: one CCD carries 3D V-Cache (larger L3, better for gaming), while the other runs at higher clock speeds (better for general compute). Optimal performance requires routing game workloads to the V-Cache CCD and background tasks to the frequency CCD.

AMD's official solution on Windows is a fragile chain: Xbox Game Bar must detect a foreground game, the PPM Provisioning File driver tells Windows to park the non-V-Cache CCD, and the game runs on the correct cores. This mechanism:

- Fails silently when Game Bar doesn't recognize a game
- Breaks intermittently across chipset driver updates
- Parks CCD1 instead of routing background tasks to it (wasted cores)
- Has no user-facing controls or diagnostic feedback
- Has never been meaningfully improved since the 7950X3D launch in 2023

On Linux, AMD contributed a proper kernel-level driver (`amd_x3d_vcache`, kernel 6.13+) that exposes a scheduling bias toggle and integrates directly with the kernel scheduler. The community built `x3d-toggle` on top of it with automatic workload detection. No equivalent exists for Windows.

Beyond the affinity problem, there is no Windows tool that simply lets you *see* what your dual-CCD processor is doing — which cores are active, which CCD a game is running on, whether AMD's built-in parking is even working. Users have to cross-reference Task Manager, HWiNFO, and Process Lasso to piece together a picture of their CCD behaviour. A real-time visual dashboard that makes CCD activity transparent would be valuable even if it never touched a single process affinity.

**X3D Dual CCD Optimizer fills both gaps** — it provides a real-time visual dashboard that makes CCD activity transparent, AND optionally manages process affinity to route games to the V-Cache CCD.

---

## 2. Project Vision

A lightweight, open-source Windows application that operates in two modes:

- **Monitor Mode (default):** A real-time visual dashboard showing exactly what each CCD and core is doing — per-core load, frequency, which processes are on which CCD, and game detection. It shows what the optimizer *would* do without touching anything. Works on any dual-CCD Ryzen processor, not just X3D. This is the safe, zero-risk entry point.

- **Optimize Mode (user-enabled):** Everything Monitor does, plus active CPU affinity management. When a game is detected, it pins the game to the V-Cache CCD and migrates background processes to the frequency CCD. When the game closes, all constraints are released. Requires confirmed V-Cache detection to enable.

Users start in Monitor mode, observe the tool's behaviour, build trust, and enable Optimize mode when ready. Every action — real or simulated — is visible in the dashboard.

### Design Principles

1. **Observe first, optimize later** — Monitor mode is the default. Users see exactly what the tool would do before it does anything. No process is touched until the user explicitly enables Optimize mode.
2. **Transparency first** — The dashboard is the product. Users should see exactly what their CPU is doing, which CCD is handling what, and every action the optimizer takes or would take. No black box.
3. **Zero-configuration by default** — Auto-detects CCD topology, auto-detects games via GPU heuristics and a 65-game known database. Works out of the box.
4. **Manual override always available** — User-defined game list as fallback. Manual always wins over auto.
5. **Minimal footprint** — System tray when minimized. Dashboard on demand. Near-zero CPU/RAM when idle.
6. **No kernel access required** — Userspace only. Uses standard Windows APIs. No driver signing needed.
7. **Open source from day one** — GPL v2, public repo, community contributions welcome.

---

## 3. Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                    WPF Dashboard Window                           │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Status Bar  [Monitor ◉ / ○ Optimize]  CPU Model  Session  │  │
│  │  Pulsing status dot  |  Animated pill toggle  |  Timer      │  │
│  └────────────────────────────────────────────────────────────┘  │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────────┐ │
│  │  CCD0 Panel  │ │  CCD1 Panel  │ │   Process Router         │ │
│  │  (per-core   │ │  (per-core   │ │   (which process         │ │
│  │   load/freq  │ │   load/freq  │ │    on which CCD)         │ │
│  │   heatmap)   │ │   heatmap)   │ │                          │ │
│  │  V-Cache or  │ │  Frequency   │ │                          │ │
│  │  Freq badge  │ │  badge       │ │                          │ │
│  └──────┬───────┘ └──────┬───────┘ └──────────┬───────────────┘ │
│         │                │                     │                  │
│  ┌──────▼────────────────▼─────────────────────▼──────────────┐ │
│  │                   Activity Log                              │ │
│  │  (timestamped feed — real or simulated actions)             │ │
│  │  (alternating rows, auto-scroll, italic for Monitor)        │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                   │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Footer — gradient separator, version, overlay toggle       │  │
│  └────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬───────────────────────────────────┘
                                │
┌───────────────────────────────▼───────────────────────────────────┐
│                   Compact Overlay (OLED-safe)                      │
│  280x160, transparent, topmost, draggable, auto-hide              │
│  Mode dot + CCD load bars + last action + game name               │
│  Pixel shift every 3min, Ctrl+Shift+O hotkey                      │
└───────────────────────────────┬───────────────────────────────────┘
                                │
┌───────────────────────────────▼───────────────────────────────────┐
│              System Tray (WinForms NotifyIcon)                     │
│   Colored circles: blue=Monitor, purple=Optimize idle, green=active│
│   Context menu: mode toggle, overlay toggle, dashboard, log, exit  │
└───────────────────────────────┬───────────────────────────────────┘
                                │
┌───────────────────────────────▼───────────────────────────────────┐
│                       Core Engine                                  │
│                                                                    │
│  ┌──────────────┐  ┌────────────────────────────────────────────┐ │
│  │  CCD Mapper  │  │         Process Watcher                    │ │
│  │  (topology   │  │  (polling loop, game detection)            │ │
│  │   detection) │  │  (mode-independent)                        │ │
│  └──────┬───────┘  └───────────────┬────────────────────────────┘ │
│         │                          │                               │
│  ┌──────▼──────────────────────────▼─────────────────────────────┐│
│  │            Affinity Manager (MODE-AWARE, LOCK-BASED)          ││
│  │  Monitor:  emits Would* events, never calls Win32 API         ││
│  │  Optimize: emits real events, calls SetProcessAffinityMask    ││
│  │  Thread-safe via _syncLock for mid-game mode switches         ││
│  └───────────────────────────────────────────────────────────────┘│
│                                                                    │
│  ┌───────────────────────────────────────────────────────────────┐│
│  │              Game Detector (3-tier, mode-independent)         ││
│  │  1. Manual list (config, highest priority)                    ││
│  │  2. Known games database (65-game JSON, community-maintained) ││
│  │  3. GPU heuristic (WMI, 5s debounce, 10s exit delay)         ││
│  │  (manual always overrides auto)                               ││
│  └───────────────────────────────────────────────────────────────┘│
│                                                                    │
│  ┌───────────────────────────────────────────────────────────────┐│
│  │         GPU Monitor (WMI-based auto-detection)                ││
│  │  Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine     ││
│  │  Per-process 3D engine utilization via WMI                    ││
│  │  Tests counter availability at startup                        ││
│  └───────────────────────────────────────────────────────────────┘│
│                                                                    │
│  ┌───────────────────────────────────────────────────────────────┐│
│  │         Performance Monitor (mode-independent)                ││
│  │  PDH counters: per-core load % and frequency MHz              ││
│  │  Timer-driven collection, configurable interval               ││
│  │  Feeds dashboard + overlay via SnapshotReady event            ││
│  └───────────────────────────────────────────────────────────────┘│
└───────────────────────────────┬───────────────────────────────────┘
                                │
┌───────────────────────────────▼───────────────────────────────────┐
│                      Configuration                                 │
│   JSON config v3 at %APPDATA%\X3DCCDOptimizer\config.json          │
│   Includes overlay, auto-detection with debounce, operation mode   │
└────────────────────────────────────────────────────────────────────┘
```

---

### 3.1 Operating Modes

The application operates in one of two modes at any time. Mode is user-selectable and persists across restarts.

#### Monitor Mode (Default)

Monitor mode is the safe, observation-only mode. It is the default on first launch and requires no special hardware.

**What runs:**
- CCD topology detection — identifies CCDs, cores, L3 sizes, V-Cache presence
- Performance monitoring — per-core load, frequency via PDH counters
- Process tracking — which processes are running on which CCD
- Game detection — identifies game launches via manual list, known games DB, and GPU auto-detection

**What does NOT run:**
- `SetProcessAffinityMask` — never called. No process affinity is modified.

**Activity log output:**
- `[MONITOR] WOULD ENGAGE: elitedangerous64.exe -> CCD0 (V-Cache)`
- `[MONITOR] WOULD MIGRATE: discord.exe -> CCD1 (Frequency)`
- `[MONITOR] WOULD RESTORE: discord.exe -> all cores`

**Use cases:**
- First-time users observing before enabling control
- Diagnosing whether AMD's built-in CCD parking is working correctly
- Users on non-X3D dual-CCD Ryzen processors (e.g., 7950X, 9950X) who want CCD visibility
- Anticheat-sensitive games where touching affinity is risky

**Compatibility:** Any dual-CCD AMD Ryzen processor. Single-CCD processors still show per-core metrics but CCD-specific features are limited.

#### Optimize Mode (User-Enabled)

Optimize mode adds active affinity management on top of everything Monitor does.

**Requirements to enable:**
- V-Cache CCD confirmed during topology detection (`HasVCache == true`)
- User explicitly toggles from Monitor to Optimize

**What it adds beyond Monitor:**
- `SetProcessAffinityMask` calls to pin games to V-Cache CCD
- Background process migration to Frequency CCD
- Automatic affinity restoration on game exit

**Activity log output:**
- `ENGAGE: elitedangerous64.exe -> CCD0 (V-Cache)`
- `MIGRATE: discord.exe -> CCD1 (Frequency)`
- `RESTORE: discord.exe -> all cores`

#### Mode Switching

**Toggle location:** Dashboard status bar (animated pill toggle), system tray context menu, and overlay right-click menu.

**Monitor -> Optimize (while game is running):**
1. Mode flag changes immediately
2. AffinityManager detects active game session
3. Immediately engages: pins game to V-Cache, migrates background processes
4. Activity log shows real ENGAGE/MIGRATE entries from this point

**Optimize -> Monitor (while game is running):**
1. Mode flag changes immediately
2. AffinityManager calls RestoreAll — all modified affinities return to original values
3. Activity log shows RESTORE entries, then switches to [MONITOR] WOULD* entries
4. Game continues running, unaffected, on whatever cores Windows assigns

**Mode persistence:** Saved to `config.json` as `"operationMode": "monitor"` or `"operationMode": "optimize"`. Restored on next launch.

**Optimize toggle disabled when:** `HasVCache == false` (non-X3D processor). Toggle is greyed out at 40% opacity with tooltip: "Optimize mode requires a V-Cache processor."

---

## 4. Module Breakdown

### 4.1 CCD Mapper (`Core/CcdMapper.cs`)

**Purpose:** Detect CPU topology at startup. Identify which cores belong to which CCD, and which CCD has V-Cache.

**How:**
- Use `GetLogicalProcessorInformationEx` (P/Invoke) to enumerate processor groups, cores, and cache topology
- Query L3 cache sizes per CCD via `SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX` structures
- Parse `CACHE_RELATIONSHIP` structures manually via pointer arithmetic, accounting for the 18-byte `Reserved` padding field (a bug in the original implementation used an incorrect padding size, causing misaligned reads)
- Identify V-Cache CCD by comparing L3 sizes (V-Cache CCD = 96MB L3, standard CCD = 32MB)
- WMI fallback: `Win32_Processor` for CPU model and `NumberOfCores`, `Win32_CacheMemory` (Level 5 = L3) for cache topology with estimated core masks
- Fallback: manual CCD specification in config via `ccdOverride` for edge cases
- Output: `CpuTopology` object containing:
  - `VCacheMask` (IntPtr) — affinity bitmask for V-Cache cores
  - `FrequencyMask` (IntPtr) — affinity bitmask for standard cores
  - `VCacheCores` (int[]) — list of V-Cache core indices
  - `FrequencyCores` (int[]) — list of standard core indices
  - `CpuModel` (string) — e.g., "AMD Ryzen 9 7950X3D"
  - `VCacheL3SizeMB` (int) — L3 size in MB for V-Cache CCD
  - `StandardL3SizeMB` (int) — L3 size in MB for standard CCD
  - `TotalPhysicalCores` (int) — from WMI `NumberOfCores`
  - `TotalLogicalCores` (int) — from `Environment.ProcessorCount`
  - `HasVCache` (bool, computed) — `true` if `VCacheL3SizeMB > StandardL3SizeMB * 2`. Used to gate the Optimize mode toggle.

**Supported processors:**

| Mode | Processors |
|------|-----------|
| Monitor + Optimize | Ryzen 9 7950X3D, 7900X3D, 9950X3D, 9900X3D (dual-CCD X3D) |
| Monitor only | Any dual-CCD Ryzen (7950X, 9950X, etc.) — `HasVCache = false` |
| Limited (per-core only) | Single-CCD Ryzen — no CCD routing features |

**Startup behaviour:**
- Dual-CCD X3D detected: full functionality, both modes available
- Dual-CCD non-X3D detected: Monitor mode available, Optimize toggle disabled with explanation
- Single-CCD detected: per-core metrics only, CCD-specific features hidden
- No AMD processor detected / topology detection fails: error dialog and exit

### 4.2 Performance Monitor (`Core/PerformanceMonitor.cs`)

**Purpose:** Collect real-time per-core CPU metrics for the dashboard and overlay.

**Mode-independent:** Runs identically in Monitor and Optimize modes. Performance data collection has no dependency on the operating mode.

**Metrics collected (per core):**
- Load percentage (0-100%)
- Current frequency (MHz)
- Temperature (reserved field, null when unavailable)

**How:**
- PDH (Performance Data Helper) counters via P/Invoke
  - `\Processor Information(0,N)\% Processor Utility` for per-core load (fallback: `% Processor Time`)
  - `\Processor Information(0,N)\Processor Frequency` for per-core MHz
- Uses `PdhAddEnglishCounter` for locale-independent counter names
- Initial baseline collection on construction (first real snapshot discarded to allow PDH baseline)
- Thread-safe via `_disposeLock` object for safe disposal during background collection
- Refresh rate: configurable, default 1000ms (1 Hz)

**Output:** `CoreSnapshot[]` array, one entry per logical core:
```csharp
public record CoreSnapshot
{
    public int CoreIndex { get; init; }
    public int CcdIndex { get; init; }      // 0 or 1
    public double LoadPercent { get; init; }
    public double FrequencyMHz { get; init; }
    public double? TemperatureC { get; init; } // null if unavailable
}
```

**Events:** Raises `SnapshotReady(CoreSnapshot[])` on each refresh cycle. Both the dashboard's `MainViewModel` and the `OverlayViewModel` subscribe to this event.

**IDisposable:** Closes the PDH query handle and stops the timer on disposal.

### 4.3 Process Watcher (`Core/ProcessWatcher.cs`)

**Purpose:** Continuously monitor running processes to detect game launches and exits.

**Mode-independent:** Runs identically in Monitor and Optimize modes. Game detection fires regardless of mode — the AffinityManager decides what to do based on the current mode.

**How:**
- Uses `System.Timers.Timer` with configurable interval (default: 2000ms)
- Scans running processes via `System.Diagnostics.Process.GetProcesses()`
- Checks manual list and known games DB first (priority 1 and 2)
- Falls back to GPU auto-detection heuristic (priority 3) if enabled
- Tracks foreground window via `GetForegroundWindow` + `GetWindowThreadProcessId` (P/Invoke)
- Raises events: `GameDetected(ProcessInfo)`, `GameExited(ProcessInfo)`

**Auto-detection debounce:**
- Candidate tracking: must sustain GPU usage above threshold for `detectionDelaySeconds` (default: 5s)
- Exit delay: auto-detected games get `exitDelaySeconds` (default: 10s) grace period before exit event fires
- GPU drop check: if auto-detected game loses GPU focus and is not foreground, exit delay begins

**ProcessInfo record:**
```csharp
public record ProcessInfo
{
    public string Name { get; init; } = "";
    public int Pid { get; init; }
    public string DetectionSource { get; init; } = "manual list";
    public DetectionMethod Method { get; init; } = DetectionMethod.Manual;
    public float GpuUsage { get; init; }
}
```

### 4.4 Game Detector (`Core/GameDetector.cs`)

**Purpose:** Determine whether a running process is a game that should be pinned to the V-Cache CCD.

**Mode-independent:** Detection runs in both modes. In Monitor mode, detected games trigger simulated log entries. In Optimize mode, they trigger real affinity changes.

**Detection methods (3-tier priority):**

1. **Manual list (highest priority):** User-defined list of executable names in config. Case-insensitive matching with and without `.exe` suffix. Match = game. No further checks.

2. **Known games database:** Bundled `Data/known_games.json` with 65 game executables covering major titles across genres — shooters, RPGs, strategy, racing, MMOs, and simulators. Community-contributed, updated with releases. Provides display names for detected games.

3. **Auto-detection via GPU heuristic (lowest priority):** Handled by `ProcessWatcher` using `GpuMonitor`. Foreground process with GPU 3D engine utilization above threshold (default: 50%) and not in exclusion list. Subject to 5-second debounce before confirmation and 10-second exit delay.

**Exclusion list:** Configurable, 18 entries by default: browsers (`chrome.exe`, `firefox.exe`, `msedge.exe`), streaming (`obs64.exe`, `obs.exe`), communication (`discord.exe`, `spotify.exe`), development (`devenv.exe`, `code.exe`), media (`vlc.exe`, `mpc-hc64.exe`), creative (`photoshop.exe`, `premiere pro.exe`, `aftereffects.exe`, `davinci resolve.exe`, `blender.exe`), and system (`explorer.exe`, `dwm.exe`).

**DetectionMethod enum:**
```csharp
public enum DetectionMethod
{
    Manual,
    Database,
    Auto
}
```

### 4.5 GPU Monitor (`Core/GpuMonitor.cs`)

**Purpose:** Query per-process GPU utilization for game auto-detection.

**How:**
- Uses WMI `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine` class
- Filters by PID and 3D engine type (`engtype_3D`) in the instance name
- Returns total `UtilizationPercentage` across all 3D engine instances for a given process
- Tests counter availability at startup; disables auto-detection if GPU performance counters are unavailable
- WMI query timeout: 2 seconds per call, 3 seconds for startup test

**IDisposable:** Simple flag-based disposal to stop further queries.

### 4.6 Affinity Manager (`Core/AffinityManager.cs`)

**Purpose:** Mode-aware affinity management. In Optimize mode, applies and releases CPU affinity constraints. In Monitor mode, simulates the same logic and emits what-would-happen events.

**Thread safety:** All operations protected by `_syncLock` object. Lock-based synchronization ensures safe mid-game mode switches from the UI thread while the process watcher fires events from timer threads.

**Mode awareness:** The AffinityManager checks its `Mode` property before every `SetProcessAffinityMask` call. The event stream has the same structure regardless of mode — only the action type and whether Win32 APIs are called differ.

#### Monitor Mode Behaviour

When a game is detected:
1. Calculate what affinity changes would be made (same logic as Optimize)
2. Emit `WouldEngage` event for the game process
3. Emit `WouldMigrate` events for each background process that would be moved (first 5 individually, then summary)
4. Do NOT call `SetProcessAffinityMask` or any other Win32 affinity API
5. Do NOT store original affinities (nothing to restore)

When a game exits:
1. Emit `WouldRestore` event — "game exited — would have restored all affinities"
2. Clear game tracking state

#### Optimize Mode Behaviour

When a game is detected — engage:
1. Open game process handle with `PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION`
2. Read and store original affinity mask via `GetProcessAffinityMask`
3. Set game process affinity to `VCacheMask` via `SetProcessAffinityMask`
4. Iterate all non-essential background processes, store originals, set affinity to `FrequencyMask`
5. Store all original affinities in `Dictionary<int, IntPtr>` for restoration
6. Emit `Engaged` / `Migrated` events for each modification
7. Skip protected processes with `Skipped` event, skip access-denied with silent skip

When a game exits — disengage:
1. Restore all modified processes to original affinity from stored dictionary
2. Clear game tracking state
3. Emit `Restored` event with count of restored and failed processes
4. Log session summary

#### Mode Switch Mid-Game

**Monitor -> Optimize (game already detected):**
1. Clear stored affinities
2. Immediately engage: pin game to VCacheMask, migrate background to FrequencyMask
3. Store original affinities from this point
4. Emit real `Engaged` / `Migrated` events

**Optimize -> Monitor (game still running):**
1. Call `RestoreAll` — restore all modified affinities to original values
2. Emit `Restored` events for each process
3. Clear stored affinities
4. Game remains tracked but in observation-only mode; subsequent detection cycles emit `Would*` events

#### Protected Processes (never touched in either mode)

Hardcoded: `System`, `Idle`, `csrss`, `smss`, `services`, `wininit`, `lsass`, `winlogon`, `dwm`, `audiodg`, `fontdrvhost`, `Registry`, `Memory Compression`, `svchost`, `X3DCcdOptimizer`

Additionally: PID 0 and PID 4 are always skipped. User-defined `protectedProcesses` list in config (default: `audiodg.exe`, `svchost.exe`).

#### Events Emitted

```csharp
public event Action<AffinityEvent>? AffinityChanged;

public record AffinityEvent
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string ProcessName { get; init; } = "";
    public int Pid { get; init; }
    public AffinityAction Action { get; init; }
    public string Detail { get; init; } = "";
}

public enum AffinityAction
{
    // Optimize mode — real actions
    Engaged,          // Game pinned to V-Cache CCD
    Migrated,         // Background process moved to Frequency CCD
    Restored,         // Affinity restored after game exit
    Skipped,          // Process was protected/inaccessible
    Error,            // Operation failed

    // Monitor mode — simulated actions
    WouldEngage,      // Would have pinned game to V-Cache CCD
    WouldMigrate,     // Would have moved process to Frequency CCD
    WouldRestore      // Would have restored affinity
}
```

### 4.7 Dashboard Window (`Views/DashboardWindow.xaml` + `.xaml.cs`)

**Purpose:** The centrepiece of the application. A real-time WPF visual display showing CCD activity, process routing, and action history. This is what makes the tool unique — and in Monitor mode, the dashboard IS the product.

**Framework:** WPF with MVVM pattern. `MainViewModel` drives all data binding. Dark theme with custom resource dictionaries.

**Layout (top to bottom, Grid rows):**

1. **Status Bar (Row 0)** — Rounded-corner banner with dynamic accent colour.
   - Contains a prominent **Monitor / Optimize animated pill toggle** (164x36px `ToggleButton` with sliding thumb, drop shadow, and bounce animation)
   - Pill toggle slides right and transitions from blue (`AccentBlue #4B9EFF`) to green (`AccentGreen #34C759`) via 250ms cubic ease animation
   - Click bounce: scale down to 0.95x on press, overshoot to 1.04x on release, settle to 1.0x
   - Optimize toggle greyed out at 40% opacity when `HasVCache == false`
   - **Pulsing status dot:** 8px white ellipse with opacity animation (1.0 to 0.35, 1.2s cycle, sine ease, auto-reverse, infinite)
   - **Session timer:** Cascadia Mono font, visible only during active game session (`Xh MMm SSs` or `Mm SSs`)
   - Status text changes with mode and state:
     - Monitor + no game: "Monitor — observing CCD activity" (blue)
     - Monitor + game detected: "Monitor — observing [game.exe] on CCD0" (blue)
     - Optimize + no game: "Optimize — waiting for game" (purple)
     - Optimize + game active: "Optimize — [game.exe] pinned to V-Cache CCD" (green)

2. **CCD Panels (Row 1)** — Two side-by-side panels in a `UniformGrid`, one per CCD.
   - Header: CCD name (18px SemiBold), V-Cache/Frequency badge (pill-shaped, green or blue background), L3 size text
   - **Accent-colored left edge stripe:** 3px rounded border in badge colour at 50% opacity, providing CCD identity at a glance
   - Core range text (e.g., "Cores 0-15")
   - Role label: "Gaming — [game.exe]", "Observed — [game.exe]", "Background", or "Idle"
   - Core grid: 4x2 `UniformGrid` of `CoreTile` user controls, each showing:
     - Core index label (C0, C1, etc.) — Cascadia Mono, 10px
     - Load percentage — large Cascadia Mono bold number (26px, `BigNumber` style)
     - Current frequency — Cascadia Mono, 10px, top-right
     - Thin load bar at bottom (2px with green accent fill proportional to load)
     - **Load-based background colour:** idle (#222226), moderate (#3A3318 amber tint), hot (#1A382A green tint)
     - **Opacity:** 35% when parked/idle (0% load), full otherwise
   - **Border styling by mode and state:**
     - Monitor + game observed: accent-coloured border on relevant CCD panel
     - Optimize + game engaged: accent-coloured border on V-Cache CCD
     - No game: default border (#3A3A42)

3. **Process Router Table (Row 2)** — Shows active process-to-CCD assignments in a card panel.
   - Columns: Process name (Cascadia Mono, 12px), CCD assignment badge (pill, green for V-Cache, blue for Frequency), detail text
   - Alternating row backgrounds for readability
   - Updates on every affinity event

4. **Activity Log (Row 3)** — Scrolling `ListBox` of timestamped actions with virtualization enabled.
   - Three-column grid per entry: timestamp (68px, Cascadia Mono 11px, tertiary), action type (185px, Cascadia Mono 11px, SemiBold, colour-coded), detail (remaining, with `CharacterEllipsis` trimming)
   - **Optimize mode entries:**
     - ENGAGE (green `AccentGreen`), MIGRATE (green), RESTORE (blue `AccentBlue`), SKIP (amber `AccentAmber`), ERROR (red `AccentRed`)
   - **Monitor mode entries:**
     - `[MONITOR] WOULD ENGAGE` — dimmed opacity (0.65), italic font style
     - `[MONITOR] WOULD MIGRATE` — dimmed, italic
     - `[MONITOR] WOULD RESTORE` — dimmed, italic
   - **Alternating row shading:** every other row gets `RowAlt` (#1C1C20) background
   - Auto-scrolls to newest entry via `CollectionChanged` handler
   - Max display: 200 entries (ring buffer in `ActivityLogViewModel`)

5. **Footer (Row 4)** — Info bar with overlay toggle button.
   - **Gradient separator:** `LinearGradientBrush` that fades from transparent to `BorderDefault` (#3A3A42) at centre and back
   - Left: version, detected CPU model, core/thread count, polling interval
   - Right: "Show Overlay" / "Hide Overlay" subtle button (`SubtleButtonStyle`)

**Window behaviour:**
- Default size: 860x740px
- Minimum size: 680x540px
- Remembers position/size in config, restores on launch with screen bounds validation
- Close button minimizes to tray (hides window, cancels close via `OnClosing`)
- DPI-aware: WPF built-in per-monitor scaling

### 4.8 Compact Overlay (`Views/OverlayWindow.xaml` + `.xaml.cs`)

**Purpose:** A minimal, OLED-safe floating overlay showing CCD status while gaming. Designed to be unobtrusive and to not cause screen burn-in on OLED displays.

**Dimensions:** 280x160px, `WindowStyle="None"`, `AllowsTransparency="True"`, `Topmost="True"`, `ShowInTaskbar="False"`.

**Visual design:**
- Rounded border (12px radius) with semi-transparent dark background (`#CC1A1A1E`) and subtle white border (`#40FFFFFF`)
- Mode indicator: coloured dot + mode text (blue=Monitor, purple=Optimize idle, green=Optimize active)
- Game name: shows current game or "No game detected" (tertiary colour when no game, primary when active)
- CCD load bars: two horizontal bars (CCD0 green, CCD1 blue) with proportional fill and percentage text (Cascadia Mono)
- Last action text: bottom-aligned, tertiary colour, character-ellipsis trimming

**OLED safety features:**
- **Auto-hide:** Fades out to 0% opacity after configurable timeout (default: 10 seconds) via cubic ease animation (500ms). Fades back in on interaction (200ms).
- **Pixel shift:** Randomly shifts position by -5 to +5 pixels every configurable interval (default: 3 minutes) to prevent burn-in. Clamped to screen bounds.
- **Transparent background:** Minimizes static bright pixels.

**Interaction:**
- **Draggable:** Left-click + drag anywhere on the overlay. Position saved to config.
- **Mouse hover:** Resets auto-hide timer.
- **Right-click context menu:** "Open Dashboard", "Toggle Mode", separator, "Close Overlay"
- **Hotkey:** `Ctrl+Shift+O` shows the overlay (registered via `User32.RegisterHotKey` with `MOD_NOREPEAT`)
- Default position: top-right corner of primary screen with 20px margin

**ViewModel:** `OverlayViewModel` — subscribes to `SnapshotReady`, `AffinityChanged`, `GameDetected`, `GameExited` events. Calculates average CCD load from snapshots.

### 4.9 System Tray (`Tray/TrayIconManager.cs` + `Tray/IconGenerator.cs`)

**Purpose:** Persistent system tray presence via WinForms `NotifyIcon` hosted in a WPF application. Dashboard and overlay hide here when minimized.

**Icon generation:** Programmatically generated 32x32 anti-aliased filled circles with a subtle highlight gradient. No icon files needed. Generated via `System.Drawing` and cached by colour name.

**Tray icon states (coloured circles):**
- Blue (`#4B9EFF`): Monitor mode (observing, no game or game observed)
- Purple (`#9B6DFF`): Optimize mode, idle (waiting for game)
- Green (`#34C759`): Optimize mode, active (game engaged)
- Yellow (`#FFB340`): Warning
- Red (`#FF4545`): Error

**Icon updates:** Reactive to `MainViewModel.PropertyChanged` — updates icon, tooltip text, and menu item states when `CurrentMode`, `IsGameActive`, or `StatusText` changes.

**Right-click `ContextMenuStrip`:**
- Status line (disabled, informational — shows current mode and state)
- Separator
- "Mode: Monitor" (radio-style, checked when active)
- "Mode: Optimize" (radio-style, checked when active, disabled when `!IsOptimizeEnabled`)
- Separator
- "Open Dashboard"
- "Show Overlay" / "Hide Overlay" (toggles)
- Separator
- "View Log File..." (opens `%APPDATA%\X3DCCDOptimizer\logs` in Explorer)
- "About" (version dialog)
- Separator
- "Exit"

**Double-click:** Opens/shows Dashboard window.

**Tooltip:** Truncated to 127 characters (Windows limit).

### 4.10 Settings Window (`SettingsForm.cs`) — Phase 3

**Purpose:** Configuration editor. Separate from Dashboard.

**Planned tabs:**
- **General:** Polling interval, dashboard refresh rate, start with Windows, minimize to tray on close, notifications, default operating mode
- **Game List:** Add/remove manual game executables. Checkbox list with add/remove buttons.
- **Exclusions:** Add/remove excluded processes
- **Auto-Detection:** Enable/disable, GPU threshold slider, require foreground toggle, debounce timing
- **Overlay:** Enable/disable, auto-hide timeout, pixel shift interval, opacity slider, hotkey configuration
- **Advanced:** CCD override (manual core mask entry), log level, protected process list, log file location

### 4.11 Configuration (`Config/AppConfig.cs` + `config.json`)

**Config file location:** `%APPDATA%\X3DCCDOptimizer\config.json`

**Schema version:** 3

```json
{
  "version": 3,
  "operationMode": "monitor",
  "pollingIntervalMs": 2000,
  "dashboardRefreshMs": 1000,
  "autoDetection": {
    "enabled": true,
    "gpuThresholdPercent": 50,
    "requireForeground": true,
    "detectionDelaySeconds": 5,
    "exitDelaySeconds": 10
  },
  "manualGames": [
    "elitedangerous64.exe",
    "ffxiv_dx11.exe",
    "stellaris.exe",
    "re4.exe",
    "helldivers2.exe",
    "starcitizen.exe"
  ],
  "excludedProcesses": [
    "chrome.exe",
    "firefox.exe",
    "msedge.exe",
    "obs64.exe",
    "obs.exe",
    "discord.exe",
    "spotify.exe",
    "devenv.exe",
    "explorer.exe",
    "dwm.exe",
    "vlc.exe",
    "mpc-hc64.exe",
    "photoshop.exe",
    "premiere pro.exe",
    "aftereffects.exe",
    "davinci resolve.exe",
    "blender.exe",
    "code.exe"
  ],
  "protectedProcesses": [
    "audiodg.exe",
    "svchost.exe"
  ],
  "ccdOverride": null,
  "logging": {
    "level": "Information",
    "maxSizeMb": 10
  },
  "ui": {
    "startWithWindows": true,
    "startMinimized": false,
    "minimizeToTray": true,
    "notifications": true,
    "windowPosition": null,
    "windowSize": null
  },
  "overlay": {
    "enabled": false,
    "autoHideSeconds": 10,
    "pixelShiftMinutes": 3,
    "hotkey": "Ctrl+Shift+O",
    "opacity": 0.8,
    "position": null
  }
}
```

**`operationMode` field:**
- `"monitor"` (default) — observation only, no affinity changes
- `"optimize"` — active affinity management
- If config says `"optimize"` but `HasVCache == false` at startup, falls back to `"monitor"` with a warning log

**`ccdOverride` field (when set):**
```json
{
  "ccdOverride": {
    "vcacheCores": [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
    "frequencyCores": [16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31]
  }
}
```

**Load/Save:** `AppConfig.Load()` creates the directory and file on first run with defaults. Corrupted config gracefully falls back to defaults. `AppConfig.Save()` writes indented JSON with null properties omitted.

### 4.12 Logger (`Logging/AppLogger.cs`)

**Implementation:** Serilog with dual sinks:
- **Console sink:** For development and debugging
- **File sink:** Rotating log file at `%APPDATA%\X3DCCDOptimizer\logs\`, configurable max size, configurable log level

**Structured logging:** All log entries use Serilog's structured format with named properties.

---

## 5. Tech Stack

| Component            | Choice                                      | Rationale                                          |
|----------------------|----------------------------------------------|----------------------------------------------------|
| Language             | C# 12 / .NET 8                               | Native Windows. First-class Win32/WMI/WPF.         |
| Target framework     | net8.0-windows                                | LTS, Windows-specific APIs enabled                 |
| UI framework         | WPF (MVVM)                                    | Rich data binding, animations, dark theme support  |
| System tray          | WinForms NotifyIcon (hosted in WPF app)       | WPF has no native tray API; WinForms interop works |
| Win32 API            | P/Invoke (DllImport)                          | kernel32, user32, pdh.dll direct access            |
| WMI                  | System.Management                             | CPU topology fallback, GPU perf counters           |
| Performance counters | PDH via P/Invoke                              | Per-core load/frequency, lower overhead than WMI   |
| GPU detection        | WMI GPUPerformanceCounters                    | Per-process GPU usage without external dependencies|
| JSON config          | System.Text.Json                              | Built-in, fast, source-gen compatible              |
| Logging              | Serilog + Serilog.Sinks.File + Serilog.Sinks.Console | Rolling files, structured logging          |
| Build                | dotnet publish --self-contained                | Single-file .exe, no .NET install needed           |
| Installer            | Inno Setup (planned, Phase 4)                 | Professional installer for public release          |

### Self-Contained Deployment

```bash
dotnet publish src/X3DCcdOptimizer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Single .exe, currently ~155MB (includes .NET runtime + WPF dependencies). Trimming planned for Phase 4 to reduce size. No prerequisites on target machine.

---

## 6. Build Phases

### Phase 1: Foundation + Console Dashboard -- COMPLETE

**Goal:** Working core loop with console-based output proving the engine works.

Files delivered:
- `Core/CcdMapper.cs` — topology detection via `GetLogicalProcessorInformationEx` with `CACHE_RELATIONSHIP` parsing
- `Core/AffinityManager.cs` — set/release affinity via P/Invoke
- `Core/ProcessWatcher.cs` — timer-based polling
- `Core/GameDetector.cs` — manual list matching only
- `Core/PerformanceMonitor.cs` — per-core load/frequency via PDH
- `Config/AppConfig.cs` — load/save JSON config
- `Logging/AppLogger.cs` — Serilog file logging
- `Native/Kernel32.cs` — P/Invoke signatures for affinity and topology
- `Native/User32.cs` — P/Invoke signatures for foreground window
- `Native/Pdh.cs` — PDH P/Invoke signatures
- `Native/Structs.cs` — native struct definitions
- `Models/CpuTopology.cs`, `CoreSnapshot.cs`, `AffinityEvent.cs`
- `Program.cs` — console entry point with live core stats

**Key bug fixed:** `CACHE_RELATIONSHIP` struct had incorrect padding. The Windows API defines an 18-byte `Reserved` field between the `Type` (DWORD) and `GroupCount` (WORD) fields. The initial implementation used 20 bytes, causing misaligned reads of `GroupCount` and `GroupMask`, which resulted in incorrect core masks and V-Cache detection failures. Fixed by changing the `Reserved` field to `[MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)] public byte[] Reserved`.

**Exit criteria (met):** Launch the app in console mode, see per-core load/frequency printing, launch a game, see log output confirming game pinned to V-Cache CCD and background processes migrated to CCD1, close game, see restoration log. All operations verified via Task Manager.

### Phase 2: WPF Dashboard + Monitor/Optimize Dual-Mode -- COMPLETE

**Goal:** Full visual WPF dashboard with Monitor/Optimize dual-mode system as a first-class feature.

Files delivered:
- `App.xaml` + `App.xaml.cs` — WPF application entry point, engine wiring, hotkey registration
- `Views/DashboardWindow.xaml` + `.xaml.cs` — main dashboard window with all panels and mode toggle
- `Views/CcdPanel.xaml` + `.xaml.cs` — CCD core grid UserControl with accent edge stripe
- `Views/CoreTile.xaml` + `.xaml.cs` — individual core tile with load-based colouring
- `ViewModels/MainViewModel.cs` — central ViewModel binding dashboard to engine
- `ViewModels/CcdPanelViewModel.cs` — CCD panel data and border state
- `ViewModels/CoreTileViewModel.cs` — per-core load/freq/colour
- `ViewModels/ActivityLogViewModel.cs` — ring buffer log entries
- `ViewModels/ProcessRouterViewModel.cs` — process-to-CCD assignment tracking
- `ViewModels/LogEntryViewModel.cs` — individual log entry formatting
- `ViewModels/ProcessEntryViewModel.cs` — process router entry formatting
- `ViewModels/ViewModelBase.cs` — `INotifyPropertyChanged` base class
- `ViewModels/RelayCommand.cs` — `ICommand` implementation
- `Converters/LoadColorConverter.cs` — load % to background colour
- `Converters/LoadBarWidthConverter.cs` — load % to pixel width
- `Converters/BoolToFontStyleConverter.cs` — italic for Monitor mode log entries
- `Themes/DarkTheme.xaml` — colour palette (#1A1A1E base, accents, glows, gradients)
- `Themes/Typography.xaml` — font stack (Segoe UI Variable + Cascadia Mono), text styles
- `Themes/Controls.xaml` — card, badge, pill toggle, subtle button, core tile, scrollbar styles
- `Models/OperationMode.cs` — Monitor/Optimize enum

Key deliverables:
- Animated pill toggle with drop shadow and bounce effect
- Pulsing status dot animation
- Load-based core tile colouring (idle/moderate/hot)
- Accent-coloured CCD panel edge stripes
- Alternating log row shading
- Monitor/Optimize mode-aware activity log with italic styling
- HasVCache gating on Optimize toggle
- Mode switch mid-game (immediate engage or restore)
- Session timer display

**Exit criteria (met):** Launch app, starts in Monitor mode, dashboard shows real-time CCD activity with dark theme. Launch a game, log shows `[MONITOR] WOULD ENGAGE` in italic. Toggle to Optimize, log shows real `ENGAGE`, cores light up, processes migrate. Toggle back to Monitor, affinities restored. Close game, dashboard shows restoration.

### Phase 2.5: OLED-safe Overlay, GPU Auto-Detection, Code Audit -- COMPLETE

**Goal:** Compact gaming overlay, GPU-based game auto-detection, and full codebase quality audit.

Files delivered:
- `Views/OverlayWindow.xaml` + `.xaml.cs` — 280x160 transparent OLED-safe overlay
- `ViewModels/OverlayViewModel.cs` — overlay data binding with auto-hide and pixel shift
- `Core/GpuMonitor.cs` — per-process GPU utilization via WMI
- `Tray/TrayIconManager.cs` — WinForms NotifyIcon with mode-aware icons and context menu
- `Tray/IconGenerator.cs` — programmatic coloured circle icon generation
- `Data/known_games.json` — 65-game database

**Code audit (12 issues fixed):**
Phase 2.5 included a full code audit that identified and fixed 12 issues across the codebase, covering resource management, thread safety, error handling, and UI polish.

Key deliverables:
- OLED-safe overlay with auto-hide (10s), pixel shift (3min), and opacity fade animations
- Ctrl+Shift+O hotkey for overlay toggle
- Overlay right-click context menu
- GPU auto-detection via WMI with 5-second debounce and 10-second exit delay
- 65-game known games database
- Auto-detection exclusion list (18 entries)
- System tray with programmatic coloured circle icons
- Tray context menu with mode toggle, overlay toggle, log viewer, and about dialog
- Global exception handlers (Dispatcher + AppDomain)
- Hotkey registration and unregistration lifecycle

**Exit criteria (met):** Launch app, overlay appears at top-right and auto-hides after 10 seconds. Ctrl+Shift+O brings it back. Launch an unlisted game with GPU usage, auto-detected after 5-second debounce. Overlay shows CCD load bars and last action. System tray icon colour matches mode. Right-click tray and overlay menus work correctly. Close overlay via right-click menu.

### Phase 3: Settings Window + Start-with-Windows -- NEXT

**Goal:** User-configurable settings and system startup integration.

Planned additions:
- `SettingsWindow.xaml` — WPF tabbed settings editor
- Start-with-Windows (Registry key `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`)
- In-app game list editor (add/remove manual executables)
- Exclusion list editor
- Auto-detection settings (threshold slider, debounce timing)
- Overlay settings (auto-hide, pixel shift, opacity, hotkey)
- Log level configuration
- CCD override editor

**Exit criteria:** Open Settings from tray menu or dashboard. Modify game list, exclusion list, polling interval, auto-detection settings. Changes persist and take effect immediately. Enable start-with-Windows, verify app launches on login.

### Phase 4: Polish & Release

**Goal:** Public release quality.

Planned additions:
- `dotnet publish` with IL trimming to reduce exe size from ~155MB
- GitHub Actions CI (build + test + publish release artifacts)
- Inno Setup installer (optional)
- README.md with dashboard screenshots showing both modes
- Known games database as updatable sidecar file
- Proper application icon (`.ico` file)

**Exit criteria:** Clean install on a fresh Windows 11 machine, works end-to-end, dashboard looks polished in both modes, no .NET install required. Exe size under 50MB with trimming.

---

## 7. Project Structure

```
x3d-ccd-optimizer/
├── src/
│   └── X3DCcdOptimizer/
│       ├── X3DCcdOptimizer.csproj
│       ├── App.xaml                        # WPF Application definition
│       ├── App.xaml.cs                     # Entry point, engine wiring, hotkey
│       │
│       ├── Core/
│       │   ├── CcdMapper.cs               # CCD topology detection (HasVCache)
│       │   ├── PerformanceMonitor.cs       # Per-core load/freq via PDH
│       │   ├── ProcessWatcher.cs           # Process polling + game detection events
│       │   ├── GameDetector.cs             # 3-tier game identification + ProcessInfo
│       │   ├── GpuMonitor.cs              # Per-process GPU usage via WMI
│       │   └── AffinityManager.cs         # Mode-aware, lock-based affinity ops
│       │
│       ├── Views/
│       │   ├── DashboardWindow.xaml(.cs)   # Main dashboard + mode toggle
│       │   ├── CcdPanel.xaml(.cs)          # CCD core grid UserControl
│       │   ├── CoreTile.xaml(.cs)          # Individual core tile
│       │   └── OverlayWindow.xaml(.cs)     # Compact OLED-safe overlay
│       │
│       ├── ViewModels/
│       │   ├── MainViewModel.cs           # Central dashboard ViewModel
│       │   ├── CcdPanelViewModel.cs       # CCD panel data + borders
│       │   ├── CoreTileViewModel.cs       # Per-core load/colour
│       │   ├── ActivityLogViewModel.cs    # Ring buffer log entries
│       │   ├── LogEntryViewModel.cs       # Individual log entry formatting
│       │   ├── ProcessRouterViewModel.cs  # Process-to-CCD assignment
│       │   ├── ProcessEntryViewModel.cs   # Process router entry formatting
│       │   ├── OverlayViewModel.cs        # Overlay data + auto-hide + pixel shift
│       │   ├── ViewModelBase.cs           # INotifyPropertyChanged base
│       │   └── RelayCommand.cs            # ICommand implementation
│       │
│       ├── Converters/
│       │   ├── LoadColorConverter.cs      # Load % → background brush
│       │   ├── LoadBarWidthConverter.cs   # Load % → bar width
│       │   └── BoolToFontStyleConverter.cs # Monitor mode → italic
│       │
│       ├── Themes/
│       │   ├── DarkTheme.xaml             # Colours, brushes, gradients
│       │   ├── Typography.xaml            # Font stack + text styles
│       │   └── Controls.xaml              # Card, badge, pill toggle, button styles
│       │
│       ├── Tray/
│       │   ├── TrayIconManager.cs         # WinForms NotifyIcon + context menu
│       │   └── IconGenerator.cs           # Programmatic coloured circle icons
│       │
│       ├── Config/
│       │   └── AppConfig.cs               # Config model + I/O (version 3)
│       │
│       ├── Logging/
│       │   └── AppLogger.cs               # Serilog setup (console + file)
│       │
│       ├── Native/
│       │   ├── Kernel32.cs                # P/Invoke: affinity, topology, processes
│       │   ├── User32.cs                  # P/Invoke: foreground window, hotkeys
│       │   ├── Pdh.cs                     # P/Invoke: performance counters
│       │   └── Structs.cs                 # GROUP_AFFINITY, CACHE_RELATIONSHIP,
│       │                                  # SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX,
│       │                                  # PDH_FMT_COUNTERVALUE
│       │
│       ├── Models/
│       │   ├── CpuTopology.cs             # Topology data (HasVCache computed)
│       │   ├── CoreSnapshot.cs            # Per-core metrics snapshot
│       │   ├── AffinityEvent.cs           # Affinity event + AffinityAction enum
│       │   └── OperationMode.cs           # Monitor/Optimize enum
│       │
│       └── Data/
│           └── known_games.json           # 65-game database
│
├── tests/
│   └── X3DCcdOptimizer.Tests/
│       └── X3DCcdOptimizer.Tests.csproj
│
├── X3DCcdOptimizer.sln
├── X3D_CCD_OPTIMIZER_BLUEPRINT.md         # This file — project spec
├── CLAUDE.md                              # AI assistant instructions
├── CONTRIBUTING.md
├── LICENSE                                # GPL v2
├── .gitignore
└── .editorconfig
```

---

## 8. Key P/Invoke Signatures

```csharp
// Native/Kernel32.cs
internal static class Kernel32
{
    internal const uint PROCESS_SET_INFORMATION = 0x0200;
    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    internal const int RelationProcessorCore = 0;
    internal const int RelationCache = 2;
    internal const int RelationProcessorPackage = 3;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetProcessAffinityMask(
        IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetLogicalProcessorInformationEx(
        int RelationshipType, IntPtr Buffer, ref uint ReturnedLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);
}

// Native/User32.cs
internal static class User32
{
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const uint VK_O = 0x4F;
    internal const int WM_HOTKEY = 0x0312;
}

// Native/Pdh.cs
internal static class Pdh
{
    internal const uint PDH_FMT_DOUBLE = 0x00000200;
    internal const int PDH_CSTATUS_VALID_DATA = 0x00000000;
    internal const int PDH_CSTATUS_NEW_DATA = 0x00000001;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern int PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern int PdhAddEnglishCounter(
        IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll")]
    internal static extern int PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll")]
    internal static extern int PdhGetFormattedCounterValue(
        IntPtr hCounter, uint dwFormat, out IntPtr lpdwType, out PDH_FMT_COUNTERVALUE pValue);

    [DllImport("pdh.dll")]
    internal static extern int PdhCloseQuery(IntPtr hQuery);
}

// Native/Structs.cs
[StructLayout(LayoutKind.Sequential)]
internal struct GROUP_AFFINITY
{
    public UIntPtr Mask;
    public ushort Group;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CACHE_RELATIONSHIP
{
    public byte Level;
    public byte Associativity;
    public ushort LineSize;
    public uint CacheSize;
    public int Type;                    // PROCESSOR_CACHE_TYPE enum

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
    public byte[] Reserved;            // 18 bytes — critical: NOT 20

    public ushort GroupCount;
    public GROUP_AFFINITY GroupMask;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
{
    public int Relationship;
    public uint Size;
    // Union data follows — parsed manually via pointer arithmetic
}

[StructLayout(LayoutKind.Sequential)]
internal struct PDH_FMT_COUNTERVALUE
{
    public int CStatus;
    public double doubleValue;
}
```

---

## 9. Dashboard Design Specification

### Dark Theme Colour Palette

| Token            | Hex       | Usage                                      |
|------------------|-----------|--------------------------------------------|
| BgPrimary        | `#1A1A1E` | Window background                          |
| BgSecondary      | `#232328` | Card/panel backgrounds                     |
| BgTertiary       | `#2C2C32` | Button backgrounds                         |
| TextPrimary      | `#E8E8ED` | Headings, load numbers                     |
| TextSecondary    | `#9898A0` | Labels, process details                    |
| TextTertiary     | `#5C5C66` | Timestamps, hints, core indices            |
| AccentBlue       | `#4B9EFF` | Monitor mode, frequency CCD                |
| AccentGreen      | `#34C759` | Optimize mode, V-Cache CCD, engage actions |
| AccentPurple     | `#9B6DFF` | Optimize idle                              |
| AccentAmber      | `#FFB340` | Moderate load, skip actions, warnings      |
| AccentRed        | `#FF4545` | Error actions, critical states             |
| CoreIdle         | `#222226` | Core tile 0-15% load                       |
| CoreModerate     | `#3A3318` | Core tile 16-40% load (amber tint)         |
| CoreHot          | `#1A382A` | Core tile 41-100% load (green tint)        |
| BorderDefault    | `#3A3A42` | Panel borders, separator lines             |
| BorderSubtle     | `#2A2A32` | Subtle borders, pressed states             |
| RowAlt           | `#1C1C20` | Alternating log row shading                |

### Typography

| Style         | Font                                          | Size | Weight   | Colour        |
|---------------|-----------------------------------------------|------|----------|---------------|
| SectionHeader | Segoe UI Variable Display                     | 18px | SemiBold | TextPrimary   |
| EmphasisText  | Segoe UI Variable Display                     | 14px | SemiBold | TextPrimary   |
| BodyText      | Segoe UI Variable Display                     | 13px | Normal   | TextPrimary   |
| SmallText     | Segoe UI Variable Display                     | 12px | Normal   | TextSecondary |
| HintText      | Segoe UI Variable Display                     | 11px | Normal   | TextTertiary  |
| BigNumber     | Cascadia Mono                                 | 26px | Bold     | TextPrimary   |
| MonoText      | Cascadia Mono                                 | 12px | Normal   | TextPrimary   |
| LogEntry      | Cascadia Mono                                 | 11px | SemiBold | (action-coded)|

### Core Tiles (load-based colours)

- 0-15% load: `CoreIdle` (#222226)
- 16-40% load: `CoreModerate` (#3A3318 amber tint)
- 41-100% load: `CoreHot` (#1A382A green tint)
- Parked/idle (0% load + no frequency): 35% opacity

### CCD Panel Edges

Each CCD panel has a 3px accent-coloured left edge stripe:
- V-Cache CCD: green gradient (`#6034C759` to `#0034C759`)
- Frequency CCD: blue gradient (`#604B9EFF` to `#004B9EFF`)

### CCD Panel Border States

- Monitor + game observed on this CCD: accent-coloured border
- Optimize + game engaged on this CCD: accent-coloured border
- No game / other CCD: default border (`#3A3A42`)

### Animated Pill Toggle

- 164x36px `ToggleButton` with 18px corner-radius track
- 78px sliding thumb with `DropShadowEffect` (1px depth, 6px blur, 0.5 opacity)
- **Slide animation:** 250ms `DoubleAnimation` on `TranslateTransform.X` (0 to 76), `CubicEase EaseOut`
- **Colour animation:** 250ms `ColorAnimation` on thumb background and drop shadow (blue to green)
- **Click bounce:** `PreviewMouseDown` scales to 0.95x over 80ms; `PreviewMouseUp` overshoots to 1.04x over 80ms, then settles to 1.0x over 120ms with `CubicEase`
- **Disabled state:** 40% opacity

### Status Bar

- Rounded border (10px radius), 12px horizontal margin
- Background bound to `StatusColor` (blue, green, or purple `SolidColorBrush`)
- Subtle centre-brightened gradient overlay (`#0AFFFFFF` at centre)
- Contains: pulsing dot, status text, session timer (right), pill toggle (far right)

### Activity Log

- `ListBox` with virtualization (`VirtualizingPanel.IsVirtualizing="True"`)
- `AlternationCount="2"` for alternating row shading
- Custom `ListBoxItem` template: rounded border (3px radius), 6px horizontal padding
- Three-column grid: timestamp (68px), action (185px), detail (remaining)
- Auto-scroll: `CollectionChanged` triggers `ScrollIntoView` on last item

### Footer

- `LinearGradientBrush` separator: transparent -> `#803A3A42` at centre -> transparent
- Info text left-aligned, overlay toggle button right-aligned

### Process CCD Badges

- V-Cache CCD: green pill badge (`AccentGreen`)
- Frequency CCD: blue pill badge (`AccentBlue`)
- 4px corner radius, 8px horizontal padding

### Log Action Colours

**Optimize mode:**
- ENGAGE: `AccentGreen` (#34C759)
- MIGRATE: `AccentGreen`
- RESTORE: `AccentBlue` (#4B9EFF)
- SKIP: `AccentAmber` (#FFB340)
- ERROR: `AccentRed` (#FF4545)

**Monitor mode (same colours but dimmed):**
- [MONITOR] WOULD ENGAGE: 65% opacity, italic font style
- [MONITOR] WOULD MIGRATE: 65% opacity, italic
- [MONITOR] WOULD RESTORE: 65% opacity, italic

### Refresh Rates

| Component | Default | Range | Source |
|-----------|---------|-------|--------|
| Core load/frequency | 1000ms | 500-5000ms | PerformanceMonitor |
| Process list | 2000ms | 1000-10000ms | ProcessWatcher |
| Activity log | Immediate | Event-driven | AffinityManager events |
| Status bar | Immediate | Event-driven | State changes |
| Overlay CCD bars | 1000ms | Same as perf monitor | SnapshotReady events |

### Window Behaviour

- Default size: 860x740px
- Minimum size: 680x540px
- Remembers position/size in config (validated against screen bounds on restore)
- Close button minimizes to tray (configurable)
- DPI-aware: WPF per-monitor scaling (automatic)
- Window startup location: CenterScreen (first launch) or restored position

---

## 10. Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| Anticheat flags affinity changes | Game crash or ban | **Monitor mode is the default.** Users observe first. Per-game toggle. Only OS-level affinity APIs. Document known anticheat interactions. Most anticheats do not flag `SetProcessAffinityMask`. |
| Exclusive fullscreen blocks overlay | Overlay invisible during gameplay | Mitigation: use borderless windowed mode. Overlay uses `Topmost` but cannot render over exclusive fullscreen. Document in FAQ. |
| Process exits mid-affinity-change | Harmless exception | try/catch on every process operation. Log and continue. |
| Non-X3D dual-CCD CPU | App on non-target hardware | **Monitor mode works on any dual-CCD Ryzen.** Optimize toggle is disabled when `HasVCache == false`. |
| User enables Optimize on non-X3D | Incorrect affinity routing | Optimize toggle greyed out when `HasVCache == false`. Cannot be enabled via UI. Config fallback to Monitor with warning if manually edited. |
| GPU auto-detection false positives | Wrong process pinned | Manual list overrides auto. Exclusion list (18 entries). 5-second debounce. 10-second exit delay. Monitor mode shows what would be pinned without acting. |
| CCD ordering changes | Wrong CCD gets game | Detect by L3 cache size, not CCD number. Manual override available via `ccdOverride` config. |
| Windows permission errors | Can't set some affinities | Expected for system processes. Log, skip, continue. Monitor mode unaffected (never calls Win32 affinity APIs). Standard user sufficient for game processes. |
| Dashboard CPU usage | Tool consumes resources | PerformanceMonitor uses 1Hz polling. Activity log uses virtualized ListBox. Overlay auto-hides. Tray icon updates are event-driven, not polled. |
| 155MB exe size | Large download, slow startup | IL trimming planned for Phase 4 to reduce to ~30-50MB. Self-contained trade-off for zero prerequisites. |
| PDH counter not available | No per-core metrics | Fallback counter paths (`% Processor Time`). Degrade gracefully (show 0%). |
| GPU performance counters unavailable | Auto-detection disabled | GpuMonitor tests availability at startup. Falls back to manual + database detection only. Logged as warning. |
| Mode switch mid-game causes stutter | Brief performance disruption | Affinity changes are fast (< 1ms per process). Acceptable trade-off for user-initiated action. |

---

## 11. Compatibility

### Monitor Mode

- **OS:** Windows 10 21H2+, Windows 11
- **CPU:** Any AMD dual-CCD Ryzen processor (Zen 4 or Zen 5). Single-CCD shows per-core metrics only.
- **Runtime:** Self-contained .exe, no install required
- **Architecture:** x64 only
- **Display:** WPF per-monitor DPI scaling
- **Privileges:** Standard user. No elevated permissions required.

### Optimize Mode

- **CPU:** AMD dual-CCD X3D processors only (7950X3D, 7900X3D, 9950X3D, 9900X3D)
- **Requirement:** V-Cache CCD confirmed via topology detection (`HasVCache == true`)
- **Privileges:** Standard user. System processes gracefully skipped.
- All other requirements same as Monitor mode.

### Known Limitations

- Overlay requires borderless windowed mode in games (exclusive fullscreen blocks topmost windows)
- Self-contained exe is ~155MB (includes .NET runtime + WPF; trimming planned)
- GPU performance counters may be unavailable on older GPU drivers, disabling auto-detection
- WMI GPU queries have a 2-second timeout per call, which may cause brief polling pauses

---

## 12. What This Tool Is NOT

- **Not just an optimizer.** In Monitor mode, it's a real-time CCD diagnostic and visibility tool that works on any dual-CCD Ryzen.
- **Not a kernel driver.** Works at process affinity level only. No ring-0 code, no driver signing.
- **Not a replacement for AMD's chipset drivers.** Supplements, not replaces. Users should keep chipset drivers updated.
- **Not a general-purpose process manager.** One job: CCD visibility and affinity for dual-CCD Ryzen.
- **Not a hardware monitor.** Dashboard shows routing-relevant metrics only (load, frequency), not full system monitoring (voltage, power, temperature).
- **Not affiliated with AMD.** Independent open-source project.

---

## 13. Future Possibilities (Post v1.0)

- Per-game profiles (different background routing per game)
- Session statistics (time in game mode, CCD utilization history, graphs)
- Game database auto-update from GitHub
- Steam/Epic/GOG library integration
- Community game database (web endpoint for submissions)
- WPF MAUI upgrade for richer visuals and potential cross-platform
- Power plan integration (auto-switch on engage)
- Temperature monitoring integration (LibreHardwareMonitor)
- Export session logs to CSV
- Portable mode (config next to .exe instead of %APPDATA%)
- Parking health check — automated report on whether AMD's built-in CCD parking is functioning correctly, comparing observed core assignment against expected behaviour

---

*This document is the single source of truth for the X3D Dual CCD Optimizer project. All implementation decisions reference this blueprint.*
