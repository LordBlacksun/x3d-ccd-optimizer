# Changelog

All notable changes to X3D CCD Inspector are documented here.

## [Unreleased]

### Fixed
- Excluded processes were not blocking game detection via manual rules or library scan — only the GPU heuristic path checked the exclusion list. Wallpaper Engine and similar apps in the exclusion list were still detected as games.

### Added
- **Process Exclusions tab** — third dashboard tab showing running user-level processes with click-to-toggle exclusion. Filters system services, auto-refreshes every 5 seconds, persists immediately to config.
- Runtime exclusion add/remove on `GameDetector` — no restart needed when toggling exclusions.

### Changed
- CCD heatmap panels now use fluid layout — scales with window size instead of fixed 560px width.
- Xbox Game Bar status shows "Standby" instead of "Not Running" when idle.
- GameMode status shows "Standby" instead of "Inactive" when no game is detected.

### Documentation
- Added standardized benchmark methodology to BENCHMARK_RESEARCH.md with required settings, system state checklist, and recording template.

## [2.0.0-beta] — 2026-03-30

### Changed — Project Transition: Optimizer → Inspector

The project has been fundamentally restructured from an optimizer to a visibility and control tool. This was driven by benchmark research showing that background process migration caused ~9% performance regression by disrupting AMD's scheduling infrastructure (see [Benchmark Research](docs/BENCHMARK_RESEARCH.md)).

**Identity:**
- Renamed from "X3D CCD Optimizer" to "X3D CCD Inspector"
- All namespaces, assemblies, and references updated
- New focus: visibility over intervention -- show what's happening, let the user decide

**Removed:**
- Background process migration (`MigrateBackground`, `MigrateNewProcesses`) -- the core finding that started this transition
- Monitor/Optimize dual-mode toggle
- Process Rules tab
- `OptimizeStrategy` and `OperationMode` enums
- Re-migration timer
- All affinity mask setting for background processes
- Old optimizer-framing UI elements and documentation

**Added:**
- Real-time system status dashboard (AMD driver service, driver state, Game Bar, GameMode) with color-coded indicators
- Active game panel showing detection method, CCD distribution, thread counts, and driver action
- SystemStateMonitor -- central polling service (7s state + 1.5s foreground detection)
- Per-game CCD preference via AMD's per-app profile registry (`\Preferences\App\{GameName}`)
- Game Library CCD preference dropdown (Auto / V-Cache / Frequency)
- Startup sync between LiteDB preferences and AMD registry state
- Affinity pinning fallback -- game-only, explicit opt-in, only when driver unavailable
- Protected process list enforced on all affinity operations
- Overlay game-only visibility (hides when game not in foreground)
- Overlay "(pinned)" suffix for affinity fallback
- CCD Map tab (renamed from Process Router)
- 66 new tests (CCD preference, affinity pin, system state logic)

**Reworked:**
- AffinityManager -- gutted from 817 lines to game tracking + game-only pinning
- Dashboard -- 5-row layout with system status, active game, CCD heatmaps, tabs, activity log
- Overlay -- game name + CCD on line 1, driver state on line 2
- Activity log -- new event types: DRIVER STATE, GAME BAR, CCD, CCD PREF, AFFINITY PIN
- RecoveryManager -- simplified to one-shot driver preference restoration

## [1.0.0] — 2026-03-27

### Added
- Four-tier processor support (DualCcdX3D, SingleCcdX3D, SingleCcdStandard, DualCcdStandard)
- AMD V-Cache driver preference strategy via `amd3dvcache` registry interface (credit: cocafe/vcache-tray)
- WPF dashboard with dark theme, per-core heatmaps, grouped process router
- Settings window (5-tab modal with live-apply)
- Dirty shutdown recovery
- Start with Windows (registry HKCU Run key)
- Single-instance enforcement
- Admin elevation with UAC prompt
- Compact gaming overlay with OLED burn-in protection
- GPU heuristic auto-detection with debounce
- System tray with color-coded icons
- Three-tier game detection: manual list, library scan (Steam/Epic/GOG), GPU heuristic
- Game Library tab with source badges and opt-in box art
- About dialog with AI disclosure

### Security
- Two comprehensive security audits (17 + 30 findings), all actionable items fixed
- Atomic file writes for config and recovery files
- Protected process list enforcement
- Thread safety improvements throughout

## [0.2.0] — 2026-03-27

### Added
- WPF dashboard with Monitor/Optimize toggle and dark theme
- Compact always-on-top overlay with OLED burn-in protection
- GPU heuristic auto-detection
- System tray with context menu

### Fixed
- CACHE_RELATIONSHIP struct padding (18-byte Reserved field)

## [0.1.0] — 2026-03-25

### Added
- Core engine: CCD topology detection via P/Invoke with WMI fallback
- Per-core performance monitoring via PDH counters
- Process affinity management with protected process list
- JSON configuration with manual game list
- Console output for topology info and affinity events
