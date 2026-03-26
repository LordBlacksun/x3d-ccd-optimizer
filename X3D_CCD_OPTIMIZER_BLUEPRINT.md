# X3D Dual CCD Optimizer — Project Blueprint

**Version:** 0.3.0
**Author:** Carlo Benedetti
**License:** GPL v2
**Repository:** github.com/LordBlacksun/x3d-ccd-optimizer
**Status:** Phase 1 complete

---

## 1. Problem Statement

AMD Ryzen dual-CCD X3D processors (7950X3D, 7900X3D, 9950X3D, 9900X3D) feature an asymmetric architecture: one CCD carries 3D V-Cache (larger L3, better for gaming), while the other runs at higher clock speeds (better for general compute). Optimal performance requires routing game workloads to the V-Cache CCD and background tasks to the frequency CCD.

AMD's official solution on Windows is a fragile chain: Xbox Game Bar must detect a foreground game → the PPM Provisioning File driver tells Windows to park the non-V-Cache CCD → the game runs on the correct cores. This mechanism:

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
3. **Zero-configuration by default** — Auto-detects CCD topology, auto-detects games. Works out of the box.
4. **Manual override always available** — User-defined game list as fallback. Manual always wins over auto.
5. **Minimal footprint** — System tray when minimized. Dashboard on demand. Near-zero CPU/RAM when idle.
6. **No kernel access required** — Userspace only. Uses standard Windows APIs. No driver signing needed.
7. **Open source from day one** — GPL v2, public repo, community contributions welcome.

---

## 3. Architecture Overview

```
┌──────────────────────────────────────────────────────────────┐
│                     Dashboard Window                          │
│  ┌───────────────────────────────────────────────────────┐   │
│  │  Status Bar  [Monitor ◉ / ○ Optimize]  CPU Model      │   │
│  └───────────────────────────────────────────────────────┘   │
│  ┌─────────────┐ ┌─────────────┐ ┌───────────────────────┐  │
│  │  CCD0 Panel │ │  CCD1 Panel │ │   Process Router      │  │
│  │  (per-core  │ │  (per-core  │ │   (which process      │  │
│  │   load/freq │ │   load/freq │ │    on which CCD)      │  │
│  │   heatmap)  │ │   heatmap)  │ │                       │  │
│  └──────┬──────┘ └──────┬──────┘ └──────────┬────────────┘  │
│         │               │                    │               │
│  ┌──────▼───────────────▼────────────────────▼────────────┐  │
│  │                   Activity Log                          │  │
│  │  (timestamped feed — real or simulated actions)         │  │
│  └────────────────────────────────────────────────────────┘  │
│                                                               │
│  ┌────────────────────────────────────────────────────────┐  │
│  │                    Footer Bar                           │  │
│  │   (version, polling interval, auto-detection status)    │  │
│  └────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬───────────────────────────────┘
                                │
┌───────────────────────────────▼───────────────────────────────┐
│                  System Tray (NotifyIcon)                       │
│          (minimized state, mode toggle, quick menu)             │
└───────────────────────────────┬───────────────────────────────┘
                                │
┌───────────────────────────────▼───────────────────────────────┐
│                       Core Engine                               │
│                                                                 │
│  ┌──────────────┐  ┌───────────────────────────────────────┐  │
│  │  CCD Mapper  │  │         Process Watcher               │  │
│  │  (topology   │  │  (polling loop, game detection)       │  │
│  │   detection) │  │  (mode-independent)                   │  │
│  └──────┬───────┘  └───────────────┬───────────────────────┘  │
│         │                          │                           │
│  ┌──────▼──────────────────────────▼────────────────────────┐ │
│  │            Affinity Manager (MODE-AWARE)                  │ │
│  │  Monitor:  emits Would* events, never calls Win32 API     │ │
│  │  Optimize: emits real events, calls SetProcessAffinityMask│ │
│  └───────────────────────────────────────────────────────────┘ │
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │              Game Detector (mode-independent)             │ │
│  │  (auto: GPU usage via PDH / WMI)                          │ │
│  │  (manual: user-defined executable list)                   │ │
│  │  (known games database)                                   │ │
│  │  (manual always overrides auto)                           │ │
│  └───────────────────────────────────────────────────────────┘ │
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │         Performance Monitor (mode-independent)            │ │
│  │  (per-core load %, frequency, temperature)                │ │
│  │  (via PDH counters / WMI)                                 │ │
│  │  (feeds dashboard in real-time)                           │ │
│  └───────────────────────────────────────────────────────────┘ │
└───────────────────────────────┬───────────────────────────────┘
                                │
┌───────────────────────────────▼───────────────────────────────┐
│                      Configuration                              │
│              (JSON config via System.Text.Json)                  │
└─────────────────────────────────────────────────────────────────┘
```

---

### 3.1 Operating Modes

The application operates in one of two modes at any time. Mode is user-selectable and persists across restarts.

#### Monitor Mode (Default)

Monitor mode is the safe, observation-only mode. It is the default on first launch and requires no special hardware.

**What runs:**
- CCD topology detection — identifies CCDs, cores, L3 sizes, V-Cache presence
- Performance monitoring — per-core load, frequency, temperature
- Process tracking — which processes are running on which CCD
- Game detection — identifies game launches via manual list, known games DB, and auto-detection

**What does NOT run:**
- `SetProcessAffinityMask` — never called. No process affinity is modified.

**Activity log output:**
- `[MONITOR] WOULD ENGAGE: elitedangerous64.exe → CCD0 (V-Cache)`
- `[MONITOR] WOULD MIGRATE: discord.exe → CCD1 (Frequency)`
- `[MONITOR] WOULD RESTORE: discord.exe → all cores`

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
- `ENGAGE: elitedangerous64.exe → CCD0 (V-Cache)`
- `MIGRATE: discord.exe → CCD1 (Frequency)`
- `RESTORE: discord.exe → all cores`

#### Mode Switching

**Toggle location:** Dashboard status bar (prominent toggle) and system tray context menu.

**Monitor → Optimize (while game is running):**
1. Mode flag changes immediately
2. AffinityManager detects active game session
3. Immediately engages: pins game to V-Cache, migrates background processes
4. Activity log shows real ENGAGE/MIGRATE entries from this point

**Optimize → Monitor (while game is running):**
1. Mode flag changes immediately
2. AffinityManager calls RestoreAll — all modified affinities return to original values
3. Activity log shows RESTORE entries, then switches to [MONITOR] WOULD* entries
4. Game continues running, unaffected, on whatever cores Windows assigns

**Mode persistence:** Saved to `config.json` as `"operationMode": "monitor"` or `"operationMode": "optimize"`. Restored on next launch.

**Optimize toggle disabled when:** `HasVCache == false` (non-X3D processor). Toggle is greyed out with tooltip: "Optimize mode requires a V-Cache processor."

---

## 4. Module Breakdown

### 4.1 CCD Mapper (`CcdMapper.cs`)

**Purpose:** Detect CPU topology at startup. Identify which cores belong to which CCD, and which CCD has V-Cache.

**How:**
- Use `GetLogicalProcessorInformationEx` (P/Invoke) to enumerate processor groups, cores, and cache topology
- Query L3 cache sizes per CCD via `SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX` structures
- Identify V-Cache CCD by comparing L3 sizes (V-Cache CCD = 96MB L3, standard CCD = 32MB)
- WMI fallback: `Win32_Processor` and `Win32_CacheMemory` via `System.Management`
- Fallback: manual CCD specification in config for edge cases
- Output: `CpuTopology` object containing:
  - `VCacheMask` (IntPtr) — affinity bitmask for V-Cache cores
  - `FrequencyMask` (IntPtr) — affinity bitmask for standard cores
  - `VCacheCores` (int[]) — list of V-Cache core indices
  - `FrequencyCores` (int[]) — list of standard core indices
  - `CpuModel` (string) — e.g., "AMD Ryzen 9 7950X3D"
  - `VCacheL3SizeMB` (int) — L3 size in MB for V-Cache CCD
  - `StandardL3SizeMB` (int) — L3 size in MB for standard CCD
  - `TotalLogicalCores` (int) — total logical cores across both CCDs
  - `HasVCache` (bool) — `true` if one CCD has significantly larger L3 (V-Cache detected). Used to gate the Optimize mode toggle.

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
- No AMD processor detected: warning dialog and exit

### 4.2 Performance Monitor (`PerformanceMonitor.cs`)

**Purpose:** Collect real-time per-core CPU metrics for the dashboard.

**Mode-independent:** Runs identically in Monitor and Optimize modes. Performance data collection has no dependency on the operating mode.

**Metrics collected (per core):**
- Load percentage (0-100%)
- Current frequency (MHz)
- Temperature (°C, if available via WMI `MSAcpi_ThermalZoneTemperature` or LibreHardwareMonitor interop)

**How:**
- Primary: PDH (Performance Data Helper) counters via P/Invoke
  - `\Processor Information(_Total)\% Processor Utility` for overall
  - `\Processor Information(0,N)\% Processor Utility` for per-core load
  - `\Processor Information(0,N)\Processor Frequency` for per-core MHz
- Fallback: WMI `Win32_PerfFormattedData_PerfOS_Processor`
- Refresh rate: configurable, default 1000ms (1 Hz). Dashboard can request up to 500ms (2 Hz).

**Output:** `CoreSnapshot[]` array, one entry per logical core:
```csharp
public record CoreSnapshot(
    int CoreIndex,
    int CcdIndex,            // 0 or 1
    double LoadPercent,
    double FrequencyMHz,
    double? TemperatureC     // null if unavailable
);
```

**Events:** Raises `SnapshotReady(CoreSnapshot[])` on each refresh cycle for the dashboard to consume.

### 4.3 Process Watcher (`ProcessWatcher.cs`)

**Purpose:** Continuously monitor running processes to detect game launches and exits.

**Mode-independent:** Runs identically in Monitor and Optimize modes. Game detection fires regardless of mode — the AffinityManager decides what to do based on the current mode.

**How:**
- Use `System.Timers.Timer` with configurable interval (default: 2 seconds)
- Enumerate processes via `System.Diagnostics.Process.GetProcesses()`
- Compare against known game list (manual) and auto-detection heuristics
- Raise events: `GameDetected(ProcessInfo)`, `GameExited(ProcessInfo)`
- Track foreground window via `GetForegroundWindow` (P/Invoke)
- Optional: `ManagementEventWatcher` (WMI) for process start/stop events as low-overhead alternative

### 4.4 Game Detector (`GameDetector.cs`)

**Purpose:** Determine whether a running process is a game that should be pinned to the V-Cache CCD.

**Mode-independent:** Detection runs in both modes. In Monitor mode, detected games trigger simulated log entries. In Optimize mode, they trigger real affinity changes.

**Detection methods (priority order):**

1. **Manual list (highest priority):** User-defined list of executable names in config. Match = game. No further checks.

2. **Known games database:** Bundled `known_games.json` of common game executables. Community-contributed, updated with releases.

3. **Auto-detection (lowest priority):** Heuristic-based:
   - Process consuming significant GPU resources (via PDH GPU counters or WMI)
   - Process is the foreground window
   - Process is not in exclusion list

**Exclusion list:** Configurable. Default: `chrome.exe`, `firefox.exe`, `obs64.exe`, `discord.exe`, `spotify.exe`, `devenv.exe`, etc.

### 4.5 Affinity Manager (`AffinityManager.cs`)

**Purpose:** Mode-aware affinity management. In Optimize mode, applies and releases CPU affinity constraints. In Monitor mode, simulates the same logic and emits what-would-happen events.

**Mode awareness:** The AffinityManager receives its operating mode from configuration and checks it before every `SetProcessAffinityMask` call. The event stream has the same structure regardless of mode — only the action type and whether Win32 APIs are called differ.

#### Monitor Mode Behaviour

When a game is detected:
1. Calculate what affinity changes would be made (same logic as Optimize)
2. Emit `WouldEngage` event for the game process
3. Emit `WouldMigrate` events for each background process that would be moved
4. Do NOT call `SetProcessAffinityMask` or any other Win32 affinity API
5. Do NOT store original affinities (nothing to restore)

When a game exits:
1. Emit `WouldRestore` events for each process that would have been restored
2. Clear game tracking state

#### Optimize Mode Behaviour

When a game is detected — engage:
1. Set game process affinity to `VCacheMask`
2. Iterate non-essential background processes, set affinity to `FrequencyMask`
3. Store original affinities in `Dictionary<int, IntPtr>` for restoration
4. Emit `Engaged` / `Migrated` events for each modification

When a game exits — disengage:
1. Restore all modified processes to original affinity
2. Clear game tracking state
3. Emit `Restored` events
4. Log session duration

#### Mode Switch Mid-Game

**Monitor → Optimize (game already detected):**
1. Immediately engage: pin game to VCacheMask, migrate background to FrequencyMask
2. Store original affinities from this point
3. Emit real `Engaged` / `Migrated` events

**Optimize → Monitor (game still running):**
1. Call `RestoreAll` — restore all modified affinities to original values
2. Emit `Restored` events for each process
3. Clear stored affinities
4. Switch to emitting `Would*` events for subsequent game detection cycles

#### Protected Processes (never touched in either mode)

- System processes (PID 0, PID 4, csrss.exe, smss.exe, services.exe, etc.)
- Audio stack (audiodg.exe)
- The optimizer itself
- User-defined protection list

#### Events Emitted

```csharp
public event Action<AffinityEvent> AffinityChanged;

public record AffinityEvent(
    DateTime Timestamp,
    string ProcessName,
    int Pid,
    AffinityAction Action,
    string Detail              // e.g., "→ CCD0 (V-Cache)" or "access denied"
);

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

### 4.6 Dashboard Window (`DashboardForm.cs`)

**Purpose:** The centrepiece of the application. A real-time visual display showing CCD activity, process routing, and action history. This is what makes the tool unique — and in Monitor mode, the dashboard IS the product.

**Layout (top to bottom):**

1. **Status Bar** — Full-width banner at top.
   - Contains a prominent **Monitor / Optimize toggle** (styled as a segmented control or toggle switch)
   - Optimize toggle greyed out with tooltip when `HasVCache == false`
   - Status text changes with mode and state:
     - Monitor + no game: "Monitor — observing CCD activity"
     - Monitor + game detected: "Monitor — observing [game.exe] on CCD0"
     - Optimize + no game: "Optimize — waiting for game"
     - Optimize + game active: "Optimize — [game.exe] pinned to V-Cache CCD | Session: Xh Ym"
   - Warning/error states override: "Topology detection failed", "Auto-detection uncertain"

2. **CCD Panels** — Two side-by-side panels, one per CCD.
   - Header: CCD name, core range, V-Cache/Frequency badge, L3 size
   - Role label: "Gaming — [game.exe]" or "Background — N processes migrated"
   - Core grid: 4×2 grid of core tiles, each showing:
     - Core index (C0, C1, etc.)
     - Load percentage (large text)
     - Current frequency (small text)
     - Color-coded background: green = high load, amber = moderate, neutral = low, dimmed = parked/idle
   - **Border styling by mode:**
     - Monitor + game observed: blue dashed border on the CCD where the game is running
     - Optimize + game engaged: green solid border on V-Cache CCD
     - No game: default border
   - Refresh: 1 Hz default, driven by `PerformanceMonitor.SnapshotReady`

3. **Process Router Table** — Shows active process-to-CCD assignments.
   - Columns: Process name (mono font), CCD assignment badge, CPU usage %
   - Sorted by CPU usage descending
   - Color-coded CCD badges: green for V-Cache, blue for Frequency
   - Updates on every process watcher cycle

4. **Activity Log** — Scrolling feed of timestamped actions.
   - Each entry: timestamp, action type (color-coded), details
   - **Optimize mode entries:**
     - ENGAGE (green), MIGRATE (green), RESTORE (blue), SKIP (amber), ERROR (red)
   - **Monitor mode entries:**
     - `[MONITOR] WOULD ENGAGE` — muted/italic styling, dimmed green
     - `[MONITOR] WOULD MIGRATE` — muted/italic styling, dimmed green
     - `[MONITOR] WOULD RESTORE` — muted/italic styling, dimmed blue
   - Auto-scrolls to newest entry
   - Max display: 200 entries (ring buffer)

5. **Footer** — Static info bar.
   - App version, detected CPU model, polling interval, auto-detection status

**Behaviour:**
- Opens from system tray on double-click or menu → "Dashboard"
- Minimizes to system tray on close (not to taskbar)
- Remembers window position and size between sessions
- Refresh timer pauses when window is minimized (saves CPU)
- DPI-aware: `HighDpiMode.PerMonitorV2`

**Implementation:**
- WinForms `Form` with `TableLayoutPanel` for main layout
- Custom `UserControl` for each CCD panel (reusable for 2-CCD and future 3-CCD)
- `ListView` in virtual mode for process table (handles large process lists efficiently)
- `RichTextBox` or custom control for colored log entries
- `System.Windows.Forms.Timer` (UI thread) for triggering redraws from `PerformanceMonitor` data
- Double-buffered rendering to prevent flicker on refresh

### 4.7 System Tray (`TrayApplication.cs`)

**Purpose:** Persistent system tray presence. Dashboard minimizes here. Mode toggle available in context menu.

**Tray icon states:**
- 🔵 Blue: Monitor mode (observing, no game or game observed)
- 🟣 Purple: Optimize mode, idle (waiting for game)
- 🟢 Green: Optimize mode, active (game engaged)
- 🟡 Yellow: Warning
- 🔴 Red: Error

**Right-click ContextMenuStrip:**
- Status line (disabled, informational — shows current mode and state)
- **"Mode: Monitor" / "Mode: Optimize"** (submenu or toggle item)
- Separator
- "Open Dashboard" / "Show Dashboard"
- Separator
- "Pause Monitoring"
- "Force Engage Now" (pins current foreground app — Optimize mode only, greyed in Monitor)
- Separator
- "Settings..."
- "View Log File..."
- "About"
- Separator
- "Exit"

**Double-click:** Opens/shows Dashboard.

**Balloon tips:** Optional toast notifications on engage/disengage and mode change (configurable).

### 4.8 Settings Window (`SettingsForm.cs`)

**Purpose:** Configuration editor. Separate from Dashboard.

**Tabs:**
- **General:** Polling interval, dashboard refresh rate, start with Windows, minimize to tray on close, notifications, default operating mode
- **Game List:** Add/remove manual game executables. Checkbox list with add/remove buttons.
- **Exclusions:** Add/remove excluded processes
- **Auto-Detection:** Enable/disable, GPU threshold slider, require foreground toggle
- **Advanced:** CCD override (manual core mask entry), log level, protected process list, log file location

### 4.9 Configuration (`AppConfig.cs` + `config.json`)

**Config file location:** `%APPDATA%\X3DCCDOptimizer\config.json`

```json
{
  "version": 3,
  "operationMode": "monitor",
  "pollingIntervalMs": 2000,
  "dashboardRefreshMs": 1000,
  "autoDetection": {
    "enabled": true,
    "gpuThresholdPercent": 50,
    "requireForeground": true
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
    "obs64.exe",
    "discord.exe",
    "spotify.exe"
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
  }
}
```

**`operationMode` field:**
- `"monitor"` (default) — observation only, no affinity changes
- `"optimize"` — active affinity management
- If config says `"optimize"` but `HasVCache == false` at startup, falls back to `"monitor"` with a warning log

### 4.10 Logger (`AppLogger.cs`)

**Implementation:** Serilog with dual sinks:
- **File sink:** Rotating log file, configurable max size
- **Dashboard sink:** Custom Serilog sink that pushes `AffinityEvent` objects to the Dashboard's activity log in real-time

---

## 5. Tech Stack

| Component            | Choice                              | Rationale                                          |
|----------------------|--------------------------------------|----------------------------------------------------|
| Language             | C# 12 / .NET 8                      | Native Windows. First-class Win32/WMI/WinForms.    |
| Target framework     | net8.0-windows                       | LTS, Windows-specific APIs enabled                 |
| UI framework         | WinForms                             | Lightweight, NotifyIcon built-in, good enough for dashboard |
| System tray          | NotifyIcon (WinForms built-in)       | Native tray, no dependency                         |
| Win32 API            | P/Invoke (DllImport)                 | kernel32, user32, pdh.dll direct access            |
| WMI                  | System.Management                    | CPU topology fallback, GPU perf counters           |
| Performance counters | PDH via P/Invoke                     | Per-core load/frequency, lower overhead than WMI   |
| JSON config          | System.Text.Json                     | Built-in, fast                                     |
| Logging              | Serilog + Serilog.Sinks.File         | Rolling files, structured logging, custom sink     |
| Build                | dotnet publish --self-contained      | Single-file .exe, no .NET install needed           |
| Installer            | Inno Setup (optional, post-v1.0)     | Professional installer for public release          |

### Self-Contained Deployment

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Single .exe, ~30-50MB, includes .NET runtime. No prerequisites.

---

## 6. Build Phases

### Phase 1: Foundation + Console Dashboard ✅ COMPLETE

**Goal:** Working core loop with console-based output proving the engine works.

Files:
- `Core/CcdMapper.cs` — topology detection
- `Core/AffinityManager.cs` — set/release affinity via P/Invoke
- `Core/ProcessWatcher.cs` — timer-based polling
- `Core/GameDetector.cs` — manual list matching only
- `Core/PerformanceMonitor.cs` — per-core load/frequency via PDH
- `Config/AppConfig.cs` — load/save JSON config
- `Logging/AppLogger.cs` — Serilog file logging
- `Native/Kernel32.cs` — P/Invoke signatures
- `Native/User32.cs` — P/Invoke signatures
- `Native/Pdh.cs` — PDH P/Invoke signatures
- `Native/Structs.cs` — native struct definitions
- `Program.cs` — console entry point, prints live core stats + affinity actions

**Exit criteria:** Launch the app in console mode → see per-core load/frequency printing → launch Elite Dangerous → see log output confirming game pinned to V-Cache CCD and background processes migrated to CCD1 → close game → see restoration log. All operations verified via Task Manager.

### Phase 2: Monitor Mode + Dashboard Window ← NEXT

**Goal:** Full visual dashboard with Monitor/Optimize dual-mode system as a first-class feature.

Files:
- `DashboardForm.cs` — main dashboard window with all panels and mode toggle
- `UI/CcdPanel.cs` — custom UserControl for CCD core grid (blue dashed / green solid borders)
- `UI/ProcessRouterView.cs` — process-to-CCD assignment list
- `UI/ActivityLogView.cs` — scrolling log with real and simulated entry styling
- `TrayApplication.cs` — system tray with mode-aware icon states and mode toggle menu

Key deliverables:
- Monitor/Optimize toggle in status bar and tray menu
- Mode-aware AffinityManager emitting Would* events in Monitor, real events in Optimize
- `HasVCache` gating on Optimize toggle
- `operationMode` config field persisting across restarts
- Activity log distinguishing real vs simulated actions visually
- Mode switch mid-game (immediate engage or restore)

**Exit criteria:** Launch app → starts in Monitor mode → dashboard shows real-time CCD activity → launch a game → log shows `[MONITOR] WOULD ENGAGE` → toggle to Optimize → log shows real `ENGAGE`, cores light up, processes migrate → toggle back to Monitor → affinities restored → close game → dashboard shows restoration. Tray icon changes colour with mode and state.

### Phase 3: Auto-Detection + Settings

**Goal:** Smart game detection and user-configurable settings.

Additions:
- GPU usage monitoring in `GameDetector.cs`
- Foreground window tracking
- Exclusion list logic
- Known games database (`known_games.json`)
- `SettingsForm.cs` — tabbed settings editor (includes default mode selection)
- Start-with-Windows (Registry key)
- Balloon tip notifications

**Exit criteria:** Launch an unlisted game → optimizer detects it via GPU heuristic → dashboard shows detection → pins correctly in Optimize mode, shows Would* in Monitor mode. Settings changes persist and take effect immediately.

### Phase 4: Polish & Release

**Goal:** Public release quality.

Additions:
- `dotnet publish` single-file self-contained build
- Inno Setup installer (optional)
- README.md with dashboard screenshots showing both modes
- GitHub Actions CI (build + test + publish release artifacts)
- Known games database as updatable sidecar file
- CONTRIBUTING.md
- Icon set (proper .ico files for all tray states)

**Exit criteria:** Clean install on a fresh Windows 11 machine, works end-to-end, dashboard looks polished in both modes, no Python or .NET install required.

---

## 7. Project Structure

```
x3d-ccd-optimizer/
├── src/
│   └── X3DCcdOptimizer/
│       ├── X3DCcdOptimizer.csproj
│       ├── Program.cs                    # Entry point
│       ├── TrayApplication.cs            # System tray NotifyIcon + mode toggle menu
│       ├── DashboardForm.cs              # Main dashboard window + mode toggle
│       │
│       ├── Core/
│       │   ├── CcdMapper.cs              # CCD topology detection (HasVCache output)
│       │   ├── PerformanceMonitor.cs      # Per-core load/freq/temp (mode-independent)
│       │   ├── ProcessWatcher.cs          # Process monitoring loop (mode-independent)
│       │   ├── GameDetector.cs            # Game identification logic (mode-independent)
│       │   └── AffinityManager.cs         # Mode-aware CPU affinity operations
│       │
│       ├── UI/
│       │   ├── CcdPanel.cs               # CCD core grid UserControl
│       │   ├── ProcessRouterView.cs       # Process-to-CCD list
│       │   ├── ActivityLogView.cs         # Scrolling colored log (real + simulated styling)
│       │   └── SettingsForm.cs            # Settings editor window
│       │
│       ├── Config/
│       │   ├── AppConfig.cs               # Configuration model + I/O (operationMode field)
│       │   └── DefaultConfig.cs           # Embedded defaults
│       │
│       ├── Logging/
│       │   ├── AppLogger.cs               # Serilog setup
│       │   └── DashboardSink.cs           # Custom sink → dashboard log
│       │
│       ├── Native/
│       │   ├── Kernel32.cs                # P/Invoke: affinity, topology
│       │   ├── User32.cs                  # P/Invoke: foreground window
│       │   ├── Pdh.cs                     # P/Invoke: performance counters
│       │   └── Structs.cs                 # Native struct definitions
│       │
│       ├── Models/
│       │   ├── CpuTopology.cs             # Topology data model (includes HasVCache)
│       │   ├── CoreSnapshot.cs            # Per-core metrics snapshot
│       │   └── AffinityEvent.cs           # Affinity change event model (real + Would* actions)
│       │
│       ├── Data/
│       │   └── known_games.json           # Bundled game database
│       │
│       └── Assets/
│           ├── icon_monitor.ico           # Blue — Monitor mode
│           ├── icon_optimize_idle.ico     # Purple — Optimize, waiting
│           ├── icon_optimize_active.ico   # Green — Optimize, game engaged
│           ├── icon_warning.ico           # Yellow — Warning
│           └── icon_error.ico             # Red — Error
│
├── tests/
│   └── X3DCcdOptimizer.Tests/
│       ├── X3DCcdOptimizer.Tests.csproj
│       ├── CcdMapperTests.cs
│       ├── GameDetectorTests.cs
│       ├── AffinityManagerTests.cs        # Tests for both Monitor and Optimize paths
│       ├── PerformanceMonitorTests.cs
│       └── AppConfigTests.cs
│
├── .github/
│   └── workflows/
│       └── ci.yml
│
├── X3DCcdOptimizer.sln
├── X3D_CCD_OPTIMIZER_BLUEPRINT.md         # This file — project spec
├── CLAUDE.md                              # AI assistant instructions
├── README.md
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

    internal const uint PROCESS_SET_INFORMATION = 0x0200;
    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
}

// Native/User32.cs
internal static class User32
{
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}

// Native/Pdh.cs
internal static class Pdh
{
    [DllImport("pdh.dll")]
    internal static extern int PdhOpenQuery(string szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll")]
    internal static extern int PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll")]
    internal static extern int PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll")]
    internal static extern int PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out IntPtr lpdwType, out PDH_FMT_COUNTERVALUE pValue);

    [DllImport("pdh.dll")]
    internal static extern int PdhCloseQuery(IntPtr hQuery);

    internal const uint PDH_FMT_DOUBLE = 0x00000200;
}
```

---

## 9. Dashboard Design Specification

### Color Coding

**Core tiles (load-based):**
- 0-15% load: neutral background (default surface color)
- 16-40% load: amber tint (moderate activity)
- 41-100% load: green tint (high activity, healthy)
- Parked/idle (0% + no frequency): dimmed/faded (35% opacity)

**CCD panel borders:**
- Monitor + game observed on this CCD: blue dashed border (observation, not control)
- Optimize + game engaged on this CCD: green solid border (active control)
- No game / other CCD: default border

**Status bar:**
- Monitor mode, no game: blue/info background
- Monitor mode, game observed: blue background with game name
- Optimize mode, game active: green background
- Optimize mode, idle: neutral/default background
- Warning: amber background
- Error: red background

**Process CCD badges:**
- V-Cache CCD: green badge
- Frequency CCD: blue badge

**Log action types (Optimize mode):**
- ENGAGE: green text
- MIGRATE: green text
- RESTORE: blue text
- SKIP: amber text
- ERROR: red text

**Log action types (Monitor mode):**
- [MONITOR] WOULD ENGAGE: dimmed green text, italic
- [MONITOR] WOULD MIGRATE: dimmed green text, italic
- [MONITOR] WOULD RESTORE: dimmed blue text, italic

### Refresh Rates

| Component | Default | Range | Source |
|-----------|---------|-------|--------|
| Core load/frequency | 1000ms | 500-5000ms | PerformanceMonitor |
| Process list | 2000ms | 1000-10000ms | ProcessWatcher |
| Activity log | Immediate | Event-driven | AffinityManager events |
| Status bar | Immediate | Event-driven | State changes |

### Window Behaviour

- Default size: 800×700px
- Minimum size: 640×500px
- Remembers position/size in config
- Close button minimizes to tray (configurable)
- Double-buffered rendering (no flicker)
- DPI-aware: PerMonitorV2
- High contrast / accessibility: uses system colors where possible

---

## 10. Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| Anticheat flags affinity changes | Game crash or ban | **Monitor mode is the default.** Users observe first. Per-game toggle. Only OS-level affinity. Document known anticheat interactions. |
| Process exits mid-affinity-change | Harmless exception | try/catch everything. Log and continue. |
| Non-X3D dual-CCD CPU | App on non-target hardware | **Monitor mode works on any dual-CCD Ryzen.** Optimize toggle is disabled when `HasVCache == false`. |
| User enables Optimize on non-X3D | Incorrect affinity routing | Optimize toggle greyed out when `HasVCache == false`. Cannot be enabled via UI. Config fallback to Monitor with warning if manually edited. |
| GPU auto-detection false positives | Wrong process pinned | Manual list overrides auto. Exclusion list. Monitor mode shows what would be pinned without acting. |
| CCD ordering changes | Wrong CCD gets game | Detect by L3 size, not CCD number. Manual override. |
| Windows permission errors | Can't set some affinities | Expected for system processes. Log, skip, continue. Monitor mode unaffected (never calls Win32 affinity APIs). |
| High-DPI/ultrawide scaling | Blurry dashboard | PerMonitorV2 DPI mode. Test on ultrawide. |
| PDH counter not available | No per-core metrics | Fallback to WMI. Degrade gracefully (show "N/A"). |
| Dashboard refresh drains CPU | Tool causes the problem it solves | Pause refresh when minimized. Configurable rate. |
| Mode switch mid-game causes stutter | Brief performance disruption | Affinity changes are fast (< 1ms per process). Acceptable trade-off for user-initiated action. |

---

## 11. Compatibility

### Monitor Mode

- **OS:** Windows 10 21H2+, Windows 11
- **CPU:** Any AMD dual-CCD Ryzen processor (Zen 4 or Zen 5). Single-CCD shows per-core metrics only.
- **Runtime:** Self-contained .exe, no install required
- **Architecture:** x64 only
- **Display:** Tested on standard and ultrawide, DPI-aware
- **Privileges:** Standard user. No elevated permissions required.

### Optimize Mode

- **CPU:** AMD dual-CCD X3D processors only (7950X3D, 7900X3D, 9950X3D, 9900X3D)
- **Requirement:** V-Cache CCD confirmed via topology detection (`HasVCache == true`)
- **Privileges:** Standard user. System processes gracefully skipped.
- All other requirements same as Monitor mode.

---

## 12. What This Tool Is NOT

- **Not just an optimizer.** In Monitor mode, it's a real-time CCD diagnostic tool that works on any dual-CCD Ryzen.
- **Not a kernel driver.** Works at process affinity level only.
- **Not a replacement for AMD's chipset drivers.** Supplements, not replaces.
- **Not a general-purpose process manager.** One job: CCD visibility and affinity for dual-CCD Ryzen.
- **Not a hardware monitor.** Dashboard shows routing-relevant metrics only, not full system monitoring.
- **Not affiliated with AMD.** Independent open-source project.

---

## 13. Future Possibilities (Post v1.0)

- Per-game profiles (different background routing per game)
- Session statistics (time in game mode, CCD utilization history, graphs)
- Game database auto-update from GitHub
- Steam/Epic/GOG library integration
- Community game database (web endpoint for submissions)
- WPF or MAUI upgrade for richer dashboard visuals
- Power plan integration (auto-switch on engage)
- Temperature monitoring integration (LibreHardwareMonitor)
- Export session logs to CSV
- Portable mode (config next to .exe instead of %APPDATA%)
- Parking health check — automated report on whether AMD's built-in CCD parking is functioning correctly, comparing observed core assignment against expected behaviour

---

*This document is the single source of truth for the X3D Dual CCD Optimizer project. All implementation decisions reference this blueprint.*
