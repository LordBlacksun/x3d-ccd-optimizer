![X3D CCD Optimizer](logo-512.png)

# X3D Dual CCD Optimizer

A lightweight, open-source Windows tool for AMD Ryzen processors. Real-time CCD dashboard, automatic game detection, intelligent process routing, and a compact gaming overlay — with four-tier processor support from single-CCD standard Ryzen to dual-CCD X3D.

## Why This Tool Exists

AMD's dual-CCD X3D processors (7950X3D, 7900X3D, 9950X3D, 9900X3D) rely on a complex chain of software to steer games onto the V-Cache CCD:

1. **AMD Chipset Drivers** — PPM Provisioning File Driver + 3D V-Cache Performance Optimizer service
2. **Xbox Game Bar** — KGL (Known Game List) for game detection
3. **Windows Balanced power plan** — required for core parking
4. **BIOS CPPC Dynamic Preferred Cores** — must be set correctly (Auto or Driver)
5. **AMD Application Compatibility Database** — Zen 5 X3D only, chipset v7.01.08.129+

When this chain works, games land on the V-Cache CCD automatically. When any link breaks — wrong power plan, outdated Game Bar, CPPC misconfigured, game not in KGL — cores don't park correctly and games run on the wrong CCD with measurable FPS loss.

Community troubleshooting has produced contradictory advice: some say CPPC should be Auto, others say Driver. Some say Game Bar is required, others disable it entirely and use Process Lasso. Results vary by motherboard vendor, BIOS version, chipset driver version, and Windows build. The chipset driver v7.02.13.148 (launched with the 9950X3D) updated the V-Cache driver from v1.0.7 to v1.0.9 and added the Application Compatibility Database, but multiple 7950X3D users reported it broke their previously working core parking.

**This tool cuts through the confusion** by offering two optimization strategies that don't depend on getting every link in AMD's chain correct.

## What It Does

- **Two operating modes** — Monitor mode (default) observes without touching anything. Optimize mode actively steers games to the best CCD. Switch with a single click.
- **Two optimization strategies** — Affinity Pinning (direct process affinity control) or Driver Preference (AMD's own registry interface). Choose in settings.
- **Four-tier processor support** — Dual-CCD X3D (full optimization), single-CCD X3D (monitoring), dual-CCD standard Ryzen (affinity pinning), single-CCD standard (monitoring).
- **Real-time CCD dashboard** — per-core load heatmap, frequency display, grouped process-to-CCD routing table with game badges, and a live activity log.
- **Automatic game detection** — three-tier: manual game list, 65-game known database, GPU usage heuristic with debounce.
- **Compact gaming overlay** — always-on-top mini display for single-monitor setups, with OLED burn-in protection (auto-hide, pixel shift). Toggle with Ctrl+Shift+O.
- **System tray** — color-coded icon (blue=Monitor, purple=Optimize idle, green=Optimize active), right-click menu with mode toggle, overlay, and settings.
- **Settings window** — 5-tab dialog (General, Games, Detection, Overlay, Advanced) with tooltips on every control, live-apply, start-with-Windows support.
- **Dirty shutdown recovery** — if the app crashes while affinities are modified, the next launch automatically restores everything.
- **Migrates background processes** — Discord, browsers, Spotify move to the frequency CCD (Affinity Pinning strategy only).
- **Restores everything on game exit** — all affinities return to defaults, driver preferences reset.

## Optimization Strategies

### Affinity Pinning (Default)

Directly sets process affinity masks to pin games to the V-Cache CCD and migrate background processes to the frequency CCD.

| | |
|---|---|
| **How it works** | Calls `SetProcessAffinityMask` on the game process and all non-protected background processes |
| **Pros** | Works regardless of CPPC setting, Game Bar status, chipset driver version, or power plan. No BIOS changes needed. No Xbox Game Bar dependency. Immediate effect. Works on any dual-CCD Ryzen, not just X3D. |
| **Cons** | Overrides the OS scheduler entirely for affected processes. Background migration touches every non-protected process. |
| **Requirements** | None beyond the app itself. |

### Driver Preference

Writes to AMD's `amd3dvcache` registry preference to tell the V-Cache driver to prefer the cache CCD at the scheduler level.

| | |
|---|---|
| **How it works** | Sets `HKLM\SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences\DefaultType` to `1` (PREFER_CACHE) |
| **Pros** | Works with AMD's own driver logic, not against it. Lighter touch — no per-process affinity manipulation. No background process migration needed. |
| **Cons** | Requires AMD chipset drivers with the `amd3dvcache` service installed. Registry change may not take immediate effect (can take minutes without service restart). Only available on X3D processors. |
| **Requirements** | AMD chipset drivers installed (Device Manager should show "AMD 3D V-Cache Performance Optimizer" under System Devices). AMD 3D V-Cache Performance Optimizer Service running. BIOS CPPC Dynamic Preferred Cores set to Driver (AMD CBS > SMU Common Options). AMD officially recommends Auto, but community testing across both Zen 4 and Zen 5 X3D consistently shows Driver is more reliable for this strategy. |

## System Requirements

- **OS:** Windows 10 1903+ or Windows 11
- **CPU:** AMD Ryzen processor (see supported processors below)
- **Elevation:** Runs as Administrator (required for process affinity and HKLM registry access)
- **Runtime:** .NET 8 Runtime (included in self-contained build, or install separately for framework-dependent build)

## Supported Processors

| Tier | Processors | Features |
|------|-----------|----------|
| Dual-CCD X3D | 7950X3D, 7900X3D, 9950X3D, 9900X3D | Full: Affinity Pinning, Driver Preference, CCD heatmaps, background migration, overlay |
| Single-CCD X3D | 7800X3D, 9800X3D | Monitoring only: per-core heatmap, GPU detection, overlay. No CCD steering needed. |
| Dual-CCD Standard | 7950X, 9950X, etc. | Affinity Pinning to either CCD, CCD heatmaps, monitoring. No Driver Preference. |
| Single-CCD Standard | 7700X, 5800X, etc. | Monitoring only: per-core heatmap, overlay. |

## Windows Settings Compatibility

| Setting | Affinity Pinning | Driver Preference |
|---------|-----------------|-------------------|
| Game Mode | No effect | No effect |
| Xbox Game Bar | Not needed | Not needed |
| Power Plan | Any | Balanced recommended |
| CPPC (BIOS) | Any | Set to Driver |
| HAGS | No effect | No effect |
| Core Isolation / VBS | No effect | No effect |

## Does This Conflict with AMD's Own Optimization?

- **Affinity Pinning:** Overrides AMD's scheduler preferences with direct affinity masks. If AMD's driver is also parking cores, both work — your game is pinned to V-Cache regardless. No conflict, just redundancy.
- **Driver Preference:** Writes to the same registry key AMD's driver reads. If Game Bar is also active, they agree on the preference. No conflict.

## Status

**v1.0.0** — Four-tier processor support, dual optimization strategies, settings window, dirty shutdown recovery, two security audits (all findings fixed), grouped process router, accessibility improvements.

See [CHANGELOG.md](CHANGELOG.md) for release history.

## Getting Started

### Build and Run

```bash
git clone https://github.com/LordBlacksun/x3d-ccd-optimizer.git
cd x3d-ccd-optimizer
dotnet build
dotnet run --project src/X3DCcdOptimizer
```

### Self-Contained Publish

```bash
dotnet publish src/X3DCcdOptimizer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

### Configuration

On first run, a config file is created at `%APPDATA%\X3DCCDOptimizer\config.json`. All settings are also accessible through the in-app Settings window.

**Add games to the manual list** (highest detection priority):
```json
{
  "manualGames": [
    "elitedangerous64.exe",
    "yourgame.exe"
  ]
}
```

**Configure auto-detection:**
```json
{
  "autoDetection": {
    "enabled": true,
    "gpuThresholdPercent": 50,
    "requireForeground": true,
    "detectionDelaySeconds": 5,
    "exitDelaySeconds": 10
  }
}
```

The app also ships with a 65-game known games database (`Data/known_games.json`) that is checked automatically.

## How It Works

1. On startup, queries your CPU's cache topology to identify CCDs and classify the processor tier
2. Monitors running processes against the manual list, known games database, and GPU usage heuristics
3. **In Monitor mode:** shows what it *would* do without touching anything — `[MONITOR] WOULD ENGAGE`
4. **In Optimize mode:** applies the selected strategy (Affinity Pinning or Driver Preference)
5. When the game exits, all process affinities are restored and driver preferences reset
6. Every action is logged with timestamps, detection source, and full transparency
7. If the app crashes mid-optimization, the next launch automatically recovers

## Known Limitations

- **Overlay requires borderless windowed or windowed mode.** Exclusive fullscreen games take over the display adapter — no standard Windows overlay can render on top. Performance impact of borderless windowed is negligible on Windows 11.
- **Self-contained exe is ~155MB.** Includes the full .NET 8 runtime + WPF + WinForms. Trimming planned for a future release.
- **GPU auto-detection requires Windows GPU performance counters.** If your GPU driver doesn't expose them, auto-detection is disabled and the tool falls back to manual list + known database. Games are still detected, just not via GPU usage.
- **Driver Preference latency.** Registry changes may not take immediate effect — the AMD driver polls the registry value. For best results, set your preferred strategy before launching your game.
- **64+ logical processors.** Performance monitoring hardcodes processor group 0. Current consumer X3D processors max out at 32 threads, so this doesn't affect the target audience. Forward-compatibility note for future high-core-count processors.
- **AMD Application Compatibility Database.** The thread-pool-reduction feature introduced in chipset v7.01.08.129+ is independent of this tool. Confirmed for 9950X3D; applicability to Zen 4 X3D is unclear from AMD documentation. Whitelisted titles: Deus Ex: Mankind Divided, Dying Light 2, Far Cry 6, Metro Exodus, Metro Exodus Enhanced, Total War: Three Kingdoms, Total War: Warhammer III, Wolfenstein.

## Why Not Just Use Process Lasso?

Process Lasso is a general-purpose process manager that can do affinity pinning, but:

- It has no awareness of X3D CCD topology or V-Cache identification
- You have to manually configure every game and every background app
- There's no automatic game detection (manual list, known DB, or GPU heuristic)
- There's no visual feedback of what each CCD is doing
- It can't use AMD's driver preference registry interface
- It's commercial software for a problem that needs a focused, lightweight tool

This tool does one thing and does it well.

## Roadmap

- [x] **Phase 1** — Core engine: topology detection, affinity management, per-core monitoring
- [x] **Phase 2** — WPF dashboard with Monitor/Optimize toggle, dark theme, per-core heatmap
- [x] **Phase 2.5** — OLED-safe overlay, code audit, GPU auto-detection with 65-game database
- [x] **Phase 3** — Settings window, start-with-Windows, AMD driver preference strategy, 4-tier processor support, grouped process router, security audits
- [ ] **Phase 4** — CI/CD, trimmed single-file build, installer, GitHub releases

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

The known games database (`src/X3DCcdOptimizer/Data/known_games.json`) is a great place to start — adding game executables helps everyone.

## License

GPL v2. See [LICENSE](LICENSE) for details.

## How This Was Built

This project is conceived, designed, and architecturally directed by LordBlacksun. The implementation is generated by [Claude Code](https://claude.ai/claude-code) (Anthropic's AI coding tool) under human supervision.

**What that means in practice:**

- LordBlacksun defines the architecture, makes all design decisions, and sets the project direction. The [blueprint](X3D_CCD_OPTIMIZER_BLUEPRINT.md) is the authoritative spec.
- Claude Code generates the implementation — code, documentation, tests — following the blueprint and coding conventions.
- LordBlacksun reviews, tests on real hardware, and approves every change before it's committed. Nothing is merged without human sign-off.
- The workflow is "build freely, approve before commit." The AI proposes, the human disposes.

**Why disclose this?**

Because honesty matters more than optics. AI-assisted development is a legitimate way to build software, and pretending otherwise would be worse than just saying it. The code works, the architecture is sound, and every line has been reviewed by a human. But AI-generated code can have blind spots — which is exactly why community code reviews and contributions are actively welcomed.

If you spot something that doesn't look right, open an issue or PR. That's how open source is supposed to work.

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to get involved, and [SECURITY.md](SECURITY.md) for the security posture.

## Acknowledgements

Inspired by the Linux community's [x3d-toggle](https://github.com/pyrotiger/x3d-toggle) tool and the AMD `amd_x3d_vcache` kernel driver. Built because Windows users deserve the same level of control.

- [vcache-tray](https://github.com/cocafe/vcache-tray) by cocafe — for discovering and documenting the AMD V-Cache driver registry interface. The Driver Preference optimization strategy builds on their work.
