# X3D CCD Optimizer — Improvement Roadmap

Assessment based on full codebase audit (March 2026). Ordered by impact-to-effort ratio.

---

## 1. Test Coverage (High Impact, Medium Effort)

### Problem
5 tests in 1 file covering only `ProcessorTier` enum values. Zero coverage of core logic: topology detection, affinity management, game detection, library scanning, database operations. For a tool that calls `SetProcessAffinityMask` with admin rights, this is the most significant quality gap.

### What to Test

**Tier 1 — Unit tests (pure logic, no OS dependencies):**

| Component | What to test | Why it matters |
|-----------|-------------|----------------|
| `GameDetector.CheckGame()` | Manual > DB > launcher > null priority chain | Wrong priority = wrong game detected |
| `GameDetector.IsExcluded()` | Case-insensitive matching, .exe suffix handling | False positive = game not detected |
| `ScannedGame` model | LiteDB round-trip serialization | Data corruption = lost scan results |
| `GameLibraryScanner.ShouldSkipExe()` | All prefix and suffix patterns | Missing pattern = duplicate entries |
| `GameLibraryScanner.SelectBestExe()` | Name matching, root preference, size tiebreaker | Wrong pick = game not detected |
| `VdfParser.Parse()` | Nested blocks, escaped strings, comments | Parse failure = no Steam games found |
| `ProcessorTier.IsSupported()` | All 4 enum values | Wrong gate = app runs on unsupported CPU |
| `AppConfig.Validate()` | Boundary values, corrupted JSON fallback | Bad config = crash on startup |
| `GameDatabase.Deduplicate()` | Same name/source, different AppIds, empty DB | Missing dedup = duplicate entries |
| `GameDatabase.MigrateFromJsonCache()` | Valid cache, corrupt cache, empty cache, already migrated | Migration failure = lost game data |

**Tier 2 — Integration tests (need filesystem, not OS-level):**

| Component | What to test |
|-----------|-------------|
| `GameLibraryScanner.ScanSteam()` | Mock steamapps directory with ACF files, verify one exe per game |
| `GameLibraryScanner.ScanEpic()` | Mock manifests directory with .item JSON files |
| `GameDatabase` full lifecycle | Create, upsert, deduplicate, ToDictionary, dispose |
| `AppConfig.Load()` / `Save()` | Round-trip, version migration (v1 -> v3), atomic write |
| `ArtworkDownloader.GetLocalPath()` | Path construction, directory creation |

**Tier 3 — Behavioral tests (harder to isolate):**

| Component | What to test |
|-----------|-------------|
| `ProcessWatcher` idle/active switching | Timer interval changes on game detect/exit |
| `AffinityManager` mode switching | Monitor -> Optimize mid-game, Optimize -> Monitor mid-game |
| `AffinityManager.Deduplicate()` | Dead PID pruning in `_originalMasks` |

### Implementation Approach

Create test fixtures with mock data — don't hit real Steam/Epic/GOG installations:

```
tests/X3DCcdOptimizer.Tests/
├── ProcessorTierTests.cs          (existing — expand)
├── GameDetectorTests.cs           (new)
├── VdfParserTests.cs              (new)
├── GameLibraryScannerTests.cs     (new)
├── GameDatabaseTests.cs           (new)
├── AppConfigTests.cs              (new)
├── SelectBestExeTests.cs          (new)
├── TestData/
│   ├── appmanifest_570.acf        (mock Steam manifest)
│   ├── libraryfolders.vdf         (mock Steam library config)
│   ├── epic_manifest.item         (mock Epic manifest)
│   ├── known_games_test.json      (minimal test DB)
│   └── config_v1.json             (migration test fixture)
```

**Target:** 50+ tests covering all Tier 1 items. This is achievable in a single focused session and would catch the majority of regression risks.

### What NOT to Test
- WPF ViewModels (property change notifications) — low risk, high effort to mock dispatchers
- P/Invoke wrappers — testing Windows API behavior is the OS vendor's job
- Overlay pixel shift — visual behavior, not logic

---

## 2. ETW Process Notifications (High Impact, High Effort)

### Problem
`Process.GetProcesses()` is called from 9 locations, polling every 2-4 seconds. Each call enumerates every process on the system, allocates Process objects, and discards most of them. This is the single most expensive operation in the hot path.

### Solution
Replace polling with Event Tracing for Windows (ETW) process start/stop notifications. The `Microsoft-Windows-Kernel-Process` provider emits events when processes start and stop, allowing event-driven detection with zero polling overhead.

### Architecture

```
Current:
  Timer (2-4s) → Process.GetProcesses() → foreach → CheckGame() → detect/skip

Proposed:
  ETW subscription → OnProcessStart(pid, name) → CheckGame() → detect
  ETW subscription → OnProcessStop(pid) → if tracked game → handle exit
```

### Implementation

Use `Microsoft.Diagnostics.Tracing.TraceEvent` NuGet package (Microsoft's official ETW library):

```csharp
public class ProcessEventWatcher : IDisposable
{
    private TraceEventSession _session;

    public event Action<int, string>? ProcessStarted;
    public event Action<int>? ProcessStopped;

    public void Start()
    {
        _session = new TraceEventSession("X3DCcdOptimizer-ProcessWatch");
        _session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);

        _session.Source.Kernel.ProcessStart += data =>
            ProcessStarted?.Invoke(data.ProcessID, data.ProcessName);

        _session.Source.Kernel.ProcessStop += data =>
            ProcessStopped?.Invoke(data.ProcessID);

        Task.Run(() => _session.Source.Process()); // blocks on background thread
    }
}
```

### Benefits
- Zero CPU when idle (no polling at all)
- Instant game detection (event fires within milliseconds of process start)
- No more `Process.GetProcesses()` in the hot path
- Eliminates the idle/active polling distinction entirely

### Risks
- ETW requires admin (already have it)
- `TraceEventSession` is heavyweight (~2MB NuGet package)
- Kernel ETW sessions are system-wide singletons — name collision possible
- Need fallback to polling if ETW session creation fails (e.g., another tool using the session name)

### Migration Path
1. Add `ProcessEventWatcher` alongside existing `ProcessWatcher`
2. Wire game detection to ETW events
3. Keep `ProcessWatcher` as fallback (reduce polling to 10-15s for safety net)
4. AffinityManager re-migration can stay timer-based (5s is fine for catching child processes)

### Estimated Impact
- Idle CPU: 0.3-0.5% → near 0%
- Game detection latency: 2-4 seconds → <100ms
- Background process migration: same (still timer-based)

---

## 3. Performance Benchmarks (High Impact, Low Effort)

### Problem
The README claims the tool helps but provides no evidence. The target audience (enthusiast PC builders) values data over marketing. A single benchmark page would be the strongest adoption driver.

### What to Measure

**Test matrix:**

| Variable | Values |
|----------|--------|
| CPU | 7950X3D (primary), 9950X3D (if available) |
| Strategy | No optimization, Driver Preference, Affinity Pinning |
| Games | 3-5 cache-sensitive titles |
| Metric | Average FPS, 1% low FPS, frame time variance |
| Runs | 5 per configuration (statistical significance) |

**Suggested games (known to be cache-sensitive):**
- Cyberpunk 2077 (open world, large working set)
- Counter-Strike 2 (competitive, frame time matters)
- Baldur's Gate 3 (CPU-heavy, many threads)
- Factorio (single-thread bottleneck, cache-sensitive)
- Starfield (large world streaming)

**Methodology:**
1. Fresh boot, same background apps (Discord, Steam, browser with 5 tabs)
2. Same in-game benchmark scene/sequence
3. Log which CCD the game lands on without optimization (run 10 times, count CCD0 vs CCD1 placement)
4. Compare average FPS and 1% lows across all three conditions

### Output Format
A wiki page: `Benchmark-Results.md` with:
- Test hardware specifications
- Methodology description
- Results table per game
- Conclusion: "In X out of Y games, optimization improved 1% lows by Z%"
- Honest assessment: "Games that already land on V-Cache see no improvement"

### Why This Matters
This is the single most requested piece of information from the target audience. Reddit posts about X3D optimization tools always get "but does it actually help?" as the top comment. Having data answers that question definitively.

---

## 4. Code Signing (Medium Impact, Low Effort)

### Problem
Windows SmartScreen flags unsigned executables. For a tool requesting admin elevation, an unsigned binary triggers two scary-looking prompts (SmartScreen + UAC). This is the #1 barrier to casual adoption.

### Solution
The README already references SignPath.io as planned. Implementation:

1. Apply for a free open-source code signing certificate from [SignPath Foundation](https://signpath.org)
2. Add a signing step to the `release.yml` GitHub Actions workflow
3. Sign the exe and the Inno Setup installer

### SignPath Integration (GitHub Actions)

```yaml
- name: Sign with SignPath
  uses: signpath/github-action-submit-signing-request@v1
  with:
    api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
    organization-id: ${{ secrets.SIGNPATH_ORG_ID }}
    project-slug: x3d-ccd-optimizer
    signing-policy-slug: release-signing
    artifact-configuration-slug: exe
    github-artifact-id: ${{ steps.upload.outputs.artifact-id }}
```

### Timeline
- Application: 1-2 weeks for approval (open-source projects are prioritized)
- Integration: 1 session to add the workflow step
- Result: SmartScreen prompt goes away for most users after a few dozen downloads build reputation

---

## 5. Auto-Update Check (Medium Impact, Low Effort)

### Problem
No mechanism for users to know a new version exists. The app could run outdated for months.

### Solution
Lightweight, opt-in, privacy-respecting version check:

```csharp
public static class UpdateChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<string?> CheckForUpdate(string currentVersion)
    {
        // GitHub Releases API — public, no auth needed, no data sent
        var url = "https://api.github.com/repos/LordBlacksun/x3d-ccd-optimizer/releases/latest";
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("X3DCcdOptimizer/" + currentVersion);

        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var latest = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');

        if (latest != null && Version.TryParse(latest, out var latestVer) &&
            Version.TryParse(currentVersion, out var currentVer) && latestVer > currentVer)
            return latest;

        return null;
    }
}
```

### UX
- Setting: "Check for updates on startup" — **OFF by default** (consistent with zero-network-by-default philosophy)
- When enabled: single GET to GitHub API on startup, no more than once per 24 hours
- If update found: subtle text in footer: "v1.1.0 available" with clickable link to releases page
- No auto-download, no nag dialogs, no background polling

### Privacy
- Only a single HTTPS GET to `api.github.com` — GitHub sees the request IP, nothing else
- User-Agent header includes version (standard practice, helps GitHub rate limiting)
- No telemetry piggyback

---

## 6. Process Lasso Feature Parity Check (Low Impact, Research Only)

### Context
Process Lasso is the incumbent for X3D users. Understanding what it does that this tool doesn't helps identify adoption blockers.

### Features Process Lasso Has That This Tool Doesn't

| Feature | Process Lasso | X3D CCD Optimizer | Gap Impact |
|---------|--------------|-------------------|------------|
| Per-game profiles | Yes (saves settings per-exe) | No (global rules only) | Medium — power users want per-game tuning |
| CPU priority management | Yes (ProBalance) | No | Low — outside scope |
| Per-process persistent rules | Yes (survives reboot, per-exe) | Partial (manual list, but no per-game priority/affinity override) | Medium |
| I/O priority | Yes | No | Low — outside scope |
| Performance Mode / Game Mode | Yes | Partial (Optimize mode is similar) | Low |
| Watchdog / keep-alive | Yes | No | Low |
| GUI process manager | Yes (full process list) | Partial (process router shows managed processes only) | Low |
| Tray icon with per-process control | Yes | No (tray has mode/overlay toggles only) | Low |

### What's Worth Adding
**Per-game profiles** is the biggest gap. Users want "Cyberpunk always uses Affinity Pinning, CS2 always uses Driver Preference." Currently the strategy is global.

Implementation sketch:
- `GameProfile` model: ProcessName, Strategy, CustomAffinityMask (optional)
- UI: right-click a game in Game Library → "Set Profile"
- Config: `gameProfiles` list in AppConfig
- AffinityManager checks for game-specific profile before applying global strategy

---

## 7. Remaining Architecture Improvements

### 7A. Reduce Process.GetProcesses() Call Sites

Even without ETW (item 2), some call sites can be eliminated:

| Call site | Current purpose | Fix |
|-----------|----------------|-----|
| `AffinityManager.MigrateBackground()` | Initial background migration | Keep — runs once per game detection |
| `AffinityManager.MigrateNewProcesses()` | Re-migration timer | Keep — 5s is acceptable |
| `ProcessWatcher.Poll()` (game scan) | Detect new games | Keep — this is the core loop |
| `ProcessWatcher.Poll()` (upgrade scan) | Find known game to replace auto-detected | Could optimize: only scan if auto-detected game is active |
| `AffinityManager.RestoreAll()` | Get process names for logging | Could use cached names from `_originalMasks` + stored name |
| `ProcessPickerWindow` | UI process picker | Keep — user-triggered, not on timer |
| `ProcessRouterViewModel` | Dashboard display | Could reduce frequency or use cached data from ProcessWatcher |

**Quick win:** Store process names alongside PIDs in `_originalMasks` to avoid `Process.GetProcessById()` calls during restore. Change from `Dictionary<int, IntPtr>` to `Dictionary<int, (IntPtr Mask, string Name)>`.

### 7B. WMI GPU Query Optimization

`GpuMonitor` uses a cached `ManagementObjectSearcher` but still executes a WMI query on each poll. WMI overhead is ~5-15ms per query. Options:

- **Short term:** Increase skip ratio when idle (currently skips every other query — could skip 3 of 4)
- **Long term:** Replace WMI with NVAPI/ADL direct GPU queries (much faster, but vendor-specific)
- **Alternative:** Use `D3DKMTQueryStatistics` (undocumented but stable kernel API used by Task Manager) for GPU utilization without WMI

### 7C. Known Games Database Expansion

65 built-in games is a good start but misses many popular titles. Sources for expansion:
- PCGamingWiki has a comprehensive database of game executables
- SteamDB lists popular games by concurrent players
- Community contributions via GitHub issues/PRs (already encouraged in CONTRIBUTING.md)

Low-hanging fruit to add: Path of Exile 2, Marvel Rivals, The Finals, Wuthering Waves, Zenless Zone Zero, Black Myth: Wukong, Indiana Jones, Dragon Age: The Veilguard, S.T.A.L.K.E.R. 2.

---

## Priority Summary

| # | Item | Impact | Effort | Recommended Order |
|---|------|--------|--------|-------------------|
| 1 | Test coverage (Tier 1 unit tests) | High | Medium | Do first — catches regressions for everything else |
| 3 | Performance benchmarks | High | Low | Do early — strongest adoption argument |
| 4 | Code signing | Medium | Low | Do early — removes SmartScreen barrier |
| 5 | Auto-update check | Medium | Low | Do after signing — signed updates are more trustworthy |
| 2 | ETW process notifications | High | High | Do when polling overhead becomes a measured problem |
| 6 | Per-game profiles | Medium | Medium | Do when community requests it |
| 7 | Architecture cleanup | Low-Med | Low-Med | Incremental, opportunistic |
