# Security Audit Report — X3D Dual CCD Optimizer

**Date:** 2026-03-27
**Auditor:** Claude Opus 4.6 (1M context)
**Scope:** Full codebase — every `.cs`, `.xaml`, `.csproj`, `.manifest`, `.json` file
**Commit:** `4a5b095` (develop branch)
**Context:** Admin-elevated Windows desktop app that manipulates process affinities, writes HKLM registry keys, queries WMI, and persists state to disk on gaming PCs.

---

## Findings

### SEC-001 — Non-atomic file writes for config.json and recovery.json
**Severity:** High
**Files:** `Config/AppConfig.cs:199`, `Core/RecoveryManager.cs:225`

```csharp
// AppConfig.cs:199
File.WriteAllText(ConfigPath, json);

// RecoveryManager.cs:225
File.WriteAllText(RecoveryPath, json);
```

**Description:** `File.WriteAllText` is not atomic. If the process crashes, the machine loses power, or the write is interrupted mid-stream, the file will be partially written / corrupt. Config.json corruption is recoverable (falls back to defaults), but recovery.json corruption during an active optimization session means the next launch cannot restore process affinities.

**Attack scenario:** Power loss or BSOD while game is running with Optimize mode active. Recovery.json is corrupt on next boot. Modified process affinities are never restored. Background processes remain pinned to the frequency CCD until manual intervention or reboot.

**Recommended fix:** Write to a temporary file, then atomically replace via rename.

```csharp
// RecoveryManager.cs — WriteState()
private static void WriteState()
{
    try
    {
        Directory.CreateDirectory(RecoveryDir);
        var json = JsonSerializer.Serialize(_currentState, JsonOptions);
        var tempPath = RecoveryPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, RecoveryPath, overwrite: true);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to write recovery state");
    }
}
```

Apply the same pattern to `AppConfig.Save()`.

---

### SEC-002 — No single-instance enforcement
**Severity:** High
**File:** `App.xaml.cs`

**Description:** No mutex or other mechanism prevents multiple instances from running simultaneously. Two instances would both poll processes, both attempt to set affinities, and both write to the same recovery.json file. The second instance would read the first's recovery.json at startup and interpret it as a dirty shutdown, resetting all affinities while the first instance is still actively managing them.

**Attack scenario:** User accidentally launches the app twice (e.g., from tray + desktop shortcut). Instance B reads instance A's recovery.json, resets all affinities to all-cores. Instance A is now managing stale state. Both instances now fight over process affinities on every poll cycle. The recovery file is repeatedly overwritten by both.

**Recommended fix:** Add a named mutex at the start of `OnStartup`.

```csharp
// App.xaml.cs — add field
private Mutex? _singleInstanceMutex;

// App.xaml.cs — OnStartup, before any other work
_singleInstanceMutex = new Mutex(true, "Global\\X3DCcdOptimizer_SingleInstance", out var createdNew);
if (!createdNew)
{
    MessageBox.Show("X3D CCD Optimizer is already running.", "Already Running",
        MessageBoxButton.OK, MessageBoxImage.Information);
    Shutdown();
    return;
}
```

---

### SEC-003 — Recovery.json can reset affinities on arbitrary processes
**Severity:** High
**File:** `Core/RecoveryManager.cs:87-140`

```csharp
foreach (var entry in state.ModifiedProcesses)
{
    var nameWithoutExe = entry.Name.EndsWith(".exe", ...) ? entry.Name[..^4] : entry.Name;
    if (!runningProcesses.TryGetValue(nameWithoutExe, out var matches))
    { ... }
    foreach (var proc in matches)
    {
        // Reset to all cores (full system mask)
        var fullMask = new IntPtr(-1);
        Kernel32.SetProcessAffinityMask(handle, fullMask);
    }
}
```

**Description:** Recovery reads process names from recovery.json and resets ALL running processes matching each name to full CPU affinity. A malicious or corrupted recovery.json could list system-critical process names, causing their affinities to be modified. The code does not check the entries against the protected process list before acting.

**Attack scenario:** Attacker with write access to `%APPDATA%\X3DCCDOptimizer\` crafts a recovery.json listing "csrss" or "lsass". On next app launch, the recovery code opens these processes and calls SetProcessAffinityMask on them. While resetting to all-cores is generally benign, modifying system process affinities at all is unexpected and violates the app's own protected-process policy.

**Recommended fix:** Filter recovery entries through the protected process list.

```csharp
// RecoveryManager.cs — RecoverAffinityPinning(), before the foreach loop
var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "System", "Idle", "csrss", "smss", "services", "wininit",
    "lsass", "winlogon", "dwm", "audiodg", "fontdrvhost",
    "Registry", "Memory Compression", "svchost", "X3DCcdOptimizer"
};

foreach (var entry in state.ModifiedProcesses)
{
    var nameWithoutExe = entry.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        ? entry.Name[..^4] : entry.Name;

    if (protectedNames.Contains(nameWithoutExe))
    {
        Log.Warning("Recovery: skipping protected process {Name}", entry.Name);
        skipped++;
        continue;
    }
    // ... existing restoration logic
}
```

---

### SEC-004 — VCacheDriverManager lazy init is not thread-safe
**Severity:** Medium
**File:** `Core/VCacheDriverManager.cs:29`

```csharp
private static bool? _isDriverAvailable;

public static bool IsDriverAvailable
{
    get
    {
        _isDriverAvailable ??= CheckDriverInstalled();
        return _isDriverAvailable.Value;
    }
}
```

**Description:** The `??=` operator on a nullable static field is not atomic. If two threads access `IsDriverAvailable` concurrently before it's initialized, both will call `CheckDriverInstalled()`. This is a benign race (both get the same result), but it violates the "cached on first access" intent and does redundant registry I/O.

**Attack scenario:** No direct exploit, but under heavy thread contention at startup, redundant registry reads could slow initialization.

**Recommended fix:** Use `Lazy<bool>` for thread-safe lazy initialization.

```csharp
private static readonly Lazy<bool> _isDriverAvailable = new(CheckDriverInstalled);

public static bool IsDriverAvailable => _isDriverAvailable.Value;
```

---

### SEC-005 — GameDetector.CurrentGame not synchronized
**Severity:** Medium
**File:** `Core/GameDetector.cs:21`

```csharp
public ProcessInfo? CurrentGame { get; set; }
```

**Description:** `CurrentGame` is set from the `ProcessWatcher` timer thread (ThreadPool) and read from multiple threads including the UI dispatcher. There is no synchronization — no `lock`, no `volatile`, no `Interlocked`. This is a data race. On x86 this is practically safe due to memory model guarantees, but it violates .NET's threading rules and could cause stale reads on other architectures or future JIT optimizations.

**Attack scenario:** No direct security exploit, but a stale read could cause a game to be double-detected or a game exit to be missed, leading to stuck affinity states.

**Recommended fix:** Use a lock or make the property thread-safe.

```csharp
private ProcessInfo? _currentGame;
private readonly object _gameLock = new();

public ProcessInfo? CurrentGame
{
    get { lock (_gameLock) return _currentGame; }
    set { lock (_gameLock) _currentGame = value; }
}
```

---

### SEC-006 — Config values not validated after deserialization
**Severity:** Medium
**File:** `Config/AppConfig.cs:170-193`

```csharp
public static AppConfig Load()
{
    // ... deserialize ...
    var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
    if (config != null)
        return config;  // No validation of values
}
```

**Description:** Deserialized config values are used directly without range validation. A hand-edited or corrupt config could contain: `PollingIntervalMs: 0` (timer with 0ms interval = CPU spin), `PollingIntervalMs: -1` (ArgumentOutOfRangeException in Timer), `GpuThresholdPercent: -50` (never detects games), `DetectionDelaySeconds: 999999` (never fires), or `CcdOverride.VCacheCores: [-1, 999]` (undefined mask shifts).

**Attack scenario:** User hand-edits config.json and sets `pollingIntervalMs: 0`. App launches and creates a timer with 0ms interval. Timer fires continuously, consuming 100% CPU. Or sets a negative value, crashing the app with an unhandled exception before recovery can run.

**Recommended fix:** Add a `Validate()` method called after `Load()`.

```csharp
public void Validate()
{
    PollingIntervalMs = Math.Clamp(PollingIntervalMs, 500, 30000);
    DashboardRefreshMs = Math.Clamp(DashboardRefreshMs, 500, 30000);
    AutoDetection.GpuThresholdPercent = Math.Clamp(AutoDetection.GpuThresholdPercent, 1, 100);
    AutoDetection.DetectionDelaySeconds = Math.Clamp(AutoDetection.DetectionDelaySeconds, 1, 60);
    AutoDetection.ExitDelaySeconds = Math.Clamp(AutoDetection.ExitDelaySeconds, 1, 120);
    Overlay.AutoHideSeconds = Math.Clamp(Overlay.AutoHideSeconds, 1, 300);
    Overlay.PixelShiftMinutes = Math.Clamp(Overlay.PixelShiftMinutes, 1, 60);
    Overlay.Opacity = Math.Clamp(Overlay.Opacity, 0.1, 1.0);

    if (CcdOverride is { VCacheCores: { } vc, FrequencyCores: { } fc })
    {
        if (vc.Any(c => c < 0 || c > 63) || fc.Any(c => c < 0 || c > 63))
        {
            Log.Warning("CcdOverride contains out-of-range core indices — ignoring override");
            CcdOverride = null;
        }
    }
}
```

Call `config.Validate()` after `AppConfig.Load()` in `App.OnStartup`.

---

### SEC-007 — VCacheDriverManager does not validate registry value type or range
**Severity:** Medium
**File:** `Core/VCacheDriverManager.cs:47-55`

```csharp
public static int? GetCurrentPreference()
{
    var value = key.GetValue(RegValueName);
    if (value is int intVal)
        return intVal;
    return null;
}
```

**Description:** The registry value is only checked for being `int` type. If another program writes an unexpected value (e.g., `DefaultType = 99`), it would be silently accepted. More importantly, `WritePreference` writes arbitrary int values without clamping to 0/1. Currently the callers only pass `PREFER_FREQ` (0) and `PREFER_CACHE` (1), but there's no defensive check.

**Attack scenario:** Another program sets `DefaultType` to a value outside {0, 1}. `GetCurrentPreference()` returns the value but nothing validates it. If the optimizer reads this, it could make incorrect decisions. Or if a future code change passes an unexpected value to `WritePreference`, it would write invalid data to the driver registry.

**Recommended fix:** Validate the range on read and write.

```csharp
public static int? GetCurrentPreference()
{
    // ... existing code ...
    if (value is int intVal && intVal >= 0 && intVal <= 1)
        return intVal;
    if (value is int outOfRange)
        Log.Warning("amd3dvcache DefaultType has unexpected value: {Value}", outOfRange);
    return null;
}

private static bool WritePreference(int value)
{
    if (value is not (PREFER_FREQ or PREFER_CACHE))
        throw new ArgumentOutOfRangeException(nameof(value), $"Expected 0 or 1, got {value}");
    // ... existing write code ...
}
```

---

### SEC-008 — WMI queries in CcdMapper have no timeout
**Severity:** Medium
**File:** `Core/CcdMapper.cs:155-156`, `Core/CcdMapper.cs:209-210`

```csharp
// CcdMapper.cs:155 — no timeout set
using var cacheSearcher = new ManagementObjectSearcher(
    "SELECT * FROM Win32_CacheMemory WHERE Level = 5");

// CcdMapper.cs:209 — no timeout set
using var searcher = new ManagementObjectSearcher(
    "SELECT Name, NumberOfCores FROM Win32_Processor");
```

**Description:** These WMI queries have no `Options.Timeout` set, unlike `GpuMonitor` which correctly sets 2-3 second timeouts. If the WMI service is hung, damaged, or under heavy load (common on gaming PCs with many monitoring tools), these queries can block indefinitely, hanging the application at startup with no UI visible and no way for the user to intervene.

**Attack scenario:** WMI service is stuck due to a misbehaving WMI provider (e.g., antivirus, hardware monitoring software). App hangs at startup with no window, no tray icon, no indication it's running. User launches it again (SEC-002 makes this worse). Eventually many hung instances accumulate.

**Recommended fix:** Add timeouts to all WMI queries.

```csharp
using var cacheSearcher = new ManagementObjectSearcher(
    "SELECT * FROM Win32_CacheMemory WHERE Level = 5");
cacheSearcher.Options.Timeout = TimeSpan.FromSeconds(10);

using var searcher = new ManagementObjectSearcher(
    "SELECT Name, NumberOfCores FROM Win32_Processor");
searcher.Options.Timeout = TimeSpan.FromSeconds(10);
```

---

### SEC-009 — ProcessWatcher debounce state not synchronized with Dispose
**Severity:** Medium
**File:** `Core/ProcessWatcher.cs:17-24`, `Core/ProcessWatcher.cs:274-282`

```csharp
// Fields modified in Poll() (ThreadPool thread)
private int _autoDetectCandidatePid;
private string _autoDetectCandidateName = "";
private DateTime _autoDetectCandidateStart;
// ...
private bool _disposed;

// Dispose called from UI thread
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    _timer.Stop();
    _timer.Dispose();
}
```

**Description:** `_disposed` is checked in `Dispose()` without synchronization. While `System.Timers.Timer.Stop()` prevents new callbacks, a `Poll()` already in progress on the ThreadPool continues to run. `Poll()` does not check `_disposed`. After `_timer.Dispose()`, a still-running `Poll()` could access the timer or other state that's being torn down. The `_disposed` field should be `volatile` and checked at the top of `Poll()`.

**Attack scenario:** App shutdown races with a timer callback. Poll() runs after Dispose() has nulled out downstream references. NullReferenceException in ThreadPool thread is swallowed by the global exception handler but leaves the app in an undefined state during shutdown.

**Recommended fix:**

```csharp
private volatile bool _disposed;

private void Poll()
{
    if (_disposed) return;
    try
    {
        // ... existing poll logic ...
    }
    catch (Exception ex)
    {
        if (!_disposed) // Don't log during shutdown
            Log.Warning(ex, "Error during process scan");
    }
}
```

---

### SEC-010 — Manifest does not request admin elevation
**Severity:** Medium
**File:** `app.manifest:7`

```xml
<requestedExecutionLevel level="asInvoker" uiAccess="false" />
```

**Description:** The manifest requests `asInvoker` (inherit caller's privileges), not `requireAdministrator`. The CLAUDE.md documentation states "app already runs elevated" and the app writes to HKLM registry keys (VCacheDriverManager) and modifies system process affinities. If launched without elevation:
- `VCacheDriverManager.WritePreference()` will fail with `UnauthorizedAccessException` (handled, but DriverPreference strategy silently doesn't work)
- `SetProcessAffinityMask` will fail for many processes with access denied (handled as Skipped)
- `StartupManager` writes to HKCU which works without admin

The app functions but is significantly degraded without elevation, and provides no warning to the user about why things aren't working.

**Attack scenario:** User launches without admin. Selects DriverPreference strategy. App reports "DRIVER SET" never fires — only errors in the log. User thinks the app is broken. Or in AffinityPinning mode, most background processes can't be migrated — Optimize mode is largely ineffective.

**Recommended fix:** Either change the manifest to request admin, or add a runtime check with a clear user-facing warning.

Option A — Request admin in manifest:
```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

Option B — Runtime check (less disruptive):
```csharp
// App.xaml.cs OnStartup, after config load
using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
var principal = new System.Security.Principal.WindowsPrincipal(identity);
if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
{
    Log.Warning("Running without administrator privileges — Optimize mode and Driver Preference will have limited functionality");
}
```

---

### SEC-011 — Empty catch blocks swallow exceptions without logging
**Severity:** Low
**Files:** Multiple

```csharp
// ProcessWatcher.cs:179
catch { }

// ProcessWatcher.cs:250
catch { }

// App.xaml.cs:237
catch { /* shutting down */ }

// GpuMonitor.cs:91
catch { return false; }

// StartupManager.cs:51
catch { return false; }
```

**Description:** Five locations swallow all exceptions without logging. While most are in contexts where errors are expected (process access, shutdown cleanup), completely silent exception swallowing makes debugging production issues significantly harder. A logged warning at Debug level costs nothing in normal operation but saves hours of debugging.

**Attack scenario:** No direct exploit, but diagnostic blindness. A systemic issue (e.g., antivirus blocking process access) would produce no evidence in logs, making support requests unanswerable.

**Recommended fix:** Add Debug-level logging to each catch block.

```csharp
// ProcessWatcher.cs:179
catch (Exception ex) { Log.Debug("Skipping process: {Error}", ex.Message); }

// App.xaml.cs:237
catch (Exception ex) { Log.Debug(ex, "Failed to unregister hotkey during shutdown"); }
```

---

### SEC-012 — Start-with-Windows registry path includes unquoted exe path risk
**Severity:** Low
**File:** `Core/StartupManager.cs:20`

```csharp
key.SetValue(AppName, $"\"{exePath}\" --minimized");
```

**Description:** The exe path is properly quoted with double quotes, which is correct. However, the path comes from `Process.GetCurrentProcess().MainModule?.FileName`. If the exe is in a directory like `C:\Program Files\X3DCcdOptimizer\`, the quoting is essential — and it's correctly done here. This is a non-issue but worth noting as a defensive measure that's already in place.

**Attack scenario:** None — correctly mitigated. If quotes were missing and the exe were at `C:\Program Files\X3D\CcdOptimizer.exe`, Windows would try `C:\Program.exe` first.

**Recommended fix:** None needed — already correct.

---

### SEC-013 — No bounds check on CcdOverride core indices in CcdMapper
**Severity:** Low
**File:** `Core/CcdMapper.cs:195-203`

```csharp
private static void ApplyOverride(CpuTopology topology, CcdOverrideConfig ovr)
{
    topology.VCacheCores = ovr.VCacheCores!;
    topology.FrequencyCores = ovr.FrequencyCores!;
    topology.VCacheMask = CoresMask(ovr.VCacheCores!);
    topology.FrequencyMask = CoresMask(ovr.FrequencyCores!);
}

private static IntPtr CoresMask(int[] cores)
{
    ulong mask = 0;
    foreach (var core in cores)
        mask |= 1UL << core;  // No bounds check on 'core'
    return new IntPtr((long)mask);
}
```

**Description:** If `CcdOverride` in config.json contains core indices < 0 or > 63, the bit shift `1UL << core` produces undefined behavior. Negative values cause ArgumentOutOfRangeException from the shift. Values > 63 silently wrap around (C# shift operator masks to 6 bits on ulong), producing incorrect masks.

**Attack scenario:** User hand-edits config with `"vcacheCores": [0, 1, 2, 99]`. The mask for core 99 wraps to `1UL << (99 & 63) = 1UL << 35`, silently mapping the wrong cores. SetProcessAffinityMask then pins processes to incorrect cores.

**Recommended fix:** Validate core indices before building mask.

```csharp
private static IntPtr CoresMask(int[] cores)
{
    ulong mask = 0;
    foreach (var core in cores)
    {
        if (core >= 0 && core < 64)
            mask |= 1UL << core;
        else
            Log.Warning("Ignoring out-of-range core index {Core} in CCD override", core);
    }
    return new IntPtr((long)mask);
}
```

---

### SEC-014 — PerformanceMonitor PDH counter paths use only group 0
**Severity:** Low
**File:** `Core/PerformanceMonitor.cs:137-149`

```csharp
string loadPath = $@"\Processor Information(0,{i})\% Processor Utility";
// ...
string freqPath = $@"\Processor Information(0,{i})\Processor Frequency";
```

**Description:** The counter path hardcodes processor group 0. Systems with more than 64 logical processors use multiple processor groups. On a high-core-count Threadripper or server processor, cores in group 1+ would not be monitored. For the target audience (consumer AMD X3D processors with 16-32 threads), this is not an issue today, but it's a latent bug for future hardware.

**Attack scenario:** None for current hardware. Informational for forward compatibility.

**Recommended fix:** None urgently needed. Document the limitation. If future X3D processors exceed 64 threads, the counter path construction would need to enumerate processor groups.

---

### SEC-015 — GpuMonitor WMI query returns stale/cached data
**Severity:** Low
**File:** `Core/GpuMonitor.cs:36-38`

```csharp
using var searcher = new ManagementObjectSearcher(
    "root\\CIMV2",
    $"SELECT UtilizationPercentage, Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
```

**Description:** The WMI query fetches ALL GPU engine entries on every call, not just the target PID. The results are then filtered in C# by string-matching `pid_{pid}_`. On systems with many GPU-accelerated processes, this query returns hundreds of rows, all of which are deserialized and iterated. This is an O(n) operation running every 2 seconds. Performance concern, not a security issue.

**Attack scenario:** System running 50+ GPU-accelerated browser tabs. Each poll cycle deserializes hundreds of WMI objects. CPU overhead increases. On a system already under load from gaming, this could contribute to frame drops.

**Recommended fix:** Informational. A WQL WHERE clause cannot filter by instance name substring. The current approach is the standard pattern for GPU perf counters. Consider caching the ManagementObjectSearcher and querying less frequently if performance profiling shows an issue.

---

### SEC-016 — No sanitization of process names in log output
**Severity:** Info
**File:** Multiple (AffinityManager.cs, ProcessWatcher.cs, RecoveryManager.cs)

**Description:** Process names from `Process.GetProcesses()` are logged directly via Serilog structured logging. Serilog's structured logging properly escapes values in templates (`{Name}`) when writing to file/console, preventing log injection. However, if a process name contained Serilog template syntax (e.g., a process literally named `{Destructuring}`), it could confuse log parsing tools.

**Attack scenario:** Theoretical only. A process would need to have `{` and `}` in its name, which Windows allows but is extremely rare.

**Recommended fix:** None needed. Serilog's structured logging handles this correctly. The `{Name}` in the template is the parameter placeholder, and the actual process name is the value — Serilog doesn't re-interpret values as templates.

---

### SEC-017 — WPF binding displays process names without length limits
**Severity:** Info
**File:** `ViewModels/MainViewModel.cs:223`, `Views/DashboardWindow.xaml`

**Description:** Game names and process names are displayed directly in StatusText, CCD panel role labels, and activity log entries. WPF TextBlock handles special characters safely (no XSS equivalent in WPF). However, an extremely long process name (Windows allows up to 260 characters) could break the UI layout — text would overflow fixed-width columns in the activity log.

**Attack scenario:** A game with a very long executable name (e.g., some indie games with 50+ character names) causes status bar text to wrap or truncate poorly.

**Recommended fix:** Informational. The existing UI uses `TextTrimming="CharacterEllipsis"` in the overlay and fixed-width columns in the log. The dashboard status bar could benefit from the same treatment, but this is a cosmetic issue.

---

### SEC-018 — `dpiAware` manifest value
**Severity:** Info (Not a bug)
**File:** `app.manifest:13`

```xml
<dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
```

**Description:** The value `true/pm` is **correct and intentional** for PerMonitorV2 applications. It means "DPI aware for Windows Vista/7/8, Per-Monitor aware for Windows 8.1+". Combined with the `PerMonitorV2` element on line 14, this is the standard recommended configuration for modern WPF apps. This was flagged in a previous audit as a typo — it is not.

**Recommended fix:** None needed.

---

## Executive Summary

### Findings by Severity

| Severity | Count | IDs |
|----------|-------|-----|
| Critical | 0 | — |
| High | 3 | SEC-001, SEC-002, SEC-003 |
| Medium | 6 | SEC-004, SEC-005, SEC-006, SEC-007, SEC-008, SEC-009, SEC-010 |
| Low | 4 | SEC-011, SEC-012, SEC-013, SEC-014 |
| Info | 4 | SEC-015, SEC-016, SEC-017, SEC-018 |

### Overall Risk Assessment

**Moderate.** The application has no critical vulnerabilities, no network attack surface, no deserialization of untrusted types, and no command injection vectors. The codebase shows strong defensive patterns: process operations wrapped in try/catch, protected process lists, configuration fallback to defaults, and proper handle cleanup via try/finally.

The three High findings are reliability issues rather than exploitability issues:
- **SEC-001** (non-atomic writes) risks data corruption on crash — mitigated by existing fallback-to-defaults logic, but recovery.json is the more important file and lacks this fallback
- **SEC-002** (no single-instance) risks conflicting behavior but requires user error to trigger
- **SEC-003** (recovery name injection) requires attacker write access to %APPDATA%, which already implies full user-level compromise

The Medium findings are thread safety issues (SEC-004, SEC-005, SEC-009), input validation gaps (SEC-006, SEC-007), a missing WMI timeout (SEC-008), and a manifest/elevation mismatch (SEC-010). None are directly exploitable, but they reduce robustness on edge-case systems.

### Prioritized Fix Order

1. **SEC-002** — Single-instance mutex (prevents cascading issues from SEC-001 and SEC-003)
2. **SEC-001** — Atomic file writes (prevents corruption)
3. **SEC-006** — Config validation (prevents crash/CPU-spin from bad config)
4. **SEC-003** — Protected process filter in recovery (defense in depth)
5. **SEC-008** — WMI timeouts (prevents startup hang)
6. **SEC-010** — Elevation check or manifest change (user experience)
7. **SEC-005** — GameDetector thread safety (correctness)
8. **SEC-004** — VCacheDriverManager lazy init (correctness)
9. **SEC-009** — ProcessWatcher dispose race (shutdown stability)
10. **SEC-007** — Registry value validation (defense in depth)

### Not Found (Positive Observations)

- **No hardcoded credentials, tokens, or secrets** anywhere in the codebase
- **No network surface** — no HTTP listeners, no sockets, no IPC
- **No deserialization of polymorphic types** — System.Text.Json is used safely
- **No string interpolation in WMI queries** with user-controlled input — all queries are hardcoded
- **No DLL loading attacks** — only system DLLs via P/Invoke (kernel32, user32, pdh)
- **No path traversal** — all file paths constructed from `Environment.SpecialFolder` constants
- **Process handles properly cleaned up** — every `OpenProcess` has a matching `CloseHandle` in a finally block
- **SetLastError = true** on all P/Invoke declarations that need it
- **NuGet dependencies are minimal and reputable** — Serilog and System.Management only, no known CVEs
- **Nullable reference types enabled** — catches null deref issues at compile time
- **Protected process list** prevents modification of critical system processes during normal operation
