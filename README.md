![X3D CCD Optimizer](logo-512.png)

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white) ![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D6?logo=windows&logoColor=white) ![License](https://img.shields.io/badge/License-GPL%20v2-blue) ![Version](https://img.shields.io/badge/Version-1.0.0--beta-orange) ![AMD Ryzen](https://img.shields.io/badge/AMD-Ryzen%20X3D-ED1C24?logo=amd&logoColor=white) [![Build](https://github.com/LordBlacksun/X3D-CCD-Optimizer/actions/workflows/build.yml/badge.svg)](https://github.com/LordBlacksun/X3D-CCD-Optimizer/actions/workflows/build.yml)

# X3D CCD Optimizer

A free, open-source Windows tool that gives you visibility and control over CCD scheduling on AMD Ryzen dual-CCD processors. Real-time dashboard, automatic game detection with library scanning, background process management, and a compact gaming overlay.

## Why Does This Exist?

AMD and Microsoft have built scheduling improvements that work behind the scenes -- CPPC preferred cores, the 3D V-Cache driver, GameMode power profiles. These are good. But they give users no visibility into what's happening and no direct control when things don't work as expected.

This tool adds transparency and explicit management on top of AMD's existing system. It doesn't replace AMD's drivers -- it supplements them. You can see exactly which CCD your game is running on, which processes are where, and take direct action if needed.

For the full technical breakdown, see the Wiki: [AMD X3D Scheduling Explained](../../wiki/AMD-X3D-Scheduling-Explained).

## Features

- **Real-time CCD dashboard** -- per-core load heatmaps, frequency display, process router with game badges, activity log
- **Game Library tab** -- shows all scanned games with source badges (Steam, Epic, GOG) and opt-in box art
- **Automatic game detection** -- scans installed Steam, Epic, and GOG libraries. On a typical gaming PC, this covers hundreds of titles out of the box. Anything not recognized falls back to GPU heuristic detection
- **Three-tier detection pipeline** -- Manual rules, Library scan (Steam/Epic/GOG), GPU heuristic
- **Two optimization strategies** -- Driver Preference (AMD's V-Cache driver registry) or Affinity Pinning (direct CPU affinity masks). Driver Preference recommended for X3D processors
- **Process Rules** -- pin games to V-Cache CCD, pin background apps (Discord, browsers, OBS) to Frequency CCD
- **Compact gaming overlay** -- always-on-top CCD load display with OLED burn-in protection (pixel shift every 3 minutes). Toggle with Ctrl+Shift+O
- **Opt-in box art** -- artwork from Steam's public CDN, off by default. Zero network activity unless you enable it
- **About dialog** -- version, license, credits, AI disclosure
- **Safe by default** -- Monitor mode observes without changing anything. Optimize is opt-in
- **Dirty shutdown recovery** -- affinities restored automatically even after crashes
- **Adaptive polling** -- idle at 4s, active at 2s. Background re-migration every 5s during optimization
- **No network connections by default** -- open source, no telemetry. Optional artwork downloads connect to Steam's public CDN only

## Screenshots

<!-- TODO: Add screenshots -->
<!-- ![Dashboard](screenshots/dashboard.png) -->
<!-- ![Game Library](screenshots/game-library.png) -->
<!-- ![Process Rules](screenshots/process-rules.png) -->
<!-- ![Overlay](screenshots/overlay.png) -->

## Download & Install

### Option 1: Portable ZIP (recommended)
Download the `.zip` from the latest [Release](../../releases), extract anywhere, and run `X3DCcdOptimizer.exe`. No installation or runtime needed — everything is included. You'll need to right-click and "Run as administrator" or accept the UAC prompt.

### Option 2: Build from source
```bash
# Requires .NET 8 SDK
git clone https://github.com/LordBlacksun/x3d-ccd-optimizer.git
cd x3d-ccd-optimizer
dotnet build
dotnet run --project src/X3DCcdOptimizer
```

## Quick Start

1. Run the app -- accept the UAC prompt (admin rights required for CPU affinity)
2. On first launch, you'll be asked whether to scan your game libraries (Steam, Epic, GOG)
3. The app starts in **Monitor mode** -- it observes your CPU without changing anything
4. Open **Settings > Process Rules** to configure which games go to V-Cache and which background apps go to Frequency CCD
5. Toggle to **Optimize** when you're ready to actively manage CCD affinity

## Supported Processors

This tool is built for dual-CCD AMD Ryzen processors only. Single-CCD processors (9800X3D, 7800X3D, 5800X3D, 7600X, 9700X, etc.) don't have the CCD scheduling problem this tool solves -- the app detects them and exits with a friendly explanation.

| Tier | Examples | Features |
|------|----------|----------|
| Dual-CCD X3D | 7950X3D, 7900X3D, 9950X3D, 9900X3D | Full: Driver Preference + Affinity Pinning, CCD heatmaps, background migration, overlay |
| Dual-CCD Standard | 5950X, 5900X, 7950X, 7900X, 9950X, 9900X | Affinity Pinning, CCD heatmaps, background migration |

Non-AMD processors are also detected and shown a separate exit dialog.

## Requirements

- **OS:** Windows 10/11 64-bit
- **Runtime:** None — the release includes everything needed
- **CPU:** AMD Ryzen dual-CCD processor (7950X3D, 9950X3D, 5950X, etc.)
- **Admin:** Required for process affinity and driver registry access

## Documentation

Full documentation is available on the [Wiki](../../wiki):

- [AMD X3D Scheduling Explained](../../wiki/AMD-X3D-Scheduling-Explained) -- how CPPC, V-Cache driver, and core parking work together
- [How It Works](../../wiki/How-It-Works) -- architecture and detection pipeline
- [FAQ](../../wiki/FAQ) -- common questions and troubleshooting
- [Process Rules](../../wiki/Process-Rules) -- configuring game and background app rules

## How It Works

On startup, the app detects your CPU's cache topology via `GetLogicalProcessorInformationEx` to identify CCDs and V-Cache presence. It verifies the processor is dual-CCD -- single-CCD and non-AMD processors get a friendly exit dialog. It then monitors running processes using ETW kernel events (with polling fallback) against a three-tier detection pipeline:

1. **Manual rules** (user-configured) -- highest priority, always wins
2. **Library scan** (Steam, Epic, GOG) -- automatic scanning of installed game libraries
3. **GPU heuristic** (WMI per-process 3D utilization) -- debounce: 5s to detect, 10s to exit

In **Monitor mode**, everything is observe-only -- the dashboard shows what the app *would* do. In **Optimize mode**, the selected strategy takes effect: Driver Preference sets AMD's registry key to prefer the V-Cache CCD, while Affinity Pinning directly sets CPU affinity masks. Background processes are migrated to the Frequency CCD in both strategies. When the game exits, all changes are restored. Every action is logged with timestamps and detection source.

## FAQ

**Is it safe?**
Yes. Monitor mode changes nothing. Optimize mode uses standard Windows APIs (`SetProcessAffinityMask`) and AMD's own driver registry interface. All changes are reversed when the game exits. Dirty shutdown recovery handles crashes.

**Why does it need admin rights?**
Windows requires administrator privileges to set CPU affinity on other processes and to write to the AMD driver's HKLM registry key. The app is fully open source -- audit every line.

**Does it work with anti-cheat?**
Driver Preference (recommended) does not modify any game process -- it only sets a registry key that AMD's driver reads. Affinity Pinning modifies the game's CPU affinity mask, which is a standard Windows feature but based on community reports may interact with some aggressive anti-cheat systems. Use Driver Preference for competitive online games.

**Does it support single-CCD processors like the 9800X3D or 7800X3D?**
No -- the app detects single-CCD processors and shows a friendly message explaining they don't need this tool. On single-CCD X3D chips, all cores share the same V-Cache, so there is no CCD scheduling problem to solve.

**Does it connect to the internet?**
No network connections by default. Optional artwork downloads (off by default) connect to Steam's public CDN only. There is no telemetry, no update checks, and no analytics.

**How is this different from Process Lasso?**
Process Lasso is a general-purpose process manager with no awareness of X3D CCD topology, no automatic game detection, no V-Cache driver integration, and no visual CCD dashboard. This tool is purpose-built for the AMD Ryzen dual-CCD scheduling problem.

**Is this AI-generated?**
The architecture and design decisions are by [LordBlacksun](https://github.com/LordBlacksun). Implementation is generated by [Claude Code](https://claude.ai/claude-code) under human supervision. Every change is reviewed, tested on real hardware, and approved before commit. See [How This Was Built](#how-this-was-built) for details.

## Disclaimer

X3D CCD Optimizer modifies CPU affinity and/or AMD driver registry preferences to optimize CCD scheduling. While these are standard Windows and AMD features, the developer provides this software as-is with no warranty. The developer assumes no liability for any consequences including but not limited to: anti-cheat detection, game bans, system instability, or data loss. Use at your own risk. See [LICENSE](LICENSE) (GPL v2) for full terms.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

Bug reports, feature requests, and code contributions are all welcome.

## How This Was Built

This project is conceived, designed, and architecturally directed by LordBlacksun. Implementation is generated by [Claude Code](https://claude.ai/claude-code) (Anthropic's AI coding tool) under human supervision.

- LordBlacksun defines the architecture, makes all design decisions, and sets the project direction
- Claude Code generates the implementation following the [blueprint](X3D_CCD_OPTIMIZER_BLUEPRINT.md) and coding conventions
- Every change is reviewed, tested on real hardware, and approved before commit
- The workflow is "build freely, approve before commit" -- the AI proposes, the human disposes

AI-assisted development is a legitimate way to build software. The code works, the architecture is sound, and every line has been reviewed. But AI-generated code can have blind spots -- community code reviews and contributions are actively welcomed.

## Credits

- [cocafe/vcache-tray](https://github.com/cocafe/vcache-tray) -- for discovering and documenting the AMD V-Cache driver registry interface. The Driver Preference strategy builds on their work.
- AMD -- for CPPC, the 3D V-Cache driver, and the scheduling infrastructure this tool builds on.
- JayzTwoCents -- for the BIOS research on setting CPPC Preferred Cores to "Driver" for optimal V-Cache scheduling.
- Inspired by the Linux community's [x3d-toggle](https://github.com/pyrotiger/x3d-toggle) and the `amd_x3d_vcache` kernel driver.

## License

GPL v2. See [LICENSE](LICENSE) for details.

## Code Signing

<!-- Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org). -->
Code signing via SignPath is planned for a future release.
