# Benchmark Research & Migration Regression Findings

Investigation from Session 62 (2026-03-30) into optimize mode performance regression observed with the FFXIV Dawntrail benchmark on AMD Ryzen 9 7950X3D.

---

## Test Environment

- **CPU:** AMD Ryzen 9 7950X3D (16 cores / 32 threads, dual-CCD)
- **CCD0:** Cores 0-15, 96MB L3 (V-Cache), VF limit fused at 5250 MHz, Mask: `0xFFFF`
- **CCD1:** Cores 16-31, 32MB L3 (Frequency), ~5700 MHz max boost, Mask: `0xFFFF0000`
- **Tier:** DualCcdX3D
- **AMD Driver:** amd3dvcache driver present, default `PREFER_FREQ` (DefaultType=0)

## Benchmark Results

| Mode | Score | Delta | What happened |
|------|-------|-------|---------------|
| Monitor (baseline) | ~34,000 | — | No intervention. AMD default `PREFER_FREQ`. OS scheduler handles placement. |
| AffinityPinning | ~31,000 | -8.8% | Game hard-pinned to V-Cache CCD + ~166 background processes migrated to Frequency CCD |
| DriverPreference (old code) | ~31,000 | -8.8% | AMD registry set to `PREFER_CACHE` + ~166 background processes migrated to Frequency CCD |

Both optimize strategies produced identical ~9% regressions compared to doing nothing.

## Clock Speed Analysis

Initial hypothesis was that the V-Cache CCD's lower clock speed explained the regression. This was ruled out:

- CCD0 VF limit: 5250 MHz (fused silicon limit, not guaranteed boost)
- CCD1 max boost: ~5700 MHz (advertised single-core, not sustained)
- Actual multi-core boost during a gaming workload is well below these limits on both CCDs
- The real-world clock delta between CCDs during the benchmark is much smaller than the theoretical ~450 MHz gap
- FFXIV is one of the most V-Cache-sensitive games — the cache advantage should easily compensate for any clock difference (7800X3D beats non-X3D chips by ~60% in crowded areas)

**Conclusion:** Clock speed does not explain the regression. Something else is degrading performance.

## FFXIV Dawntrail Benchmark — Technical Architecture

### Process Model

The benchmark uses a two-process parent-child architecture:

| Process | Role | Rendering | CPU impact |
|---------|------|-----------|------------|
| `ffxiv-dawntrail-bench.exe` | WPF/.NET launcher/configurator | None (2D UI only) | Negligible |
| `game/ffxiv_dx11.exe` | Actual benchmark engine (same as retail FFXIV client) | All 3D rendering | All game load |
| `GraphAdapterDesc.exe` | GPU enumeration utility (runs briefly at splash) | None | None |

**Lifecycle:**
1. `ffxiv-dawntrail-bench.exe` presents settings UI (resolution, graphics preset, character)
2. Writes config to `ffxivbenchmarklauncher.ini`
3. Builds command line with ~40+ `SYS.SettingName=Value` arguments
4. Spawns `game/ffxiv_dx11.exe` as child process
5. `ffxiv_dx11.exe` runs continuously through 5 scenes (~6 minutes), no restarts between scenes
6. On completion, writes score + average FPS to INI file and exits
7. Launcher reads results and displays them

**Key:** Only `ffxiv_dx11.exe` matters for CCD optimization. The launcher is irrelevant to performance. The game process runs as a single continuous execution — no mid-benchmark restarts.

### CPU Characteristics

- **One dominant main thread** handles game logic, world state, entity management — frequently at 95-100% utilization on one core
- **Helper threads** handle rendering submission, distributed across other cores at 20-40% utilization
- Scales noticeably up to **~6 cores**, diminishing returns beyond that
- DX11 multithreaded rendering (MTR) for draw call submission, but main game thread remains bottleneck

### Cache Sensitivity

FFXIV is **extremely cache-sensitive** — one of the strongest V-Cache beneficiaries:

| Comparison | FFXIV improvement |
|------------|-------------------|
| 7800X3D (96MB V-Cache) vs non-X3D | ~60% FPS in crowded areas |
| 9800X3D vs 7800X3D | ~16% (Zen 5 IPC + cache) |
| 9800X3D vs 5800X3D | ~33% |
| 9950X3D vs 9950X (non-3D) | ~15% at 1080p Maximum |

The massive L3 cache keeps entity/world-state data accessible, drastically reducing cache misses in player-dense scenarios.

### Benchmark vs Real Gameplay

The benchmark runs **pre-scripted scenes** with controlled entity counts. Real FFXIV gameplay in crowded cities (Limsa Lominsa, hunts, FATEs with hundreds of players) creates far more cache pressure. The benchmark is likely **less cache-sensitive** than real gameplay, though still significantly V-Cache-friendly based on hardware review data.

**GPU vs CPU balance varies by scene:**
- Battle scenes with spell effects → more GPU-bound
- Crowded areas with many characters → heavily CPU-bound (main thread)
- High resolutions (1440p, 4K) → shifts toward GPU-bound
- Low resolutions (720p, 1080p) → CPU-bound in most scenarios

### X3D CCD Scheduling (Dual-CCD Parts)

On 7950X3D / 7900X3D (only CCD0 has V-Cache):
- If Windows scheduler places game threads on CCD1 (no V-Cache), performance degrades ~10% with worse frametimes
- The 7950X3D can perform **worse** than a 7800X3D if scheduling goes wrong
- Windows Game Bar, AMD chipset drivers, and BIOS CPPC settings include game-aware logic to direct workloads to V-Cache CCD
- Testing shows virtually all games perform best on V-Cache CCD: ~19% average improvement vs Frequency CCD, ~18% vs unrestricted

On 9950X3D / 9900X3D (both CCDs have V-Cache): CCD scheduling is less critical since both CCDs have 3D V-Cache.

## Root Cause: Background Migration

### The Common Factor

Both optimize strategies that scored 31k shared one thing: `MigrateBackground()` — which hard-pinned **every** non-game, non-protected process (~166 processes) to the Frequency CCD via `SetProcessAffinityMask`. Monitor mode (34k) performed no migration at all.

### Problematic Migrated Processes

The following processes were migrated but should never have been touched:

**AMD CCD Scheduling Infrastructure:**
- `amd3dvcacheSvc.exe` — AMD V-Cache driver service (manages CCD scheduling preferences)
- `amd3dvcacheUser.exe` — AMD V-Cache user-mode component

**Windows Game Scheduling Infrastructure:**
- `GameBarPresenceWriter.exe` — Xbox Game Bar game-aware scheduler
- `GameBar.exe` — Xbox Game Bar
- `GameBarFTServer.exe` — Game Bar full-trust server
- `XboxGameBarWidgets.exe` — Game Bar widgets
- `gamingservices.exe` / `gamingservicesnet.exe` — Windows gaming services

**GPU Driver Services:**
- `NVDisplay.Container.exe` — NVIDIA display driver service
- `atiesrxx.exe` / `atieclxx.exe` — AMD display driver services

**Other System Infrastructure:**
- `explorer.exe` — Windows shell
- ~150 other system services, OEM processes, utilities

### Why Migration Hurts

1. **Disrupts CCD scheduling infrastructure:** AMD's own `amd3dvcacheSvc` and Windows Game Bar are the mechanisms that intelligently place game threads on V-Cache CCD. Forcibly moving them to the Frequency CCD may break their ability to influence scheduling.

2. **Cross-CCD latency for driver services:** GPU driver processes (`NVDisplay.Container.exe`) that communicate with the game's rendering thread now incur cross-CCD data transfer latency when the game is on CCD0 and the driver service is pinned to CCD1.

3. **OS scheduler interference:** The Windows scheduler is sophisticated and considers NUMA topology, cache locality, and power states when placing threads. Hard-pinning 166 processes overrides all of this optimization, likely making scheduling decisions worse, not better.

4. **The scheduler was already doing the right thing:** In monitor mode with `PREFER_FREQ` default, the game scored 34k. The OS + AMD driver were already placing threads effectively without intervention.

## Code Changes (Session 62)

### What Was Changed

All changes in `src/X3DCcdInspector/Core/AffinityManager.cs`:

**1. DriverPreference separated from background migration**

DriverPreference now only sets `PREFER_CACHE` in the AMD registry — no `SetProcessAffinityMask` calls, no `MigrateBackground()`, no re-migration timer. This makes the two strategies genuinely different:

| Aspect | AffinityPinning | DriverPreference (new) |
|--------|----------------|----------------------|
| Game | Hard-pin to V-Cache via affinity mask | AMD driver `PREFER_CACHE` only |
| Background | Hard-pin ALL to Frequency via affinity | No changes whatsoever |
| Re-migration timer | 5s for new processes | None |
| Restore on exit | Restore all saved affinity masks | Restore AMD registry to `PREFER_FREQ` |

**2. Game-name skip added to migration methods**

New `IsCurrentGame(string processName)` helper. Added as skip condition in both `MigrateBackground()` and `MigrateNewProcesses()`. Prevents the game from being migrated if it restarts with a new PID (both methods previously only checked PID, not process name).

### What Was NOT Changed

AffinityPinning still migrates all ~166 processes. This needs further rework (see next steps below).

## Pending Validation

**Critical test needed:** Run the FFXIV Dawntrail benchmark with the new DriverPreference code (driver-only, no migration).

- If score matches baseline (~34k) → confirms migration is the sole culprit
- If score still regresses → the AMD `PREFER_CACHE` registry change itself may be problematic
- Either way, AffinityPinning needs rework since it still migrates everything

## Resolution (Sessions 65-69)

This research directly led to the project pivot from "X3D CCD Optimizer" to "X3D CCD Inspector." The key conclusion — that background migration hurts rather than helps — informed the complete removal of migration logic and the redesign as a visibility and control tool.

**What was implemented:**
- **Option 3 was adopted:** Game-only pinning, zero background migration. Available as an explicit opt-in fallback when AMD's driver is not loaded (Phase 5).
- **Option 4 was adopted:** Per-game CCD preference using AMD's own per-app profile registry interface. The tool writes to `\Preferences\App\{GameName}` — no affinity masks, works with the scheduler (Phase 4).
- **Option 2 was adopted as a safety net:** Protected process list hardcoded (AMD driver services, Game Bar, GPU drivers, explorer, critical system processes). Enforced before any affinity operation.
- **Migration was completely removed.** `MigrateBackground()`, `MigrateNewProcesses()`, re-migration timer — all gone.
- **Monitor/Optimize modes were removed.** The tool is always-on visibility with explicit user-initiated control.

**Options not implemented:**
- Option 1 (selective migration) was superseded by the complete removal of migration.
- Option 5 (hybrid) was superseded by the per-app profile approach.
- Option 6 (PMC/MPKI) remains a future research direction — see [PMC Research](PMC_RESEARCH.md).

## Original Proposed Next Steps (Archived)

### Option 1: Selective migration only (originally recommended)
Change AffinityPinning to only migrate user-configured `backgroundApps` list (Steam, Discord, OBS, etc.), not every process on the system. The `backgroundApps` config field already exists — currently everything gets migrated regardless, with `backgroundApps` only controlling the log label ("rule" vs "auto").

### Option 2: Expand protected process list
Add AMD driver services, Game Bar processes, GPU driver services to the hardcoded protected set. This is a safety net but doesn't address the fundamental "migrate everything" problem.

### Option 3: Pin game only, no background migration
Simplest approach — AffinityPinning only pins the game to V-Cache CCD. No background migration at all. Let the OS scheduler handle everything else. The `backgroundApps` list becomes a separate opt-in feature for users who explicitly want to pin specific apps.

### Option 4: Per-game CCD preference
Add a `PreferredCcd` field to `GameProfile` model (values: `vcache` / `frequency` / `auto`). Some games may perform better on the Frequency CCD. Currently `GameProfile` only stores `Strategy` (affinityPinning / driverPreference / global).

### Option 5: Hybrid approach
Combine options 1 + 2 + 4: selective migration of configured apps only, protect scheduling infrastructure, allow per-game CCD preference. This preserves the tool's full value proposition while not fighting the OS scheduler.

### Option 6: Hardware-driven auto-classification (PMC/MPKI learning system)

Use AMD's built-in hardware performance monitoring counters (PMCs) to measure L3 cache misses per thousand instructions (MPKI) at runtime. The hardware directly tells you whether a game is cache-hungry (high MPKI → V-Cache CCD) or frequency-hungry (low MPKI → Frequency CCD). No guessing, no manual benchmarking.

**Architecture — learning over extended sessions:**

1. **Game detected** → check LiteDB for stored MPKI classification
2. **Known game (has stored data):** Apply stored CCD preference immediately — zero delay, zero measurement overhead
3. **Unknown game (first launch):** Start PMC sampling in the background while using a sensible default (e.g., V-Cache). After 90-120 seconds, compute MPKI, classify, store in LiteDB. Optionally re-pin if the result says Frequency CCD is better.
4. **Periodic re-validation:** Games get patched, engines change. Re-measure every N launches or after a game update to keep classifications fresh. Track measurement count, variance, and staleness as a confidence score.

**Implementation layers, in order of effort:**

| Layer | Effort | Value | Description |
|-------|--------|-------|-------------|
| Seed database | Easy | Immediate | Pre-populate classifications for top 100-200 games from published Chips and Cheese data and hardware reviewer measurements. Most users never need local learning. |
| PMC learning | Medium | Core feature | ETW kernel trace sessions with AMD-specific PMC events for L3 cache misses. App already uses ETW (TraceEvent 3.1.30) and runs as admin. Measure unknown games locally, store results. |
| A/B frametime validation | Medium | Ground truth | Instead of (or alongside) interpreting MPKI through a threshold, directly measure what matters: run 2 minutes on V-Cache CCD, 2 minutes on Frequency CCD, compare average frametimes via ETW DXGI/D3D present events. Accounts for ALL factors — cache, clocks, memory bandwidth, cross-CCD latency — not just one proxy metric. |
| Confidence/staleness tracking | Easy | Robustness | Track measurement count, MPKI variance, last-measured date. High confidence = many consistent measurements. Low confidence = single measurement 6 months ago before a major game patch → triggers re-measurement. |
| Community telemetry (opt-in) | Harder | Scale | Anonymous submissions: game exe hash + MPKI + CCD winner. After a few hundred users, the database covers thousands of games. Same opt-in pattern already used for artwork downloads. Needs a backend and privacy considerations. |

**Feasibility assessment:**

- **ETW PMC path is the most realistic.** Windows supports hardware PMC sampling through ETW kernel sessions. No custom kernel driver needed. The app already has admin rights and the TraceEvent dependency.
- **Constraints:** Only one `NT Kernel Logger` session at a time (conflicts with PresentMon, Intel GPA, some AV). AMD PMC event codes differ by Zen generation (Zen 4 vs Zen 5) — need microarchitecture detection. Per-process correlation requires matching PMC samples to game PID via context switch events.
- **Direct `RDPMC` from userspace is blocked** on Windows (ring 0 only). A custom driver (like AMD uProf's `AMDPowerProfiler.sys`) would work but is too high a barrier for end users.

**The seed database alone solves the immediate problem.** PMC learning handles the long tail. A/B frametime validation catches edge cases where MPKI alone gets the classification wrong. Community telemetry is the endgame that makes the tool self-improving at scale.

## Standardized Benchmark Methodology

Session 62 results (~34k baseline) were not recorded with settings, making later runs incomparable. All future benchmark runs MUST record the full test conditions below.

### Required Test Settings (FFXIV Dawntrail Benchmark)

| Setting | Value | Rationale |
|---------|-------|-----------|
| **Benchmark Version** | Record exact version (e.g., Ver. 1.1) | Score scaling may change between versions |
| **Resolution** | 1920x1080 | CPU-bound territory — isolates scheduling effects from GPU bottleneck |
| **Screen Mode** | Fullscreen (NOT Windowed or Borderless) | Windowed/Borderless adds DWM composition overhead, pollutes CPU measurements |
| **Graphics Upscaling** | Off (native) | Upscalers (FSR/DLSS) add variable GPU/CPU overhead depending on implementation |
| **Graphics Preset** | Maximum | Consistent load profile across runs |
| **DirectX** | 11 | FFXIV's primary renderer; DX11 MTR is the CPU-intensive path |
| **Dynamic Resolution** | Disabled | Prevents variable internal resolution during the run |

### Required System State

| Condition | Requirement | Rationale |
|-----------|-------------|-----------|
| **Background apps** | Close all non-essential apps (browsers, Discord, OBS, etc.) | Reduces CPU noise and scheduling interference |
| **X3D CCD Inspector** | Not running during baseline measurement | The app's own polling (ETW, PDH, Process.GetProcesses) must not affect results |
| **BIOS settings** | Record: CPPC, CPPC Preferred Cores, CPPC Dynamic Preferred Cores | These determine the entire scheduling stack behavior |
| **AMD driver state** | Record: DefaultType value (PREFER_FREQ=0 / PREFER_CACHE=1) | The variable under test |
| **Game Bar** | Note whether running or not | Triggers the PREFER_FREQ → PREFER_CACHE chain |
| **Thermal state** | Allow 5-minute idle cool-down between runs | Prevents thermal throttling from carrying over between runs |
| **Power plan** | Record Windows power plan (Balanced / High Performance) | Affects core parking and boost behavior |

### Recording Template

```
Date:           YYYY-MM-DD HH:MM
Benchmark:      FFXIV Dawntrail Ver. X.X
Resolution:     1920x1080
Screen Mode:    Fullscreen
Upscaler:       None
Preset:         Maximum
DirectX:        11

BIOS CPPC:              Enabled/Disabled
BIOS Preferred Cores:   Enabled/Disabled
BIOS Dynamic Preferred: Auto/Driver/Cache/Frequency
AMD Driver State:       PREFER_FREQ / PREFER_CACHE
Game Bar:               Running / Not Running
Inspector Running:      Yes / No
Power Plan:             Balanced / High Performance
Background Apps:        [list any running]

Score:          XXXXX
Avg FPS:        XXX.X
Min FPS:        XX
```

### Session 73 Benchmark Attempts (2026-04-09)

These runs are NOT comparable to the session 62 baseline due to different settings:

| Run | Score | Resolution | Mode | Upscaler | Notes |
|-----|-------|-----------|------|----------|-------|
| Session 62 baseline | ~34,000 | Unknown | Unknown | Unknown | No intervention, PREFER_FREQ. Settings not recorded. |
| Session 62 AffinityPinning | ~31,000 | Unknown | Unknown | Unknown | ~166 processes migrated |
| Session 73 run 1 | 22,505 | 1080p | Windowed | FSR | Device Manager + Inspector open |
| Session 73 run 2 | 16,645 | 3440x1440 | Borderless | DLSS | GPU-bound at ultrawide, useless for CPU scheduling comparison |
| Session 73 run 3 | 31,036 | 1080p | **Fullscreen** | FSR | Inspector closed, min FPS 8 (hitch), avg 213.8 FPS |
| Session 73 run 4 | 30,783 | 1080p | **Fullscreen** | DLSS | Inspector closed, Firefox open, min FPS 106 (clean), avg 212.9 FPS |

### Analysis

Runs 3 and 4 converge at ~31k regardless of upscaler (FSR vs DLSS), confirming the upscaler is not a factor. Min FPS 106 on run 4 confirms a clean execution with no hitches.

**~31k is the confirmed current baseline** for this system at 1080p Fullscreen Maximum DX11 with PREFER_FREQ, no Inspector running. The session 62 ~34k baseline cannot be reproduced — original test settings were not recorded. The delta may be attributable to system-level changes since 2026-03-30 (Windows updates, GPU drivers, BIOS/AGESA, thermal conditions) rather than any application-level regression.

**Conclusion:** The migration regression (34k → 31k) cannot be independently validated because the 34k baseline is no longer reproducible under any tested conditions. The bulk migration code has been fully removed (Phases 2 + 5), and ~31k represents the current system performance ceiling for this benchmark configuration.

---

## References

- [doitsujin/ffxiv-benchmark-launcher](https://github.com/doitsujin/ffxiv-benchmark-launcher) — Reverse-engineered benchmark launcher (Python reimplementation)
- [NotNite/benchtweaks](https://github.com/NotNite/benchtweaks) — FFXIV benchmark DLL injection mod (confirms `ffxiv_dx11.exe` is the rendering engine)
- [GamersNexus: AMD Ryzen 9 9950X3D Review](https://gamersnexus.net/cpus/amd-ryzen-9-9950x3d-cpu-review-benchmarks-vs-9800x3d-285k-9950x-more)
- [GamersNexus: AMD Ryzen 7 9800X3D Review](https://gamersnexus.net/cpus/rip-intel-amd-ryzen-7-9800x3d-cpu-review-benchmarks-vs-7800x3d-285k-14900k-more)
- [rkblog.dev: FFXIV Dawntrail Benchmark CPU/GPU Comparison](https://rkblog.dev/posts/ffxiv/dawntrail-benchmark-comparison/)
- [rkblog.dev: FFXIV CPU Performance Scaling](https://rkblog.dev/posts/ffxiv/final-fantasy-xiv-benchmarks-cpu-performance-scaling/)
- [cocafe/vcache-tray](https://github.com/cocafe/vcache-tray) — AMD amd3dvcache driver registry interface discovery
