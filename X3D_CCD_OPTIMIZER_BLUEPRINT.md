# X3D Dual CCD Optimizer — Project Blueprint

**Version:** 0.2.0-draft
**Author:** Carlo Benedetti
**License:** GPL v2
**Repository:** github.com/LordBlacksun/x3d-ccd-optimizer
**Status:** Pre-build

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

**X3D Dual CCD Optimizer fills this gap** — and goes further by providing a real-time visual dashboard that makes CCD activity transparent, something no existing tool offers.

---

## 2. Project Vision

A lightweight, open-source Windows application that intelligently manages CPU core affinity on dual-CCD X3D processors AND provides a real-time visual dashboard showing exactly what each CCD and core is doing. When a game is detected, it pins the game to the V-Cache CCD and migrates background processes to the frequency CCD. When the game closes, all constraints are released. Every action is visible in the dashboard.

### Design Principles

1. **Transparency first** — The dashboard is the product. Users should see exactly what their CPU is doing, which CCD is handling what, and every action the optimizer takes. No black box.
2. **Zero-configuration by default** — Auto-detects CCD topology, auto-detects games. Works out of the box.
3. **Manual override always available** — User-defined game list as fallback. Manual always wins over auto.
4. **Minimal footprint** — System tray when minimized. Dashboard on demand. Near-zero CPU/RAM when idle.
5. **No kernel access required** — Userspace only. Uses standard Windows APIs. No driver signing needed.
6. **Open source from day one** — GPL v2, public repo, community contributions welcome.

---

## 3. Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                   Dashboard Window                       │
│  ┌─────────────┐ ┌─────────────┐ ┌──────────────────┐  │
│  │  CCD0 Panel │ │  CCD1 Panel │ │  Process Router  │  │
│  │  (per-core  │ │  (per-core  │ │  (which process  │  │
│  │   load/freq │ │   load/freq │ │   on which CCD)  │  │
│  │   heatmap)  │ │   heatmap)  │ │                  │  │
│  └──────┬──────┘ └──────┬──────┘ └────────┬─────────┘  │
│         │               │                  │            │
│  ┌──────▼───────────────▼──────────────────▼─────────┐  │
│  │                  Activity Log                      │  │
│  │    (timestamped feed of every action taken)        │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │                   Status Bar                       │  │
│  │   (mode indicator, CPU model, session timer)       │  │
│  └────────────────────────────────────────────────────┘  │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│                System Tray (NotifyIcon)                   │
│           (minimized state, quick menu)                   │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│                     Core Engine                          │
│                                                          │
│  ┌──────────────┐  ┌──────────────────────────────────┐ │
│  │  CCD Mapper  │  │       Process Watcher            │ │
│  │  (topology   │  │  (polling loop, game detection)  │ │
│  │   detection) │  │                                  │ │
│  └──────┬───────┘  └──────────────┬───────────────────┘ │
│         │                         │                      │
│  ┌──────▼─────────────────────────▼───────────────────┐ │
│  │              Affinity Manager                       │ │
│  │     (SetProcessAffinityMask via P/Invoke)           │ │
│  └─────────────────────────────────────────────────────┘ │
│                                                          │
│  ┌─────────────────────────────────────────────────────┐ │
│  │              Game Detector                          │ │
│  │  (auto: GPU usage via PDH / WMI)                    │ │
│  │  (manual: user-defined executable list)             │ │
│  │  (known games database)                             │ │
│  │  (manual always overrides auto)                     │ │
│  └─────────────────────────────────────────────────────┘ │
│                                                          │
│  ┌─────────────────────────────────────────────────────┐ │
│  │           Performance Monitor                       │ │
│  │  (per-core load %, frequency, temperature)          │ │
│  │  (via PDH counters / WMI)                           │ │
│  │  (feeds dashboard in real-time)                     │ │
│  └─────────────────────────────────────────────────────┘ │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│                    Configuration                         │
│            (JSON config via System.Text.Json)             │
└─────────────────────────────────────────────────────────┘
```

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
  - `VCacheL3Size` (int) — L3 size in MB for V-Cache CCD
  - `StandardL3Size` (int) — L3 size in MB for standard CCD

**Supported processors:**
- Ryzen 9 7950X3D (8+8 cores, CCD0 = V-Cache)
- Ryzen 9 7900X3D (6+6 cores, CCD0 = V-Cache)
- Ryzen 9 9950X3D (8+8 cores, CCD0 = V-Cache)
- Ryzen 9 9900X3D (6+6 cores, CCD0 = V-Cache)

**Startup check:** If the CPU is not a recognized dual-CCD X3D, display a warning dialog and exit gracefully.

### 4.2 Performance Monitor (`PerformanceMonitor.cs`)

**Purpose:** Collect real-time per-core CPU metrics for the dashboard. This is a new module that feeds the dashboard's visual panels.

**Metrics collected (per core):**
- Load percentage (0-100%)
- Current frequency (GHz)
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
    int CcdIndex,         // 0 or 1
    float LoadPercent,
    float FrequencyGHz,
    float? TemperatureC   // null if unavailable
);
```

**Events:** Raises `SnapshotReady(CoreSnapshot[])` on each refresh cycle for the dashboard to consume.

### 4.3 Process Watcher (`ProcessWatcher.cs`)

**Purpose:** Continuously monitor running processes to detect game launches and exits.

**How:**
- Use `System.Timers.Timer` with configurable interval (default: 2 seconds)
- Enumerate processes via `System.Diagnostics.Process.GetProcesses()`
- Compare against known game list (manual) and auto-detection heuristics
- Raise events: `GameDetected(ProcessInfo)`, `GameExited(ProcessInfo)`
- Track foreground window via `GetForegroundWindow` (P/Invoke)
- Optional: `ManagementEventWatcher` (WMI) for process start/stop events as low-overhead alternative

### 4.4 Game Detector (`GameDetector.cs`)

**Purpose:** Determine whether a running process is a game that should be pinned to the V-Cache CCD.

**Detection methods (priority order):**

1. **Manual list (highest priority):** User-defined list of executable names in config. Match = game. No further checks.

2. **Known games database:** Bundled `known_games.json` of common game executables. Community-contributed, updated with releases.

3. **Auto-detection (lowest priority):** Heuristic-based:
   - Process consuming significant GPU resources (via PDH GPU counters or WMI)
   - Process is the foreground window
   - Process is not in exclusion list

**Exclusion list:** Configurable. Default: `chrome.exe`, `firefox.exe`, `obs64.exe`, `discord.exe`, `spotify.exe`, `devenv.exe`, etc.

### 4.5 Affinity Manager (`AffinityManager.cs`)

**Purpose:** Apply and release CPU affinity constraints.

**Game detected — engage:**
1. Set game process affinity to `VCacheMask`
2. Iterate non-essential background processes, set affinity to `FrequencyMask`
3. Store original affinities in `Dictionary<int, IntPtr>` for restoration
4. Emit `AffinityChanged` events for each modification (consumed by dashboard log)

**Protected processes (never touched):**
- System processes (PID 0, PID 4, csrss.exe, smss.exe, services.exe, etc.)
- Audio stack (audiodg.exe)
- The optimizer itself
- User-defined protection list

**Game exited — disengage:**
1. Restore all modified processes to original affinity
2. Clear game tracking state
3. Emit `AffinityRestored` events
4. Log session duration

**Events emitted (for dashboard consumption):**
```csharp
public event Action<AffinityEvent> AffinityChanged;

public record AffinityEvent(
    DateTime Timestamp,
    string ProcessName,
    int Pid,
    AffinityAction Action,    // Engaged, Migrated, Restored, Skipped
    string Detail              // e.g., "→ CCD1 (was: all cores)" or "access denied"
);
```

### 4.6 Dashboard Window (`DashboardForm.cs`)

**Purpose:** The centrepiece of the application. A real-time visual display showing CCD activity, process routing, and action history. This is what makes the tool unique.

**Layout (top to bottom):**

1. **Status Bar** — Full-width banner at top.
   - Green when game mode active: "Game mode active — [game.exe] pinned to V-Cache CCD | Session: Xh Ym"
   - Blue when idle: "Monitoring — no game detected"
   - Yellow on warning: "Auto-detection uncertain"
   - Red on error: "Topology detection failed"

2. **CCD Panels** — Two side-by-side panels, one per CCD.
   - Header: CCD name, core range, V-Cache/Frequency badge, L3 size
   - Role label: "Gaming — [game.exe]" or "Background — N processes migrated"
   - Core grid: 4×2 grid of core tiles, each showing:
     - Core index (C0, C1, etc.)
     - Load percentage (large text)
     - Current frequency (small text)
     - Color-coded background: green = high load, amber = moderate, neutral = low, dimmed = parked/idle
   - Refresh: 1 Hz default, driven by `PerformanceMonitor.SnapshotReady`

3. **Process Router Table** — Shows active process-to-CCD assignments.
   - Columns: Process name (mono font), CCD assignment badge, CPU usage %
   - Sorted by CPU usage descending
   - Color-coded CCD badges: green for V-Cache, blue for Frequency
   - Updates on every process watcher cycle

4. **Activity Log** — Scrolling feed of timestamped actions.
   - Each entry: timestamp, action type (color-coded), details
   - Action types: ENGAGE (green), MIGRATE (green), RESTORE (blue), SKIP (amber), ERROR (red)
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

**Purpose:** Persistent system tray presence. Dashboard minimizes here.

**Tray icon states:**
- 🟢 Green: Game mode active
- 🔵 Blue: Idle, monitoring
- 🟡 Yellow: Warning
- 🔴 Red: Error

**Right-click ContextMenuStrip:**
- Status line (disabled, informational)
- "Open Dashboard" / "Show Dashboard"
- Separator
- "Pause Monitoring"
- "Force Engage Now" (pins current foreground app)
- Separator
- "Settings..."
- "View Log File..."
- "About"
- Separator
- "Exit"

**Double-click:** Opens/shows Dashboard.

**Balloon tips:** Optional toast notifications on engage/disengage (configurable).

### 4.8 Settings Window (`SettingsForm.cs`)

**Purpose:** Configuration editor. Separate from Dashboard.

**Tabs:**
- **General:** Polling interval, dashboard refresh rate, start with Windows, minimize to tray on close, notifications
- **Game List:** Add/remove manual game executables. Checkbox list with add/remove buttons.
- **Exclusions:** Add/remove excluded processes
- **Auto-Detection:** Enable/disable, GPU threshold slider, require foreground toggle
- **Advanced:** CCD override (manual core mask entry), log level, protected process list, log file location

### 4.9 Configuration (`AppConfig.cs` + `config.json`)

**Config file location:** `%APPDATA%\X3DCCDOptimizer\config.json`

```json
{
  "version": 2,
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

### Phase 1: Foundation + Console Dashboard

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

### Phase 2: Dashboard Window

**Goal:** Full visual dashboard replacing console output.

Files:
- `DashboardForm.cs` — main dashboard window with all panels
- `UI/CcdPanel.cs` — custom UserControl for CCD core grid
- `UI/ProcessRouterView.cs` — process-to-CCD assignment list
- `UI/ActivityLogView.cs` — scrolling log with colored entries
- `TrayApplication.cs` — system tray with NotifyIcon

**Exit criteria:** Launch app → dashboard opens showing real-time CCD activity → launch a game → dashboard shows game mode engaged, cores light up, processes migrate visually → close game → dashboard shows restoration. System tray icon changes color with state.

### Phase 3: Auto-Detection + Settings

**Goal:** Smart game detection and user-configurable settings.

Additions:
- GPU usage monitoring in `GameDetector.cs`
- Foreground window tracking
- Exclusion list logic
- Known games database (`known_games.json`)
- `SettingsForm.cs` — tabbed settings editor
- Start-with-Windows (Registry key)
- Balloon tip notifications

**Exit criteria:** Launch an unlisted game → optimizer detects it via GPU heuristic → dashboard shows detection → pins correctly. Settings changes persist and take effect immediately.

### Phase 4: Polish & Release

**Goal:** Public release quality.

Additions:
- `dotnet publish` single-file self-contained build
- Inno Setup installer (optional)
- README.md with dashboard screenshots
- GitHub Actions CI (build + test + publish release artifacts)
- Known games database as updatable sidecar file
- CONTRIBUTING.md
- Icon set (proper .ico files for tray states)

**Exit criteria:** Clean install on a fresh Windows 11 machine, works end-to-end, dashboard looks polished, no Python or .NET install required.

---

## 7. Project Structure

```
x3d-ccd-optimizer/
├── src/
│   └── X3DCcdOptimizer/
│       ├── X3DCcdOptimizer.csproj
│       ├── Program.cs                    # Entry point
│       ├── TrayApplication.cs            # System tray NotifyIcon + menu
│       ├── DashboardForm.cs              # Main dashboard window
│       │
│       ├── Core/
│       │   ├── CcdMapper.cs              # CCD topology detection
│       │   ├── PerformanceMonitor.cs      # Per-core load/freq/temp
│       │   ├── ProcessWatcher.cs          # Process monitoring loop
│       │   ├── GameDetector.cs            # Game identification logic
│       │   └── AffinityManager.cs         # CPU affinity operations
│       │
│       ├── UI/
│       │   ├── CcdPanel.cs               # CCD core grid UserControl
│       │   ├── ProcessRouterView.cs       # Process-to-CCD list
│       │   ├── ActivityLogView.cs         # Scrolling colored log
│       │   └── SettingsForm.cs            # Settings editor window
│       │
│       ├── Config/
│       │   ├── AppConfig.cs               # Configuration model + I/O
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
│       │   ├── CpuTopology.cs             # Topology data model
│       │   ├── CoreSnapshot.cs            # Per-core metrics snapshot
│       │   └── AffinityEvent.cs           # Affinity change event model
│       │
│       ├── Data/
│       │   └── known_games.json           # Bundled game database
│       │
│       └── Assets/
│           ├── icon_idle.ico
│           ├── icon_active.ico
│           ├── icon_warning.ico
│           └── icon_error.ico
│
├── tests/
│   └── X3DCcdOptimizer.Tests/
│       ├── X3DCcdOptimizer.Tests.csproj
│       ├── CcdMapperTests.cs
│       ├── GameDetectorTests.cs
│       ├── AffinityManagerTests.cs
│       ├── PerformanceMonitorTests.cs
│       └── AppConfigTests.cs
│
├── .github/
│   └── workflows/
│       └── ci.yml
│
├── X3DCcdOptimizer.sln
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
- Active (game assigned): green border
- Inactive: default border

**Status bar:**
- Game mode active: green background
- Idle monitoring: blue/info background
- Warning: amber background
- Error: red background

**Process CCD badges:**
- V-Cache CCD: green badge
- Frequency CCD: blue badge

**Log action types:**
- ENGAGE: green text
- MIGRATE: green text
- RESTORE: blue text
- SKIP: amber text
- ERROR: red text

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
| Anticheat flags affinity changes | Game crash or ban | Document behaviour. Per-game toggle. Only OS-level affinity. |
| Process exits mid-affinity-change | Harmless exception | try/catch everything. Log and continue. |
| Non-X3D CPU | App on wrong hardware | Topology check at startup. Warning dialog and exit. |
| GPU auto-detection false positives | Wrong process pinned | Manual list overrides auto. Exclusion list. |
| CCD ordering changes | Wrong CCD gets game | Detect by L3 size, not CCD number. Manual override. |
| Windows permission errors | Can't set some affinities | Expected for system processes. Log, skip, continue. |
| High-DPI/ultrawide scaling | Blurry dashboard | PerMonitorV2 DPI mode. Test on ultrawide. |
| PDH counter not available | No per-core metrics | Fallback to WMI. Degrade gracefully (show "N/A"). |
| Dashboard refresh drains CPU | Tool causes the problem it solves | Pause refresh when minimized. Configurable rate. |

---

## 11. Compatibility

- **OS:** Windows 10 21H2+, Windows 11
- **CPU:** Any AMD dual-CCD X3D processor (Zen 4 or Zen 5)
- **Runtime:** Self-contained .exe, no install required
- **Architecture:** x64 only
- **Display:** Tested on standard and ultrawide, DPI-aware
- **Privileges:** Standard user. System processes gracefully skipped.

---

## 12. What This Tool Is NOT

- **Not a kernel driver.** Works at process affinity level only.
- **Not a replacement for AMD's chipset drivers.** Supplements, not replaces.
- **Not a general-purpose process manager.** One job: CCD affinity for X3D.
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

---

*This document is the single source of truth for the X3D Dual CCD Optimizer project. All implementation decisions reference this blueprint.*
