# Contributing to X3D Dual CCD Optimizer

Thanks for your interest in contributing! This project is open source under GPL v2 and welcomes contributions from the community.

## Getting Started

### Prerequisites

- Windows 10 21H2+ or Windows 11
- .NET 8 SDK
- An AMD dual-CCD X3D processor (for runtime testing — not required for code contributions)

### Building

```bash
git clone https://github.com/LordBlacksun/x3d-ccd-optimizer.git
cd x3d-ccd-optimizer
dotnet build
```

### Running

```bash
dotnet run --project src/X3DCcdOptimizer
```

Run as Administrator for full affinity control over system processes.

## How to Contribute

### Reporting Bugs

Open an issue with:
- Your CPU model (e.g., Ryzen 9 7950X3D)
- Windows version
- Steps to reproduce
- Console output or log file from `%APPDATA%\X3DCCDOptimizer\logs\`

### Adding Games to the Known Games List

The easiest way to contribute — add game executables that should be detected automatically. Edit the `manualGames` default list in `src/X3DCcdOptimizer/Config/AppConfig.cs` or (when available) `src/X3DCcdOptimizer/Data/known_games.json`.

Include:
- Exact executable name (e.g., `game.exe`)
- Full game title in your PR description
- Whether the game benefits from V-Cache (most do, but verify if possible)

### Code Contributions

1. Fork the repository
2. Create a feature branch from `develop`: `git checkout -b feature/your-feature develop`
3. Make your changes
4. Ensure `dotnet build` succeeds with no warnings
5. Test on hardware if possible
6. Submit a pull request targeting `develop`

## Branch Strategy

- `master` — stable releases only
- `develop` — active development, PRs target this branch
- `feature/*` — feature branches off develop

## Code Guidelines

- Target .NET 8 (`net8.0-windows`)
- Enable nullable reference types
- All P/Invoke calls must have `SetLastError = true` and proper error checking
- All process operations wrapped in try/catch — never crash on a single process failure
- Log significant actions at INFO, failures at WARNING or ERROR
- Use `IDisposable` for anything holding native handles or timers
- Keep it simple — this tool does one thing and should do it well

## Project Structure

```
src/X3DCcdOptimizer/
├── Native/     # P/Invoke signatures (Kernel32, User32, PDH)
├── Models/     # Data models (CpuTopology, CoreSnapshot, AffinityEvent)
├── Config/     # JSON configuration
├── Logging/    # Serilog setup
├── Core/       # Engine (CcdMapper, PerformanceMonitor, ProcessWatcher, GameDetector, AffinityManager)
└── Program.cs  # Entry point
```

## License

By contributing, you agree that your contributions will be licensed under GPL v2.
