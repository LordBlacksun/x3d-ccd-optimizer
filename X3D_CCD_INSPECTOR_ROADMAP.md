# X3D CCD Inspector — Transition Roadmap

**Date:** 2026-03-30
**Previous name:** X3D CCD Optimizer
**New name:** X3D CCD Inspector
**Direction:** Visibility and control tool, not an optimizer

---

## Project Identity

A real-time visibility and control tool for AMD Ryzen dual-CCD X3D processors.

AMD's scheduling stack for dual-CCD X3D processors (7950X3D, 7900X3D, 9950X3D, 9900X3D) involves multiple layers — CPPC rankings, the amd3dvcache driver, Xbox Game Bar, GameMode power profiles, and core parking — all working behind the scenes with zero user-facing feedback. X3D CCD Inspector makes that stack visible and gives you control where AMD doesn't.

### What it does

- See which CCD your game is running on in real time
- See whether Game Bar triggered the GameMode CCD switch
- See the AMD driver's current state (PREFER_FREQ / PREFER_CACHE)
- Automatic game detection via ETW with near-instant response
- Game library scanning (Steam, Epic, GOG) with per-game CCD preference
- Set per-game V-Cache or Frequency preference through AMD's own driver interface
- Research-backed documentation on how the entire scheduling stack works

### What it doesn't do

This tool does not replace AMD's scheduling stack. It does not claim to make your games faster. It does not migrate background processes. When AMD's system is working correctly, the best thing to do is let it work — and this tool shows you that it is. When something isn't working, it shows you that too, and gives you the controls to fix it.

### Who it's for

- X3D owners who want to understand what's happening inside their CPU
- Anyone who wants per-game CCD preference without editing the registry manually
- People who are tired of conflicting advice on overclock.net and want sourced documentation

### Who it's not for

- Single-CCD X3D owners (9800X3D, 7800X3D) — you don't have the scheduling problem this addresses
- Users looking for a Process Lasso replacement — this is not an affinity management tool

---

## What Stays From Current Codebase

- ETW game detection (the core)
- Three-tier detection (Manual → Library Scan → GPU heuristic)
- LiteDB game database
- Steam/Epic/GOG library scanning
- Game Library tab with box art
- Auto-updater and self-updater
- Installer (Inno Setup)
- Test infrastructure
- About dialog with AI disclosure

## What Gets Cut

- `AffinityManager.MigrateBackground()` — gone entirely
- `AffinityManager.MigrateNewProcesses()` — gone entirely
- Re-migration timer — gone
- Monitor vs Optimize mode toggle — gone
- All affinity mask setting for background processes — gone
- `SwitchToOptimize` / `SwitchToMonitor` methods — reworked into unified mode
- Process Rules tab — gone
- Any claim of performance optimization

## What Gets Reworked

- `AffinityManager` → becomes `CcdMonitor` or `SchedulingInspector`
- `VCacheDriverManager` → stays, becomes the primary control mechanism
- Recovery system → simplified, only needs to clean up driver registry entries
- Settings UI → strategy selection replaced with system status and per-game preferences
- `OptimizeStrategy` enum → gone or renamed
- Dashboard → real-time visibility panel (see design below)
- Process Router tab → read-only CCD Map (renamed)

## What Gets Added

- System state detection at startup (amd3dvcacheSvc status, Game Bar presence, DefaultType)
- Real-time thread-to-CCD mapping for active game
- Per-game CCD preference UI writing to AMD's `\Preferences\App` registry
- Affinity pinning as explicit opt-in fallback when driver not present (game-only, zero background migration)
- Protected process list (safety net regardless of mode)
- Research/Help tab linking to wiki and documentation

---

## Dashboard Redesign

### System Status Panel (top)

- CPU detected: Ryzen 9 7950X3D (Dual-CCD X3D)
- CCD0: V-Cache (96MB L3, cores 0-15)
- CCD1: Frequency (32MB L3, cores 16-31)
- AMD Driver: Running / Not Running
- Driver State: PREFER_FREQ / PREFER_CACHE
- Game Bar: Running / Not Running
- GameMode: Active / Inactive

### Active Game Panel (center)

- No game detected / Game: ffxiv_dx11.exe
- Detected via: ETW / Library Rule / GPU Heuristic
- Running on: CCD0 (V-Cache) / CCD1 (Frequency) / Both
- Thread distribution: 12 threads CCD0, 4 threads CCD1
- CCD Preference: Auto / V-Cache / Frequency (per-game setting)
- Driver action: GameMode triggered PREFER_CACHE / No action / Manual override active

### Activity Log (bottom)

- Timestamped events: game detected, game exited, driver state changed, Game Bar triggered, preference applied

---

## Per-Game CCD Preference System

Writes to AMD's own `\Preferences\App` registry — not affinity masks.

### Game Library Tab

Each game gets a CCD preference dropdown:
- **Auto** — let AMD's driver decide
- **V-Cache** — write PREFER_CACHE to `\Preferences\App\{GameName}`
- **Frequency** — write PREFER_FREQ to `\Preferences\App\{GameName}`

### On Game Detect

1. Look up profile in LiteDB
2. If Auto → do nothing, let the driver handle it
3. If V-Cache or Frequency → write per-app entry to driver registry
4. Log the action

### On Game Exit

Leave per-app registry entries persistent. AMD's driver handles it natively next time without our tool running. More respectful to AMD's stack.

### Driver Not Present

Gray out the preference dropdown. Show why. Offer affinity pinning fallback if user explicitly enables it.

---

## Affinity Pinning as Fallback

Only available when AMD driver is not detected. Explicit opt-in.

- Pins ONLY the game process to V-Cache CCD
- Zero background migration
- Protected process list enforced
- UI clearly states: "AMD 3D V-Cache driver not detected. Per-game CCD preference unavailable. Affinity-based game pinning available as fallback."

---

## Tab Structure

| Tab | Purpose | Status |
|-----|---------|--------|
| Dashboard | Real-time system and game status | Redesigned |
| Game Library | Library with per-game CCD preference | Stays, gains dropdown |
| CCD Map | Read-only view of process-to-CCD distribution | Reworked from Process Router |
| Settings | System preferences, fallback behavior | Reworked |
| Research / Help | Links to wiki, CPPC docs, FAQ | New |
| About | Credits, AI disclosure | Stays |

Process Rules tab — removed.

---

## Protected Process List

Hardcoded, never migrated regardless of any mode or configuration:

**AMD CCD Scheduling:**
- amd3dvcacheSvc.exe
- amd3dvcacheUser.exe

**Windows Game Scheduling:**
- GameBarPresenceWriter.exe
- GameBar.exe
- GameBarFTServer.exe
- XboxGameBarWidgets.exe
- gamingservices.exe
- gamingservicesnet.exe

**GPU Driver Services:**
- NVDisplay.Container.exe
- atiesrxx.exe
- atieclxx.exe

**Windows Shell:**
- explorer.exe

---

## Implementation Phases

### Phase 1: Rename
X3D CCD Optimizer → X3D CCD Inspector everywhere:
- Solution/project names (.sln, .csproj)
- Assembly name and namespace
- Window titles
- About dialog
- Installer (Inno Setup)
- README
- All documentation references
- SESSION_LOG.md header

### Phase 2: Cut
Remove migration logic, Monitor/Optimize modes, background affinity code:
- Remove MigrateBackground(), MigrateNewProcesses()
- Remove re-migration timer
- Remove Monitor/Optimize toggle
- Remove background affinity mask setting
- Remove OptimizeStrategy enum (or rework)

### Phase 3: Dashboard
New system status panel, active game panel, activity log:
- System state detection (driver, Game Bar, DefaultType)
- Real-time game CCD display
- Thread-to-CCD mapping
- Activity log redesign

### Phase 4: Per-Game CCD Preference
Driver registry integration:
- Game Library dropdown (Auto / V-Cache / Frequency)
- Write to `\Preferences\App` registry on game detect
- Leave entries persistent on game exit
- Gray out when driver not present

### Phase 5: Fallback
Affinity pinning as explicit opt-in when driver not present:
- Game-only pinning, zero background migration
- Protected process list enforced
- Clear UI messaging about fallback status

### Phase 6: Tab Cleanup
- Remove Process Rules tab
- Rework Process Router into read-only CCD Map
- Add Research/Help tab
- Update Settings tab

### Phase 7: Tests
- Update test suite for new architecture
- Remove migration tests
- Add visibility and preference tests
- Add driver detection tests
- Add protected process list tests

### Phase 8: Documentation
- New README with Inspector framing
- Updated wiki
- Research docs linked from app
- Screenshots

---

## Key Principles

- Never fight AMD's scheduling stack. Work with it or stay out of the way.
- Visibility over intervention. Show what's happening, let the user decide.
- When AMD's system works, acknowledge it. Don't pretend to improve what's already working.
- All technical claims sourced and documented.
- Honest about what the tool does and doesn't do.
- AI disclosure maintained: "Implementation by Claude (Anthropic). Architecture, design, QA, and testing by the developer."
