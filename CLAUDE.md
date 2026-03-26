# CLAUDE.md — X3D Dual CCD Optimizer

## Project Overview

X3D Dual CCD Optimizer is a lightweight open-source Windows application with two operating modes for AMD dual-CCD Ryzen processors:

- **Monitor Mode (default):** Real-time CCD dashboard showing per-core load, frequency, process-to-CCD mapping, and game detection — without touching any process affinity. Works on any dual-CCD Ryzen.
- **Optimize Mode (opt-in):** Everything Monitor does, plus active CPU affinity management — pins games to the V-Cache CCD and migrates background processes to the frequency CCD. Requires confirmed V-Cache detection (X3D processors only).

## Architecture

The app has two layers:
- **Core Engine** — topology detection, process monitoring, game detection, mode-aware affinity management
- **UI Layer** — system tray (NotifyIcon) + dashboard window (WinForms) — Phase 2+

All design decisions and module specs are in `X3D_CCD_OPTIMIZER_BLUEPRINT.md` at the project root. This is the single source of truth. Read it before making architectural changes.

### Operating Modes

The application operates in Monitor or Optimize mode at all times. Mode is stored in config as `operationMode` and persists across restarts.

**Mode-independent modules** (run identically in both modes):
- `CcdMapper` — topology detection
- `PerformanceMonitor` — per-core load/frequency collection
- `ProcessWatcher` — process polling and game detection events
- `GameDetector` — game identification (manual list, known DB, auto-detection)

**Mode-aware module:**
- `AffinityManager` — checks `operationMode` before every `SetProcessAffinityMask` call
  - **Monitor mode:** emits `WouldEngage`, `WouldMigrate`, `WouldRestore` events. Never calls Win32 affinity APIs.
  - **Optimize mode:** emits `Engaged`, `Migrated`, `Restored` events. Calls `SetProcessAffinityMask` to modify process affinity.

The event stream structure is identical in both modes — only the `AffinityAction` type and whether Win32 APIs are called differ. This means the dashboard, logging, and any future consumers work unchanged regardless of mode.

**Mode switching mid-game:**
- Monitor → Optimize: immediately engages (pins game, migrates background)
- Optimize → Monitor: immediately restores all affinities to original values

**Optimize toggle gating:** The Optimize toggle is disabled (greyed out) when `CpuTopology.HasVCache == false`. If config says `"optimize"` but V-Cache is not detected at startup, the app falls back to Monitor with a warning.

## Tech Stack

- **Language:** C# 12 / .NET 8
- **Target:** `net8.0-windows` (Windows-specific APIs required)
- **UI:** WinForms (NotifyIcon for tray, Forms for dashboard/settings)
- **Win32 API:** P/Invoke via `DllImport` in `Native/` folder
- **Performance counters:** PDH (Performance Data Helper) via P/Invoke
- **WMI:** `System.Management` for CPU topology fallback and GPU detection
- **Config:** `System.Text.Json`, stored at `%APPDATA%\X3DCCDOptimizer\config.json`
- **Logging:** Serilog with console + file sinks
- **Distribution:** Self-contained single-file publish (`dotnet publish -r win-x64 --self-contained`)

## Project Structure

```
x3d-ccd-optimizer/
├── src/X3DCcdOptimizer/
│   ├── Program.cs                 # Entry point
│   ├── Core/                      # Engine modules
│   │   ├── CcdMapper.cs           # CCD topology detection (HasVCache output)
│   │   ├── PerformanceMonitor.cs  # Per-core load/freq via PDH (mode-independent)
│   │   ├── ProcessWatcher.cs      # Process polling + game detection (mode-independent)
│   │   ├── GameDetector.cs        # Game identification (mode-independent)
│   │   └── AffinityManager.cs     # Mode-aware affinity management
│   ├── UI/                        # Dashboard + settings (Phase 2+)
│   ├── Config/AppConfig.cs        # JSON config model + I/O (includes operationMode)
│   ├── Logging/AppLogger.cs       # Serilog setup
│   ├── Native/                    # P/Invoke declarations
│   │   ├── Kernel32.cs            # Affinity, topology APIs
│   │   ├── User32.cs              # Foreground window APIs
│   │   ├── Pdh.cs                 # Performance counter APIs
│   │   └── Structs.cs             # Native struct definitions
│   ├── Models/                    # Shared data types
│   │   ├── CpuTopology.cs         # Includes HasVCache bool
│   │   ├── CoreSnapshot.cs
│   │   └── AffinityEvent.cs       # AffinityAction enum with real + Would* values
│   └── Data/known_games.json      # Bundled game executable database
├── tests/X3DCcdOptimizer.Tests/
├── X3DCcdOptimizer.sln
├── X3D_CCD_OPTIMIZER_BLUEPRINT.md # Full project spec — READ THIS
└── README.md
```

## Build Phases

- **Phase 1 (COMPLETE):** Core engine with console output. Topology detection, affinity management, per-core monitoring, manual game list.
- **Phase 2 (NEXT):** Monitor Mode + Dashboard Window — mode toggle as first-class UI, real-time CCD panels, process router table, activity log with real/simulated styling, system tray with mode-aware icons.
- **Phase 3:** Auto-detection via GPU heuristics, settings UI, start-with-Windows.
- **Phase 4:** Polish, single-file build, installer, CI/CD.

## Coding Conventions

- **Nullable reference types** are enabled. Respect them.
- **All P/Invoke calls** must have `SetLastError = true` and proper error checking via `Marshal.GetLastWin32Error()`.
- **All process operations** must be wrapped in try/catch. Never crash on a single process failure. Access denied is expected for system processes — log and skip.
- **Events over callbacks.** Modules communicate via C# events (e.g., `GameDetected`, `AffinityChanged`, `SnapshotReady`).
- **IDisposable** for anything holding unmanaged resources (PDH query handles, timers).
- **Log every significant action** at INFO level, every failure at WARNING or ERROR.
- **No unnecessary dependencies.** Prefer built-in .NET APIs and P/Invoke over third-party NuGet packages.
- **Case-insensitive** process name matching everywhere.
- **AffinityManager must check `operationMode` before every `SetProcessAffinityMask` call.** In Monitor mode, emit `Would*` events instead of calling Win32 APIs. Never skip this check.

## Key Technical Details

### CCD Topology Detection
Uses `GetLogicalProcessorInformationEx` with `RelationshipCache` to enumerate L3 caches per CCD. V-Cache CCD has 96MB L3 vs 32MB on the standard CCD. Detection is by cache size, NOT by CCD number (in case ordering changes with driver updates). WMI fallback via `Win32_CacheMemory`. Manual override available in config. Outputs `HasVCache` bool used to gate the Optimize mode toggle.

### Affinity Management (Mode-Aware)

The AffinityManager is the only mode-aware module. It checks the current `operationMode` before every affinity operation:

- **Monitor mode:** Runs the same detection and routing logic, but emits `WouldEngage`, `WouldMigrate`, `WouldRestore` events instead of calling `SetProcessAffinityMask`. No Win32 affinity APIs are invoked. No original affinities are stored (nothing to restore).
- **Optimize mode:** Calls `SetProcessAffinityMask` via P/Invoke. Two bitmasks: `VCacheMask` (V-Cache cores) and `FrequencyMask` (standard cores). On game detect: pin game to VCacheMask, migrate background to FrequencyMask. On game exit: restore all original masks from stored dictionary. Emits `Engaged`, `Migrated`, `Restored` events.

**AffinityAction enum (both real and simulated):**
```csharp
public enum AffinityAction
{
    Engaged,          // Optimize: game pinned to V-Cache CCD
    Migrated,         // Optimize: background moved to Frequency CCD
    Restored,         // Optimize: affinity restored after game exit
    Skipped,          // Both: process was protected/inaccessible
    Error,            // Both: operation failed

    WouldEngage,      // Monitor: would have pinned game
    WouldMigrate,     // Monitor: would have moved process
    WouldRestore      // Monitor: would have restored affinity
}
```

Protected processes (system, audio, self) are never touched in either mode.

### Performance Monitoring
PDH counters for per-core load (`\Processor Information(0,N)\% Processor Utility`) and frequency (`\Processor Information(0,N)\Processor Frequency`). Timer-driven collection, default 1Hz. Emits `CoreSnapshot[]` arrays via events for dashboard consumption. Mode-independent — runs identically regardless of operating mode.

### Game Detection Priority
1. Manual list (config) — highest priority, always wins
2. Known games database (known_games.json) — community-maintained
3. Auto-detection via GPU usage heuristic (Phase 3) — lowest priority

Game detection is mode-independent. The AffinityManager decides what to do with the detection based on the current mode.

## Commands

```bash
# Build
dotnet build

# Run (console mode)
dotnet run --project src/X3DCcdOptimizer

# Run tests
dotnet test

# Publish self-contained single file
dotnet publish src/X3DCcdOptimizer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Important Notes

- **Monitor mode is the default.** It works on any dual-CCD AMD Ryzen processor. Optimize mode requires V-Cache confirmation and explicit user opt-in.
- This is a **Windows-only** tool. Linux has a proper kernel-level solution (`amd_x3d_vcache` driver).
- This is **not a kernel driver**. It works at the userspace process affinity level only.
- It **supplements** AMD's chipset drivers, not replaces them. Users should keep chipset drivers updated.
- The target audience starts with dual-CCD Ryzen owners who want CCD visibility (Monitor mode), and extends to X3D owners who want active affinity management (Optimize mode).
- The dashboard is the centrepiece — it's what makes this tool unique. Refer to the blueprint Section 9 for the full dashboard design spec.
- **License is GPL v2.** All contributions must be compatible.
