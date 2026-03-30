# X3D CCD Inspector

Real-time visibility and control for AMD Ryzen dual-CCD X3D processors.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white) ![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D6?logo=windows&logoColor=white) ![License](https://img.shields.io/badge/License-GPL%20v2-blue) ![AMD Ryzen](https://img.shields.io/badge/AMD-Ryzen%20X3D-ED1C24?logo=amd&logoColor=white)

---

## What It Does

AMD's scheduling stack for dual-CCD X3D processors involves multiple layers -- CPPC rankings, the amd3dvcache driver, Xbox Game Bar, GameMode power profiles, and core parking -- all working behind the scenes with zero user-facing feedback. X3D CCD Inspector makes that stack visible and gives you control where AMD doesn't.

- **See which CCD your game is running on** in real time
- **See AMD driver state**, Game Bar status, and GameMode activation
- **Set per-game CCD preference** through AMD's own driver interface -- no registry editing
- **In-game overlay** showing CCD state and driver status
- **Automatic game detection** via ETW with Steam, Epic, and GOG library scanning
- **Affinity pinning fallback** when the AMD driver isn't available

## What It Doesn't Do

This tool does not replace AMD's scheduling stack. It does not claim to make your games faster. It does not migrate background processes. When AMD's system is working correctly, the best thing to do is let it work -- and this tool shows you that it is. When something isn't working, it shows you that too, and gives you the controls to fix it.

## Who It's For

- X3D owners who want to understand what's happening inside their CPU
- Anyone who wants per-game CCD preference without editing the registry manually
- People who want sourced documentation on how the entire scheduling stack works

## Who It's Not For

- Single-CCD X3D owners (9800X3D, 7800X3D) -- you don't have the scheduling problem this addresses
- Users looking for a Process Lasso replacement -- this is not an affinity management tool

## Screenshots

<!-- [screenshot: dashboard] -->
<!-- [screenshot: overlay] -->
<!-- [screenshot: game library with CCD preference] -->

## Features

### Dashboard
System status panel showing CPU model, CCD details, AMD driver service status, driver state (PREFER_FREQ/PREFER_CACHE), Xbox Game Bar presence, and GameMode status -- all with color-coded status indicators. Active game panel shows detection method, process info, CCD distribution, thread counts, and driver action. Per-core CCD heatmaps with load and frequency display.

### CCD Map
Read-only view of which processes are running on which CCD. Grouped by CCD assignment with game badges.

### Game Library
Scans installed Steam, Epic, and GOG libraries automatically. Each game has a CCD preference dropdown:
- **Auto** -- let AMD's driver decide (recommended)
- **V-Cache** -- write PREFER_CACHE to AMD's per-app profile registry
- **Frequency** -- write PREFER_FREQ to AMD's per-app profile registry

When the AMD driver isn't available, a fallback affinity pin dropdown appears instead.

### Overlay
Compact in-game display showing game name, active CCD, and driver state. Only visible when the game is in the foreground -- hides automatically when you Alt-Tab. Position configurable (corners), OLED-safe with pixel shift. Toggle with Ctrl+Shift+O.

### Activity Log
Always-visible event log showing game detection, driver state changes, Game Bar status changes, CCD preference application, and affinity pin operations.

### Fallback Affinity Pinning
When AMD's amd3dvcache driver is not loaded (CPPC set to Frequency/Cache/Disabled in BIOS), the per-game CCD preference system cannot work. In this scenario, the tool offers explicit opt-in affinity pinning -- game process only, never background processes. Protected process list enforced. Original affinity restored on game exit.

## How It Works

AMD's dual-CCD X3D scheduling relies on a chain: BIOS CPPC settings expose core rankings to Windows, the amd3dvcache driver switches rankings when GameMode activates, and Windows places threads on preferred cores. X3D CCD Inspector reads the driver's registry state, monitors Game Bar presence, and shows you the result in real time.

For per-game control, the tool writes to AMD's own per-app profile registry at `Preferences\App\{GameName}`, setting `EndsWith` (exe name) and `Type` (PREFER_CACHE or PREFER_FREQ). The driver handles the rest natively -- no affinity hacking, no fighting the scheduler.

For the full technical breakdown, see [CPPC Research](docs/CPPC_RESEARCH.md).

## Supported Processors

| Tier | Examples | Features |
|------|----------|----------|
| Dual-CCD X3D | 7950X3D, 7900X3D, 9950X3D, 9900X3D | Full: per-game CCD preference via AMD driver + affinity fallback |
| Dual-CCD Standard | 5950X, 5900X, 7950X, 7900X, 9950X, 9900X | Affinity pinning only (no V-Cache driver) |

Single-CCD processors are detected and shown a friendly exit dialog.

## Requirements

- **OS:** Windows 10/11 64-bit
- **CPU:** AMD Ryzen dual-CCD processor
- **Runtime:** .NET 8 (included in self-contained release)
- **Admin:** Required for ETW tracing, process inspection, and registry access
- **AMD chipset drivers:** Recommended for per-game CCD preference (provides amd3dvcache driver)

## Download & Install

Download the `.zip` from the latest [Release](../../releases), extract anywhere, and run `X3DCcdInspector.exe`. No installation required. Accept the UAC prompt for administrator access.

### Build from Source

```bash
git clone https://github.com/LordBlacksun/x3d-ccd-optimizer.git
cd x3d-ccd-optimizer
dotnet build
dotnet test
dotnet run --project src/X3DCcdInspector
```

## Research & Documentation

These documents consolidate scattered community knowledge with primary source citations:

- [CPPC Research](docs/CPPC_RESEARCH.md) -- how CPPC2 preferred cores, the amd3dvcache driver, and Windows scheduling interact on dual-CCD X3D processors
- [Benchmark Research](docs/BENCHMARK_RESEARCH.md) -- why migrating background processes causes performance regression
- [PMC Research](docs/PMC_RESEARCH.md) -- feasibility of hardware performance counter profiling for automatic CCD classification

## FAQ

**Does this make my games faster?**
No. This tool does not claim to improve performance. It shows you what AMD's scheduling stack is doing and gives you control when the defaults don't match your preference. In most cases, AMD's system works correctly and the best thing to do is leave it alone.

**How is this different from Process Lasso?**
Process Lasso is a general-purpose process manager. This tool is purpose-built for AMD X3D dual-CCD scheduling: it understands CCD topology, integrates with AMD's V-Cache driver, provides automatic game detection, and shows you the entire scheduling chain in one dashboard.

**Why does the tool do nothing when AMD's driver is working?**
That's the point. When the driver correctly switches to PREFER_CACHE for your game, the tool shows you it happened. No intervention needed. The tool adds value when the driver *doesn't* trigger, or when you want a specific game on a specific CCD regardless of what Game Bar decides.

**What if Game Bar isn't triggering for my game?**
Set a per-game CCD preference in the Game Library tab. This writes directly to AMD's driver registry, bypassing Game Bar entirely. The driver will apply your preference whenever it detects the game's process.

**Is this safe?**
The tool works with AMD's scheduling stack, not against it. Per-game CCD preference uses AMD's own registry interface. Affinity pinning (fallback only) uses standard Windows APIs and restores original state on game exit. No background processes are ever modified. Protected process list prevents accidental modification of system infrastructure.

**Does it need admin rights?**
Yes. ETW kernel tracing, process inspection, and HKLM registry access all require administrator privileges. The app is fully open source.

## How This Was Built

Architecture, design, QA, and testing by [LordBlacksun](https://github.com/LordBlacksun). Implementation by [Claude Code](https://claude.ai/claude-code) (Anthropic). Every change is reviewed, tested on real hardware, and approved before commit.

## Acknowledgements

- [cocafe/vcache-tray](https://github.com/cocafe/vcache-tray) -- discovered and documented the AMD V-Cache driver registry interface
- AMD -- CPPC, the 3D V-Cache driver, and the scheduling infrastructure this tool builds on
- JayzTwoCents -- BIOS research on CPPC Preferred Cores "Driver" setting
- Linux community's [x3d_vcache kernel driver](https://github.com/torvalds/linux/blob/master/drivers/platform/x86/amd/x3d_vcache.c) -- ACPI DSM mechanism documentation

## License

GPL v2. See [LICENSE](LICENSE) for details.
