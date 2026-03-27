# X3D Dual CCD Optimizer

A lightweight, open-source Windows tool for AMD dual-CCD Ryzen processors. Real-time CCD dashboard, automatic game detection, intelligent process routing, and a compact gaming overlay — all with Monitor/Optimize dual-mode for safety and control.

## The Problem

AMD Ryzen dual-CCD X3D processors (7950X3D, 7900X3D, 9950X3D, 9900X3D) have an asymmetric architecture — one CCD has 3D V-Cache (96MB L3, great for gaming), the other runs at higher clock speeds (better for general compute). For best gaming performance, your game needs to run on the V-Cache CCD while background tasks stay on the other.

AMD's built-in solution relies on Xbox Game Bar detecting your game and parking the non-V-Cache CCD. In practice:

- It fails silently when Game Bar doesn't recognize a game
- It breaks with chipset driver updates
- It parks the second CCD instead of using it for background work (wasted cores)
- There's zero feedback — you have no idea if it's working or not
- It hasn't been meaningfully improved since 2023

On Linux, AMD contributed a proper kernel-level scheduler. Windows users got a Game Bar hack and nothing else.

**This tool fixes that.**

## What It Does

- **Two operating modes** — Monitor mode (default) observes without touching anything. Optimize mode actively pins games to V-Cache. Switch between them with a single click.
- **Real-time CCD dashboard** — per-core load heatmap, frequency display, process-to-CCD routing table, and a live activity log
- **Automatic game detection** — three-tier: manual game list, 65-game known database, GPU usage heuristic with debounce
- **Compact gaming overlay** — always-on-top mini display for single-monitor setups, with OLED burn-in protection (auto-hide, pixel shift)
- **System tray** — color-coded icon (blue=Monitor, purple=Optimize idle, green=Optimize active), right-click menu with mode toggle
- **Detects CCD topology automatically** — identifies V-Cache vs frequency CCD by L3 cache size
- **Migrates background processes** — Discord, browsers, Spotify move to the frequency CCD
- **Restores everything on game exit** — all affinities return to default

## Supported Processors

| CPU | Cores | V-Cache CCD | Modes |
|-----|-------|-------------|-------|
| Ryzen 9 7950X3D | 16 cores / 32 threads | CCD0 (96MB L3) | Monitor + Optimize |
| Ryzen 9 7900X3D | 12 cores / 24 threads | CCD0 (96MB L3) | Monitor + Optimize |
| Ryzen 9 9950X3D | 16 cores / 32 threads | CCD0 (96MB L3) | Monitor + Optimize |
| Ryzen 9 9900X3D | 12 cores / 24 threads | CCD0 (96MB L3) | Monitor + Optimize |
| Other dual-CCD Ryzen | varies | N/A | Monitor only |

## Status

**Phase 2.5 complete** — WPF dashboard, Monitor/Optimize mode toggle, polished dark theme UI, compact OLED-safe overlay, GPU auto-detection with 65-game database, full code audit with 12 issues fixed.

Phase 3 (Settings UI + Start with Windows) and Phase 4 (CI/CD + Release) are next.

## Getting Started

### Requirements

- Windows 10 21H2+ or Windows 11
- An AMD dual-CCD Ryzen processor (X3D for Optimize mode, any dual-CCD for Monitor)
- .NET 8 SDK (for building from source)

### Build and Run

```bash
git clone https://github.com/LordBlacksun/x3d-ccd-optimizer.git
cd x3d-ccd-optimizer
dotnet build
dotnet run --project src/X3DCcdOptimizer
```

### Configuration

On first run, a config file is created at `%APPDATA%\X3DCCDOptimizer\config.json`.

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

**Configure the overlay:**
```json
{
  "overlay": {
    "enabled": false,
    "autoHideSeconds": 10,
    "pixelShiftMinutes": 3,
    "opacity": 0.8
  }
}
```

The app also ships with a 65-game known games database (`Data/known_games.json`) that is checked automatically.

## How It Works

1. On startup, the tool queries your CPU's cache topology to identify V-Cache and frequency CCDs
2. It monitors running processes against the manual list, known games database, and GPU usage heuristics
3. **In Monitor mode:** shows what it *would* do without touching anything — `[MONITOR] WOULD ENGAGE`
4. **In Optimize mode:** uses `SetProcessAffinityMask` to pin the game to V-Cache and migrate background processes
5. When the game exits, all process affinities are restored to defaults
6. Every action is logged with timestamps, detection source, and full transparency

This is **not** a kernel driver — it uses standard Windows APIs that don't require admin privileges.

## Roadmap

- [x] **Phase 1** — Core engine: topology detection, affinity management, console output
- [x] **Phase 2** — WPF dashboard with Monitor/Optimize toggle, dark theme, per-core heatmap
- [x] **Phase 2.5** — OLED-safe overlay, code audit, GPU auto-detection with 65-game database
- [ ] **Phase 3** — Settings window, start-with-Windows, per-game profiles
- [ ] **Phase 4** — CI/CD, trimmed single-file build, installer, release

## Known Limitations

- **The mini overlay requires borderless windowed or windowed mode.** Exclusive fullscreen games take over the display adapter and no standard Windows overlay can render on top. Switch your game to borderless windowed for overlay support — performance impact is negligible on Windows 11.
- **Self-contained exe is ~155MB.** Includes the full .NET 8 runtime + WPF + WinForms. Trimming is planned for Phase 4.
- **GPU auto-detection requires Windows GPU performance counters.** If your GPU driver doesn't expose them, auto-detection is disabled and the tool falls back to manual list + known database.

## Why Not Just Use Process Lasso?

Process Lasso is a general-purpose process manager that can do affinity pinning, but:

- It has no awareness of X3D CCD topology
- You have to manually configure every game and every background app
- There's no automatic game detection
- There's no visual feedback of what each CCD is doing
- It's bloated commercial software for a problem that needs a focused, lightweight tool

This tool does one thing and does it well.

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
