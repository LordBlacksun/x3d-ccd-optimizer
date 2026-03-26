# X3D Dual CCD Optimizer

A lightweight, open-source Windows tool that manages CPU core affinity on AMD dual-CCD X3D processors. Features a real-time dashboard showing per-core CCD activity, automatic game detection, and intelligent process routing.

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

- **Detects your CCD topology automatically** — identifies which cores have V-Cache and which don't
- **Pins your game to the V-Cache CCD** when it launches — manual game list + automatic detection
- **Migrates background processes to the frequency CCD** — Discord, Spotify, browsers, etc. actually use your other cores instead of sitting idle
- **Restores everything when you're done gaming** — all affinities go back to default
- **Shows you exactly what's happening** — real-time dashboard with per-core load, frequency, process routing, and a live activity log

## Supported Processors

| CPU | Cores | V-Cache CCD |
|-----|-------|-------------|
| Ryzen 9 7950X3D | 8+8 | CCD0 (96MB L3) |
| Ryzen 9 7900X3D | 6+6 | CCD0 (96MB L3) |
| Ryzen 9 9950X3D | 8+8 | CCD0 (96MB L3) |
| Ryzen 9 9900X3D | 6+6 | CCD0 (96MB L3) |

## Status

**Phase 1 (Foundation)** — Core engine complete. Console-mode operation with topology detection, per-core monitoring, game detection, affinity management, and logging.

Phase 2 (Dashboard UI) and Phase 3 (Auto-Detection + Settings) are in progress.

## Getting Started

### Requirements

- Windows 10 21H2+ or Windows 11
- An AMD dual-CCD X3D processor (see supported list above)
- .NET 8 SDK (for building from source)

### Build and Run

```bash
git clone https://github.com/LordBlacksun/x3d-ccd-optimizer.git
cd x3d-ccd-optimizer
dotnet build
dotnet run --project src/X3DCcdOptimizer
```

### Configuration

On first run, a config file is created at `%APPDATA%\X3DCCDOptimizer\config.json`. You can add your games to the manual list:

```json
{
  "manualGames": [
    "elitedangerous64.exe",
    "ffxiv_dx11.exe",
    "stellaris.exe",
    "helldivers2.exe",
    "yourgame.exe"
  ]
}
```

## How It Works

1. On startup, the tool queries your CPU's cache topology to identify which cores belong to the V-Cache CCD and which belong to the standard CCD
2. It monitors running processes against your game list (and optionally via GPU usage heuristics)
3. When a game is detected in the foreground, it uses the Windows `SetProcessAffinityMask` API to:
   - Pin the game to the V-Cache CCD cores
   - Migrate background processes to the frequency CCD cores
4. When the game exits, all process affinities are restored to their defaults
5. Every action is logged with timestamps for full transparency

This is **not** a kernel driver — it uses standard Windows APIs that don't require admin privileges or driver signing. It supplements AMD's chipset drivers, not replaces them.

## Roadmap

- [x] **Phase 1** — Core engine: topology detection, affinity management, console output
- [ ] **Phase 2** — Real-time dashboard with per-core CCD visualization
- [ ] **Phase 3** — Auto-detection via GPU heuristics, settings UI, start-with-Windows
- [ ] **Phase 4** — Single-file release build, installer, CI/CD

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

## Acknowledgements

Inspired by the Linux community's [x3d-toggle](https://github.com/pyrotiger/x3d-toggle) tool and the AMD `amd_x3d_vcache` kernel driver. Built because Windows users deserve the same level of control.
