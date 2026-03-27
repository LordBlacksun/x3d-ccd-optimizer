# Changelog

All notable changes to X3D Dual CCD Optimizer are documented here.

## [1.0.0] — 2026-03-27

### Added
- **Four-tier processor support** — DualCcdX3D (full optimization), SingleCcdX3D (V-Cache monitoring), SingleCcdStandard (standard monitoring), DualCcdStandard (affinity pinning). Tier auto-detected from L3 cache topology.
- **AMD V-Cache driver preference strategy** — alternative to affinity pinning, uses `amd3dvcache` registry interface. Lighter-touch, works with AMD's own scheduler logic. Credit: cocafe/vcache-tray for discovering the registry interface.
- **Grouped process router** — processes grouped by CCD assignment with game badges, sorted game-first, empty-state placeholder.
- **Settings window** — 5-tab modal (General, Games, Detection, Overlay, Advanced) with live-apply. Strategy and mode selectors gated by processor tier.
- **Dirty shutdown recovery** — writes recovery.json while affinities are engaged, restores on next launch. Strategy-aware (affinity pinning restores process masks, driver preference restores registry default).
- **Start with Windows** — registry HKCU Run key with `--minimized` flag.
- **Config validation** — all numeric values clamped to sane ranges on load.
- **Single-instance enforcement** — named mutex prevents conflicting instances.
- **Admin elevation** — manifest requires administrator for process affinity and HKLM registry access.
- **Non-AMD CPU detection** — warns Intel/other users with Continue/Exit dialog instead of silently running in reduced mode.
- **GPU heuristic feedback** — activity log shows "[AUTO] BELOW THRESHOLD" when a foreground app uses GPU but below detection threshold, so users know why it wasn't detected.
- **Power plan warning** — warns in status bar when Driver Preference is active on a non-Balanced power plan.
- **Tooltips on all settings** — plain English explanations on every interactive control.
- **Keyboard accessibility** — AccessKeys on all Settings tabs and buttons (Alt+letter shortcuts).
- **Screen reader support** — AutomationProperties.Name on all interactive UI elements across dashboard, settings, and overlay.
- **First-run onboarding** — status bar shows explanatory message on first launch.
- **Strategy restart note** — settings UI clarifies that strategy changes take effect on next launch.
- **Reduced idle GPU overhead** — GPU WMI queries skip every other poll cycle when no game is detected.

### Security
- Two comprehensive security audits (17 + 30 findings), all actionable items fixed.
- Atomic file writes (write-to-temp-then-rename) for config.json and recovery.json.
- Protected process filter in crash recovery — system processes cannot be modified.
- Thread safety: lock-protected GameDetector.CurrentGame, Lazy<bool> VCacheDriverManager, volatile ProcessWatcher._disposed.
- WMI query timeouts (10s) prevent startup hangs.
- Registry value validation on amd3dvcache reads/writes.
- Core index bounds checking in CCD override (0-63).
- IsSingleCcd guard on SwitchToOptimize/SwitchToMonitor prevents zero-mask affinity on single-CCD systems.
- Debug logging in all catch blocks for diagnostic visibility.
- Shared protected process list (single source for AffinityManager and RecoveryManager).
- Event subscriptions unwired on shutdown to prevent callbacks into disposed objects.

### Fixed
- WMI fallback core mask calculation for single-CCD processors (was hardcoded /2).
- Non-X3D single-CCD processors no longer mislabeled as "V-Cache CCD".
- Error message for non-AMD CPUs is now user-friendly (no raw exception text).
- Version numbers unified across all source files and documentation.

## [0.2.0] — 2026-03-27

### Added
- WPF dashboard with Monitor/Optimize mode toggle, dark theme, per-core heatmaps.
- Compact always-on-top overlay with OLED burn-in protection (auto-hide, pixel shift).
- GPU heuristic auto-detection with 65-game known database and debounce.
- System tray with color-coded icons and context menu.
- Three-tier game detection: manual list, known database, GPU heuristic.

### Fixed
- CACHE_RELATIONSHIP struct padding (18-byte Reserved field).
- Full code audit: 12 issues fixed (config safety, thread-safe disposal, handle leaks, multi-monitor positions).

## [0.1.0] — 2026-03-25

### Added
- Core engine: CCD topology detection via P/Invoke with WMI fallback.
- Per-core performance monitoring via PDH counters.
- Process affinity management with protected process list.
- JSON configuration with manual game list.
- Console output for topology info and affinity events.
