# X3D Dual CCD Optimizer — Phase 1 Build Prompt

You are building Phase 1 of the X3D Dual CCD Optimizer, a C# / .NET 8 application that manages CPU core affinity on AMD dual-CCD X3D processors. Read the full blueprint at `X3D_CCD_OPTIMIZER_BLUEPRINT.md` in the project root before starting.

## Project Setup

Create a new .NET 8 solution at the project root:
- Solution: `X3DCcdOptimizer.sln`
- Main project: `src/X3DCcdOptimizer/X3DCcdOptimizer.csproj` targeting `net8.0-windows`
- Test project: `tests/X3DCcdOptimizer.Tests/X3DCcdOptimizer.Tests.csproj`
- Output type: `Exe` (console mode for Phase 1, WinForms added in Phase 2)
- NuGet packages: `Serilog`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`
- Enable nullable reference types and implicit usings

## Phase 1 Scope — Foundation + Console Dashboard

Build these modules in order. Each module should compile and be testable before moving to the next.

### Step 1: Native P/Invoke Signatures (`Native/`)

Create `Kernel32.cs`, `User32.cs`, `Pdh.cs`, and `Structs.cs` with all the P/Invoke signatures documented in Section 8 of the blueprint. Include:
- `SetProcessAffinityMask` / `GetProcessAffinityMask`
- `GetLogicalProcessorInformationEx` with `SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX` structs
- `OpenProcess` / `CloseHandle` with process access constants
- `GetForegroundWindow` / `GetWindowThreadProcessId`
- PDH functions: `PdhOpenQuery`, `PdhAddEnglishCounter`, `PdhCollectQueryData`, `PdhGetFormattedCounterValue`, `PdhCloseQuery`
- `PDH_FMT_COUNTERVALUE` struct, `PDH_FMT_DOUBLE` constant

### Step 2: Models (`Models/`)

Create data models:
- `CpuTopology.cs` — topology info (masks, core lists, CPU model, L3 sizes)
- `CoreSnapshot.cs` — per-core metrics (index, CCD, load%, freq, temp)
- `AffinityEvent.cs` — affinity change event (timestamp, process, PID, action enum, detail string)

### Step 3: Configuration (`Config/`)

Create `AppConfig.cs`:
- Strongly-typed C# model matching the JSON schema in blueprint Section 4.9
- `Load()` / `Save()` methods using `System.Text.Json`
- Config path: `%APPDATA%\X3DCCDOptimizer\config.json`
- Create directory if it doesn't exist
- If config file doesn't exist, generate default config with the game list from the blueprint (Elite Dangerous, FFXIV, Stellaris, RE4, Helldivers 2, Star Citizen)

### Step 4: Logger (`Logging/`)

Create `AppLogger.cs`:
- Static Serilog configuration
- File sink: rolling, max 10MB, in `%APPDATA%\X3DCCDOptimizer\logs\`
- Console sink: for Phase 1 console output
- Log format: `[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}`

### Step 5: CCD Mapper (`Core/CcdMapper.cs`)

This is the most critical module. It must:
1. Call `GetLogicalProcessorInformationEx` with `RelationShipCache` (= 2) to enumerate L3 caches
2. Parse the returned buffer to identify cache groups and their processor masks
3. Find the CCD with the larger L3 (96MB = V-Cache CCD, 32MB = standard)
4. Build `VCacheMask` and `FrequencyMask` as `IntPtr` bitmasks
5. Populate `VCacheCores[]` and `FrequencyCores[]` from the masks
6. Query CPU model string via WMI `Win32_Processor`
7. If detection fails, log an error and check config for `ccdOverride`
8. If no override and detection failed, throw a descriptive exception

**IMPORTANT:** Test this carefully. The `GetLogicalProcessorInformationEx` buffer parsing is complex — the returned data is variable-length structs packed sequentially. Use `Marshal.PtrToStructure` and manual pointer arithmetic.

**WMI fallback:** If the P/Invoke path fails, fall back to `System.Management` to query `Win32_CacheMemory` for L3 sizes and `Win32_Processor` for core counts.

### Step 6: Performance Monitor (`Core/PerformanceMonitor.cs`)

Create the per-core metrics collector:
1. On init, open a PDH query and add counters for each logical core:
   - `\Processor Information(0,{N})\% Processor Utility` for load
   - `\Processor Information(0,{N})\Processor Frequency` for MHz
2. Provide a `CollectSnapshot()` method that:
   - Calls `PdhCollectQueryData`
   - Reads each counter's formatted value
   - Returns `CoreSnapshot[]` with CCD index derived from `CpuTopology`
3. Raise `SnapshotReady` event with the snapshot array
4. Run collection on a `System.Timers.Timer` (default 1000ms)
5. Gracefully handle missing counters (some systems may not expose frequency)

### Step 7: Game Detector (`Core/GameDetector.cs`)

Phase 1 is manual list only:
1. Accept a list of executable names from config (`manualGames`)
2. Provide `IsGame(string processName)` → bool (case-insensitive comparison)
3. Maintain state: `CurrentGame` (nullable) tracking the detected game process

### Step 8: Process Watcher (`Core/ProcessWatcher.cs`)

1. Timer-based polling at configurable interval
2. On each tick: enumerate running processes, check each against `GameDetector`
3. Track foreground window PID via `GetForegroundWindow` + `GetWindowThreadProcessId`
4. If a game is found AND it's the foreground window → raise `GameDetected` event
5. If previously tracked game is no longer running → raise `GameExited` event
6. Don't re-fire `GameDetected` if the same game is already tracked

### Step 9: Affinity Manager (`Core/AffinityManager.cs`)

1. On `GameDetected`:
   - Open the game process handle with `PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION`
   - Call `SetProcessAffinityMask` to pin it to `VCacheMask`
   - Enumerate all other processes, skip protected ones, try to set affinity to `FrequencyMask`
   - Store original masks for restoration
   - Emit `AffinityEvent` for each action (engaged, migrated, skipped)
2. On `GameExited`:
   - Restore all modified processes to original masks
   - Emit restoration events
3. Protected process list: match by name (case-insensitive). Include hardcoded system processes + config list.
4. Every `SetProcessAffinityMask` call in try/catch — access denied is expected for many processes.

### Step 10: Program.cs — Console Entry Point

Wire everything together:
1. Initialize logger
2. Load config
3. Run CCD Mapper → print detected topology (CPU model, core counts, L3 sizes, masks)
4. Start Performance Monitor → print per-core load/frequency every second in a formatted table
5. Start Process Watcher → print game detection events
6. Subscribe to Affinity Manager events → print all affinity changes
7. Run until Ctrl+C, then gracefully dispose everything

The console output should look something like:
```
[14:32:01 INF] X3D Dual CCD Optimizer v0.1.0
[14:32:01 INF] CPU: AMD Ryzen 9 7950X3D
[14:32:01 INF] CCD0 (V-Cache): Cores 0-7, L3: 96MB, Mask: 0x00FF
[14:32:01 INF] CCD1 (Frequency): Cores 8-15, L3: 32MB, Mask: 0xFF00
[14:32:01 INF] Monitoring started. Polling: 2000ms. Manual games: 6 entries.
[14:32:03 INF] === Core Status ===
[14:32:03 INF] CCD0 [V-Cache] C0:12% C1:8% C2:5% C3:3% C4:2% C5:1% C6:0% C7:0%
[14:32:03 INF] CCD1 [Freq]    C8:4%  C9:2% C10:1% C11:0% C12:0% C13:0% C14:0% C15:0%
[14:35:12 INF] GAME DETECTED: elitedangerous64.exe (PID 12840) [manual list]
[14:35:12 INF] ENGAGE: elitedangerous64.exe → CCD0 (V-Cache)
[14:35:12 INF] MIGRATE: discord.exe (PID 8412) → CCD1
[14:35:12 INF] MIGRATE: chrome.exe (PID 6204) → CCD1
[14:35:12 INF] SKIP: audiodg.exe — protected process
[14:35:12 INF] SKIP: svchost.exe (PID 892) — access denied
```

## Build Rules

1. All P/Invoke calls must have `SetLastError = true` and proper error checking via `Marshal.GetLastWin32Error()`
2. All process operations wrapped in try/catch — never crash on a single process failure
3. Log every significant action at INFO level, every failure at WARNING or ERROR
4. Use `IDisposable` pattern for `PerformanceMonitor` (PDH query handles) and `ProcessWatcher` (timer)
5. Config creates its directory and default file if they don't exist
6. Code should compile with `dotnet build` and run with `dotnet run` from the project directory
7. Keep the code clean and well-documented — this is open source, community will read it

## Exit Criteria

Phase 1 is complete when:
- [x] App starts, detects 7950X3D topology, prints correct CCD info
- [x] Per-core load/frequency displays in console and updates every second
- [x] Launching a game from the manual list triggers ENGAGE + MIGRATE log output
- [x] Task Manager confirms the game is pinned to cores 0-7 after engagement
- [x] Task Manager confirms background processes are on cores 8-15
- [x] Closing the game triggers RESTORE log output
- [x] Task Manager confirms all affinities are back to default
- [x] Protected/system processes are skipped with appropriate log messages
- [x] Config file is created on first run with sensible defaults
- [x] Log file is created in %APPDATA%\X3DCCDOptimizer\logs\
