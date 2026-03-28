# Security Audit V2 — X3D Dual CCD Optimizer

**Date:** 2026-03-28
**Auditor:** Claude Opus 4.6 (1M context)
**Scope:** Full codebase (`src/X3DCcdOptimizer/`, `installer/`, `tests/`)
**Previous audit:** Session 17 (defensive coding), Session 11 (general)

---

## Summary

| Severity | Count |
|----------|-------|
| Critical | 0     |
| High     | 3     |
| Medium   | 5     |
| Low      | 5     |
| Info     | 4     |
| **Total**| **17**|

No critical vulnerabilities found. The codebase has improved significantly since the Session 11 audit. The primary concern areas are: callbacks fired inside a shared lock (potential deadlock), registry write without read-back verification, and several robustness improvements.

---

## Findings

### HIGH-001: AffinityChanged callbacks fired inside _syncLock

- **File:** `Core/AffinityManager.cs`, lines 672-725
- **Severity:** High
- **Category:** Thread Safety

**Description:** The `Emit()` method fires `AffinityChanged?.Invoke(evt)` at line 724. This method is called from within regions that hold `_syncLock` (e.g., `OnGameDetected`, `RestoreAll`, `MigrateNewProcesses`). Subscribers include `MainViewModel.OnAffinityChanged`, `OverlayViewModel.OnAffinityChanged`, and `ProcessRouterViewModel`. These subscribers call `Dispatcher.BeginInvoke()`, which is asynchronous and safe. However, if any future subscriber performs a synchronous callback into AffinityManager (e.g., querying state), a deadlock will occur.

**Attack scenario:** Not externally exploitable. Risk is self-inflicted deadlock from future code changes, causing the app to freeze permanently during gameplay.

**Recommended fix:** Fire callbacks outside the lock:
```csharp
AffinityEvent evt;
lock (_syncLock) { /* build evt, mutate state */ }
AffinityChanged?.Invoke(evt); // Outside lock
```

---

### HIGH-002: Registry write not verified (VCacheDriverManager)

- **File:** `Core/VCacheDriverManager.cs`, lines 100-112
- **Severity:** High
- **Category:** Registry Operations

**Description:** `WritePreference()` calls `key.SetValue()` but does not read back the value to confirm the write succeeded. If another process (including the AMD driver itself) modifies the key concurrently, the write could be silently overwritten. The `SetValue` call also does not use any locking mechanism.

**Attack scenario:** A TOCTOU race where another process writes a different value between this app's `SetValue` and the next game session, silently disabling V-Cache preference without any log entry.

**Recommended fix:** Read back the value after writing and log a warning if it differs:
```csharp
key.SetValue(RegValueName, value, RegistryValueKind.DWord);
var readBack = key.GetValue(RegValueName);
if (readBack is int written && written != value)
    Log.Warning("Registry write verification failed: wrote {Expected}, read {Actual}", value, written);
```

---

### HIGH-003: No input length validation for user-entered process names

- **File:** `ViewModels/SettingsViewModel.cs`, lines 287-302 (AddGameCommand)
- **Severity:** High
- **Category:** Input Validation

**Description:** The V-Cache CCD game list still accepts manual text entry. User can type a string of arbitrary length with no character validation. While these strings are only used for case-insensitive HashSet lookups (no file I/O, no shell execution), an extremely long string (e.g., 10MB pasted text) could:
- Cause the config JSON to grow unboundedly
- Slow down HashSet operations on every poll cycle
- Crash the WPF ListBox rendering

**Attack scenario:** User pastes a very large clipboard content into the game name field, causing persistent config bloat and UI degradation across restarts.

**Recommended fix:**
```csharp
var game = NewGameText.Trim();
if (game.Length > 260) // MAX_PATH
{
    Log.Warning("Game name too long, truncating");
    game = game[..260];
}
if (game.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
{
    Log.Warning("Game name contains invalid characters");
    return;
}
```

---

### MEDIUM-001: CloseHandle declared with SetLastError=true

- **File:** `Native/Kernel32.cs`, line 29-30
- **Severity:** Medium
- **Category:** P/Invoke

**Description:** `CloseHandle` is declared with `SetLastError = true`. If `CloseHandle` is called after a failed Win32 API call but before reading `Marshal.GetLastWin32Error()`, it will clobber the error code. Currently the code reads the error code before calling `CloseHandle` in all paths (AffinityManager lines 241, 268, 309, 479, 494), so this is not actively exploitable. However, it creates a landmine for future code changes.

**Recommended fix:**
```csharp
[DllImport("kernel32.dll")]
internal static extern bool CloseHandle(IntPtr hObject);
```

---

### MEDIUM-002: GpuMonitor creates new WMI searcher every poll cycle

- **File:** `Core/GpuMonitor.cs`, lines 51-76
- **Severity:** Medium
- **Category:** Resource Management

**Description:** `GetGpuUsage()` creates a new `ManagementObjectSearcher` on every call (every 2 seconds during auto-detection). WMI connections have non-trivial overhead. The idle-skip optimization (lines 40-45) halves the frequency when no game is active, but during active detection the full rate applies.

**Attack scenario:** Not a security vulnerability per se, but a resource concern. On systems with many GPU engines (multi-GPU setups), the WMI enumeration could become slow enough to delay game detection.

**Recommended fix:** Cache the `ManagementObjectSearcher` instance and reuse across calls, or use a pre-compiled WQL query.

---

### MEDIUM-003: Singleton mutex name is predictable

- **File:** `App.xaml.cs`, line 46
- **Severity:** Medium
- **Category:** Denial of Service

**Description:** The mutex name `@"Global\X3DCcdOptimizer_SingleInstance"` is hardcoded and predictable. Any process running before this app can create a mutex with this name, causing the app to show "already running" and exit.

**Attack scenario:** Malware or prank utility creates the mutex to prevent the optimizer from starting. User sees "already running" dialog but cannot find the running instance.

**Recommended fix:** Use the installer's AppId GUID as the mutex name: `@"Global\{B7F3A2E1-5D4C-4E8B-9F1A-3C6D8E2B7A50}"`. Consider adding a security descriptor to restrict mutex access to the current user's SID.

---

### MEDIUM-004: Overly broad exception catches in scan loops

- **File:** `Core/AffinityManager.cs`, lines 650-653; `Core/ProcessWatcher.cs`, lines 177, 270
- **Severity:** Medium
- **Category:** Exception Handling

**Description:** Several scan loops catch bare `Exception` or even parameterless `catch` and silently continue. While this prevents crashes from individual process failures (which is correct), it also swallows `OutOfMemoryException` and `StackOverflowException`. More importantly, repeated failures from the same root cause (e.g., permission change) are hidden at `Debug` log level.

**Recommended fix:** Re-throw fatal exceptions:
```csharp
catch (OutOfMemoryException) { throw; }
catch (Exception ex) { Log.Debug(...); }
```

---

### MEDIUM-005: Config enum fallbacks are silent

- **File:** `Config/AppConfig.cs`, lines 248-260
- **Severity:** Medium
- **Category:** Input Validation

**Description:** `GetOperationMode()` and `GetOptimizeStrategy()` silently fall back to defaults when the config contains unrecognized values. If a user manually edits `config.json` with a typo (e.g., `"operationMode": "optmize"`), the app silently runs in Monitor mode with no indication that the setting was ignored.

**Recommended fix:** Log a warning when an unrecognized value is encountered:
```csharp
public OperationMode GetOperationMode()
{
    if (string.Equals(OperationMode, "optimize", StringComparison.OrdinalIgnoreCase))
        return Models.OperationMode.Optimize;
    if (!string.Equals(OperationMode, "monitor", StringComparison.OrdinalIgnoreCase))
        Log.Warning("Unrecognized operationMode '{Value}', defaulting to Monitor", OperationMode);
    return Models.OperationMode.Monitor;
}
```

---

### LOW-001: GpuMonitor._idleSkipCounter not thread-safe

- **File:** `Core/GpuMonitor.cs`, lines 14, 42-43
- **Severity:** Low
- **Category:** Thread Safety

**Description:** `_idleSkipCounter` is incremented without synchronization. If `GetGpuUsage()` is called from multiple threads, the counter could produce incorrect values. In practice, it's only called from `ProcessWatcher.Poll()` on a single timer thread, so this is cosmetic.

**Recommended fix:** Use `Interlocked.Increment(ref _idleSkipCounter)`.

---

### LOW-002: IconGenerator.Cache not thread-safe

- **File:** `Tray/IconGenerator.cs`, line 10
- **Severity:** Low
- **Category:** Thread Safety

**Description:** `Dictionary<string, Icon> Cache` is accessed without synchronization. In practice, `GetIcon()` is called from the UI thread (via TrayIconManager property change handlers), so no real race exists.

**Recommended fix:** Use `ConcurrentDictionary<string, Icon>` for defense in depth.

---

### LOW-003: VDF parser has no recursion depth limit

- **File:** `Core/GameLibraryScanner.cs` (VdfParser inner class)
- **Severity:** Low
- **Category:** Input Validation

**Description:** The VDF parser processes Steam's `libraryfolders.vdf` file with recursive block parsing but no depth limit. A maliciously crafted VDF file with deeply nested blocks could cause a `StackOverflowException`.

**Attack scenario:** Requires write access to Steam's installation directory, which already implies full user compromise. Low practical risk.

**Recommended fix:** Add a depth parameter with a limit of ~50 levels.

---

### LOW-004: Recovery file bare catches

- **File:** `Core/RecoveryManager.cs`, lines 126-129, 189-192
- **Severity:** Low
- **Category:** Exception Handling

**Description:** Parameterless `catch` blocks in the recovery loop silently skip processes when any exception occurs, including unexpected ones. The recovery path should be as robust as possible since it runs after an unclean shutdown.

**Recommended fix:** Log the exception at Debug level inside the bare catch:
```csharp
catch (Exception ex) { Log.Debug(ex, "Recovery skipped for PID {Pid}", proc.Id); skipped++; }
```

---

### LOW-005: Hotkey registration failure not surfaced to UI

- **File:** `App.xaml.cs`, lines 349-352
- **Severity:** Low
- **Category:** User Experience / Security

**Description:** If `RegisterHotKey` fails (another app owns Ctrl+Shift+O), the failure is logged but not shown to the user. The overlay toggle button and context menu still work, so the impact is limited to the hotkey not functioning.

**Recommended fix:** Set a flag and show a tooltip or status bar message: "Overlay hotkey unavailable — another app may be using Ctrl+Shift+O".

---

### INFO-001: Process.GetProcesses() resource cost in scan loops

- **File:** `Core/AffinityManager.cs`, lines 282, 596; `Core/ProcessWatcher.cs`, lines 142, 183
- **Severity:** Info
- **Category:** Resource Management

**Description:** Multiple calls to `Process.GetProcesses()` occur per poll cycle (ProcessWatcher known game scan + GPU auto-detect, AffinityManager re-migration every 3s). Each call allocates and returns all process objects. All process objects ARE properly disposed in `finally` blocks. On a system with hundreds of processes, this is measurable overhead but not dangerous.

**Note:** No security vulnerability. Included for completeness.

---

### INFO-002: Recovery file written on every process migration

- **File:** `Core/RecoveryManager.cs`, lines 231-245
- **Severity:** Info
- **Category:** File I/O

**Description:** `AddModifiedProcess()` writes the full recovery.json file every time a process is migrated. During the first engagement cycle, dozens of processes may be migrated in rapid succession, causing dozens of temp-file-write-and-move operations. This is I/O-intensive but not a security concern. The atomic write pattern (temp + move) is correct.

---

### INFO-003: Hardcoded registry path for amd3dvcache driver

- **File:** `Core/VCacheDriverManager.cs`, line 18
- **Severity:** Info
- **Category:** Maintainability

**Description:** The registry path `SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences` is hardcoded. If AMD changes the driver's registry layout in a future chipset driver update, the app will silently fail to set preferences. The `CheckDriverInstalled()` method provides graceful degradation (falls back to AffinityPinning).

---

### INFO-004: RUNASADMIN compatibility flag in installer

- **File:** `installer/setup.iss`, lines 76-79
- **Severity:** Info
- **Category:** Privilege Management

**Description:** The installer sets the `RUNASADMIN` compatibility flag via `HKCU\...\AppCompatFlags\Layers`. This causes UAC elevation on every launch. The app also has its own elevation check in `App.xaml.cs` with a relaunch dialog. Having both mechanisms is redundant. The app's built-in elevation handling is more user-friendly (shows explanation dialog) while the registry flag is silent.

**Note:** The registry flag ensures elevation even if the app's code-based check is bypassed, providing defense in depth. Not a vulnerability.

---

## Admin Privilege Surface Analysis

Admin privileges are used for exactly three operations:

1. **`SetProcessAffinityMask`** (Kernel32.cs) — via `AffinityManager`. Requires `PROCESS_SET_INFORMATION` access to target processes.
2. **`OpenProcess`** (Kernel32.cs) — via `AffinityManager` and `RecoveryManager`. Opens handles to other processes.
3. **Registry write to `HKLM\...\amd3dvcache\Preferences`** (VCacheDriverManager.cs) — sets the AMD driver's CCD scheduling preference.

No other privileged operations exist. The app does not:
- Create services or drivers
- Modify system files
- Write to HKLM outside the amd3dvcache key
- Open network sockets or listen on ports
- Execute external processes (except `Process.Start` for opening URLs/folders in explorer, which uses `UseShellExecute = true`)

**IPC surface:** The app has no named pipes, TCP listeners, or COM interfaces. The only IPC mechanism is the single-instance mutex (`Global\X3DCcdOptimizer_SingleInstance`), which is one-way (existence check only).

---

## P/Invoke Correctness Review

| Declaration | SetLastError | Correctness |
|---|---|---|
| `SetProcessAffinityMask` | Yes | Correct |
| `GetProcessAffinityMask` | Yes | Correct |
| `GetLogicalProcessorInformationEx` | Yes | Correct |
| `OpenProcess` | Yes | Correct |
| `CloseHandle` | Yes | **Should be removed** (MEDIUM-001) |
| `GetForegroundWindow` | No | Correct (returns NULL on failure) |
| `GetWindowThreadProcessId` | Yes | Correct |
| `RegisterHotKey` | Yes | Correct |
| `UnregisterHotKey` | Yes | Correct |
| PDH functions (Pdh.cs) | No | Correct (use return codes, not GetLastError) |

**CACHE_RELATIONSHIP struct:** The 18-byte `Reserved` field (Structs.cs:24) is correct per the Windows SDK. `GroupMask` is at offset 32 as required.

---

## Thread Safety Review

| Shared State | Protection | Assessment |
|---|---|---|
| `AffinityManager._originalMasks` | `_syncLock` | Correctly synchronized in all access paths |
| `AffinityManager._engaged, _currentGame` | `_syncLock` | Correctly synchronized |
| `GameDetector._currentGame` | `_gameLock` | Correctly synchronized |
| `GameDetector._launcherGames` | `volatile` | Correct for reference swap pattern |
| `PerformanceMonitor` disposal | `_disposeLock` | Prevents timer/Dispose race |
| `RecoveryManager._currentState` | `_lock` | Correctly synchronized |
| `GpuMonitor._idleSkipCounter` | None | **LOW-001** — cosmetic only |
| `IconGenerator.Cache` | None | **LOW-002** — practically single-threaded |
| UI updates from background | `Dispatcher.BeginInvoke` | Correctly marshaled everywhere |

---

## Comparison with Previous Audits

- **Session 11** found 12 actionable issues (3 high, 6 medium, 3 low). All were fixed.
- **Session 17** found 8 actionable issues (2 medium, 6 low). All were fixed in session 18.
- **This audit** found 13 actionable issues (3 high, 5 medium, 5 low) + 4 informational.

New findings are primarily around defensive coding improvements, not architectural flaws. The codebase's security posture has improved significantly through prior audit cycles.
