# Research Report: Hardware Performance Counter-Based Automatic CCD Selection

**Date:** 2026-03-30 | **Session:** 63 | **Agent:** Claude Opus 4.6 (1M context)

## Context

X3D CCD Inspector currently assigns games to the V-Cache CCD unconditionally. Some games actually perform better on the Frequency CCD (higher clocks, 32MB L3) than the V-Cache CCD (lower clocks, 96MB L3). This research investigates whether AMD hardware performance counters can automatically classify games as cache-hungry vs frequency-hungry at runtime, using the same approach Chips and Cheese used at Hot Chips 2023.

---

## 1A. AMD Performance Monitoring Events

### Counter Architecture

Zen 4 and Zen 5 provide **6 general-purpose core PMCs** per core (PMC0-PMC5) plus 3 fixed-function counters (INST_RETIRED, APERF, MPERF). Additionally, there are **6 L3-specific PMCs per CCD** (CPMC0-CPMC5) and **4 Data Fabric counters**.

### Core PMC MSR Addresses

| Register | MSR Address |
|---|---|
| PerfEvtSel0 / PerfCtr0 | `0xC0010200` / `0xC0010201` |
| PerfEvtSel1 / PerfCtr1 | `0xC0010202` / `0xC0010203` |
| ... (interleaved pattern) | |
| PerfEvtSel5 / PerfCtr5 | `0xC001020A` / `0xC001020B` |

PerfEvtSel register format: 12-bit EventSelect (bits 7:0 + 35:32), 8-bit UnitMask (bits 15:8), USR/OS/EN flags, CounterMask, etc.

### L3 PMC MSR Addresses (Per-CCD, NOT Per-Core)

L3 counters are shared across all cores in a CCD since L3 is shared.

| Register | MSR Address |
|---|---|
| L3 PerfEvtSel0 / PerfCtr0 | `0xC0010230` / `0xC0010231` |
| L3 PerfEvtSel1 / PerfCtr1 | `0xC0010232` / `0xC0010233` |
| ... | |
| L3 PerfEvtSel5 / PerfCtr5 | `0xC001023A` / `0xC001023B` |

L3 config registers have extra fields: `EnAllCores`, `CoreId`, `ThreadMask`, `EnAllSlices`, `SliceId`. For whole-CCD monitoring: `EnAllCores=1, ThreadMask=3, EnAllSlices=1`.

### Key L3 Cache Events (Zen 4 -- Family 19h)

| Event | Code | UMask | Description |
|---|---|---|---|
| **l3_lookup_state.l3_miss** | **0x04** | **0x01** | L3 cache misses |
| **l3_lookup_state.l3_hit** | **0x04** | **0xFE** | L3 cache hits |
| **l3_lookup_state.all** | **0x04** | **0xFF** | All L3 accesses |
| l3_xi_sampled_latency.all | 0xAC | 0x3F | Sampled L3 miss latency |
| l3_xi_sampled_latency_requests.all | 0xAD | 0x3F | L3 fill request count |

### Key Core Events

| Event | Code | UMask | Description |
|---|---|---|---|
| **ex_ret_instr** (Instructions Retired) | **0xC0** | 0x00 | For MPKI calculation |
| **ls_mab_alloc.all** (MAB allocations) | **0x41** | **0x7F** (Zen4) / **0x0F** (Zen5) | Outstanding cache misses |
| **ls_alloc_mab_count** (MAB occupancy) | **0x5F** | 0x00 | In-flight L1D misses per cycle |
| ls_dmnd_fills_from_sys.all | 0x43 | 0xFF | All demand data fills (shows fill source) |
| ls_dmnd_fills_from_sys.dram_io_near | 0x43 | 0x08 | Fills from DRAM (L3 misses going to memory) |

### Additional L3 Events

| Event | Code | UMask | Description |
|---|---|---|---|
| l3_xi_sampled_latency.dram_near | 0xAC | 0x01 | Latency from same-NUMA DRAM |
| l3_xi_sampled_latency.dram_far | 0xAC | 0x02 | Latency from different-NUMA DRAM |
| l3_xi_sampled_latency.near_cache | 0xAC | 0x04 | Latency from same-NUMA remote CCX cache |
| l3_xi_sampled_latency.far_cache | 0xAC | 0x08 | Latency from different-NUMA remote CCX cache |
| l3_xi_sampled_latency_requests.dram_near | 0xAD | 0x01 | Fill requests from same-NUMA DRAM |
| l3_xi_sampled_latency_requests.all | 0xAD | 0x3F | Fill requests from all sources |

### Cache Fill Source Events (Where L1D misses were serviced from)

| Event | Code | UMask | Description |
|---|---|---|---|
| ls_dmnd_fills_from_sys.local_l2 | 0x43 | 0x01 | Demand fills from local L2 |
| ls_dmnd_fills_from_sys.local_ccx | 0x43 | 0x02 | Demand fills from L3 or same-CCX L2 |
| ls_dmnd_fills_from_sys.near_cache | 0x43 | 0x04 | Fills from same-NUMA remote CCX cache |
| ls_dmnd_fills_from_sys.dram_io_near | 0x43 | 0x08 | Fills from same-NUMA DRAM/IO |
| ls_dmnd_fills_from_sys.far_cache | 0x43 | 0x10 | Fills from different-NUMA remote CCX |
| ls_dmnd_fills_from_sys.dram_io_far | 0x43 | 0x40 | Fills from different-NUMA DRAM/IO |

### L2 Cache Events

| Event | Code | UMask | Description |
|---|---|---|---|
| l2_cache_req_stat.ic_fill_miss | 0x64 | 0x01 | I-cache L2 miss |
| l2_cache_req_stat.ls_rd_blk_c | 0x64 | 0x08 | D-cache L2 miss |
| l2_cache_req_stat.ic_dc_miss_in_l2 | 0x64 | 0x09 | Combined I+D cache L2 misses |

### Critical Zen 4 vs Zen 5 Difference

MAB event `ls_mab_alloc` (0x41) UnitMask encodings changed:
- **Zen 4**: load/store=0x3F, HW prefetch=0x40, all=0x7F
- **Zen 5**: load/store=0x07, HW prefetch=0x08, all=0x0F

L3 events (0x04, 0xAC, 0xAD) appear unchanged between Zen 4 and Zen 5.

### Data Fabric and UMC PMC MSR Addresses

| Register | MSR Address | Count |
|---|---|---|
| DF/NB PerfEvtSel base | `0xC0010240` | 4 counters |
| DF/NB PerfCtr base | `0xC0010241` | 4 counters |
| UMC PerfEvtSel base (Zen 4+) | `0xC0010800` | Per-UMC group |
| UMC PerfCtr base (Zen 4+) | `0xC0010801` | Per-UMC group |

RDPMC indices: NB/DF counters readable via RDPMC indices 6-9, L3 counters via indices 10-15.

### MPKI Calculation Formulas

```
L3 MPKI = (l3_lookup_state.l3_miss * 1000) / ex_ret_instr
L3 Hitrate = l3_lookup_state.l3_hit / l3_lookup_state.all
L3 Miss Latency (ns) = (l3_xi_sampled_latency.all * 10) / l3_xi_sampled_latency_requests.all
MAB Occupancy (avg) = ls_alloc_mab_count / core_cycles
```

### AMD PPR Document References

- Zen 4 (Ryzen 7000): PPR for AMD Family 19h Model 11h, Rev B1 (doc #55901)
- Zen 5: Performance Monitor Counters for AMD Family 1Ah Model 00h-0Fh (doc #58550, Rev 0.01)
- AMD64 APM Vol 2: Section 3.2.5 "Performance-Monitoring Registers"
- AMD OSRR for Family 17h (doc #56255, Rev 3.03) -- historical reference for event format

---

## 1B. Accessing Counters from Windows

### Method 1: ETW HardwareCounterProfile -- BEST OPTION

**How it works:** Windows kernel can attach hardware PMC values to ETW context-switch events. When a thread is switched out, the delta of configured counters is recorded and attributed to the outgoing thread.

**What it supports:**
- Architectural counters including `ProfileTotalCycles` (ID 19), `ProfileCacheMisses` (ID 29), `ProfileBranchMispredictions` (ID 23), `ProfileTotalIssues`
- `ProfileCacheMisses` (KPROFILE_SOURCE ID 29) maps to LLC misses -- this is the key counter
- Custom AMD events can be registered via registry at `HKLM\SYSTEM\CurrentControlSet\Control\WMI\ProfileSource\<Model>\` (Windows 10 1903+)
- Maximum **3-4 simultaneous counters** (silently fails if exceeded)

**Privileges:** Admin required. No kernel driver installation needed.

**C# access:** Via `Microsoft.Diagnostics.Tracing.TraceEvent` NuGet package (MIT license) -- **already a project dependency** (v3.1.30).

**Per-process attribution:** YES, via context-switch thread attribution. Some noise from DPC/ISR contamination.

**Overhead:** Minimal -- piggybacks on existing context switches.

**Caveats:**
- Sampling-based (counter deltas at context switches), not direct polling
- `ProfileCacheMisses` mapping to specific AMD L3 events is undocumented by Microsoft
- L3-specific CPMC registers (the dedicated per-CCD L3 counters) are NOT exposed through ETW -- only core-level LLC events
- Counter configuration through ETW is limited to pre-defined profile sources plus custom registry entries

**Verdict: Most viable path. No driver needed. Already have the NuGet dependency.**

### Method 2: AMD uProf

**How it works:** AMD's profiler installs a WHQL-signed kernel driver (AMDPowerProfiler.sys or similar) that provides full PMC access.

**What it supports:** All core PMCs, L3 CPMCs, Data Fabric counters, IBS. Full AMD event catalog.

**Privileges:** Requires AMD uProf installation (separate download from amd.com).

**C# access:** `AMDuProfCLI` command-line tool can be invoked. No public API/SDK. An abandoned C# wrapper (`AMDuProf.NET` on GitHub) exists but hasn't been updated since 2019.

**Licensing:** EULA prohibits redistributing the driver/libraries. Users must install uProf separately.

**Verdict: Not viable for distribution. Cannot bundle, no stable API.**

### Method 3: PDH (Performance Data Helper)

**What it supports:** OS-level software counters only (CPU %, frequency, disk, network). No hardware PMC exposure on AMD systems.

**Verdict: Cannot read L3 cache counters. Not applicable.**

### Method 4: WMI

**What it supports:** OS-level performance objects (`Win32_PerfFormattedData_*`). No hardware PMC classes exist for AMD cache metrics.

**Verdict: Cannot read L3 cache counters. Not applicable.**

### Method 5: RDPMC/RDMSR Instructions

**How it works:** Direct CPU instructions to read MSRs (Model-Specific Registers) and PMCs.

**Privileges:** Ring 0 only. Causes GP# (General Protection) fault from user mode, even running as admin. Admin privilege is Ring 3 with elevated tokens -- not Ring 0. A kernel driver is mandatory.

**Windows 11 + VBS/HVCI:** Virtualization-Based Security makes loading unsigned or test-signed drivers even harder. HVCI enforces kernel code integrity, blocking drivers not signed through Microsoft's WHQL process.

**Verdict: Requires kernel driver. See Method 6 for driver options.**

### Method 6: Third-Party Drivers

**WinRing0:** Open-source MSR access driver, historically used by OpenHardwareMonitor and LibreHardwareMonitor. However, it is on the **Microsoft Vulnerable Driver Blocklist** since October 2022 (CVE-2020-14979). Windows Defender flags and blocks it on systems with default security settings. **Non-starter for consumer software.**

**PmcReader/MsrUtil (Chips and Cheese):** Apache-2.0 licensed C# project (github.com/ChipsandCheese/MsrUtil) that reads AMD L3 hitrate across all Zen generations. This is architecturally closest to what we'd need. However, it depends on WinRing0 internally -- same blocklist problem.

**LibreHardwareMonitor:** Monitors temperatures, voltages, clocks, and fan speeds -- NOT performance counters. Not relevant.

**Intel PCM `msr.sys`:** Intel-only, requires test-signing mode on Windows.

**Custom WHQL-signed driver:** Would require an EV code signing certificate (~$400-700/yr) + Microsoft Hardware Dev Center Dashboard submission + HLK (Hardware Lab Kit) testing or attestation signing. Significant cost and ongoing maintenance burden. Not appropriate for this project's scope.

**Verdict: No viable open-source driver exists for Windows 11 + Secure Boot.**

### Method 7: WPR (Windows Performance Recorder) / Xperf

**What it supports:** Can capture PMC data via `tracelog -Pmc` to ETL trace files. Supports attaching hardware counters to kernel events.

**Limitations:** Post-processing only via `xperf` or `WPA` -- data goes to trace files, not available in real-time. Would require: start WPR → run game → stop WPR → parse ETL → classify. Too heavyweight and disruptive for automatic use.

**Verdict: Batch analysis only. Not suitable for real-time game classification.**

### Method 8: AMD KFD / Chipset Driver

**What it supports:** KFD (Kernel Fusion Driver) is for GPU compute (ROCm/OpenCL). No CPU PMC interface on Windows. The AMD chipset driver provides power management and platform features, not performance counter access.

**Verdict: Not applicable.**

### Summary Matrix

| Method | L3 Counters | No Driver Needed | C# Support | Per-Process | Redistributable |
|---|---|---|---|---|---|
| **ETW (TraceEvent)** | **Partial (LLC)** | **Yes** | **Yes (NuGet)** | **Yes** | **Yes (MIT)** |
| AMD uProf | Full | No | CLI only | Yes | **No (EULA)** |
| PDH | No | Yes | Yes | N/A | N/A |
| WMI | No | Yes | Yes | N/A | N/A |
| RDPMC/RDMSR | Full | No | No | N/A | N/A |
| WinRing0 | Full | No (blocked) | Yes | System-wide | Blocked by MS |
| WPR/Xperf | Full | Yes | Post-process | Yes | Yes |

---

## 1C. Chips and Cheese Methodology (Hot Chips 2023)

### Reference

"Hot Chips 2023: Characterizing Gaming Workloads on Zen 4" by Chester Lam at Chips and Cheese.

### Counters Used

Chester Lam used multiple measurement passes (6 core PMCs can be programmed simultaneously, requiring multiple runs) across games including The Elder Scrolls Online and Call of Duty: Black Ops Cold War:

- **Pipeline slot utilization:** `de_no_dispatch_per_slot.*`, `de_src_op_disp.all` (new in Zen 4)
- **L1i/L1d/L2/L3 cache** miss and hit counters
- **Branch prediction:** `ex_ret_brn`, `ex_ret_brn_misp`
- **MAB occupancy:** `ls_alloc_mab_count` -- in-flight L1 data cache misses per cycle
- **TLB miss counters:** `bp_l1_tlb_miss_*`, `ls_l1_d_tlb_miss.*`
- **Infinity Fabric latency sampling** (new in Zen 4)

### Key Findings from Hot Chips 2023

- **L1i MPKI**: 17-20 (instruction cache misses per 1000 instructions) -- very high
- **Branch misprediction rate**: 4-5 per 1000 instructions (~97% predictor accuracy)
- **DTLB L1 MPKI**: 6-8
- **MAB occupancy**: Typically low -- Zen 4 rarely had more than **4 outstanding memory requests** during gaming, indicating games are **latency-bound, not bandwidth-bound**
- **Frontend-bound**: Dominant bottleneck (frontend latency >> frontend bandwidth)
- **Bad speculation**: 13-15% of pipeline throughput lost

### V-Cache Benefit Data (from 7950X3D analysis)

| Game/Workload | Standard L3 MPKI | V-Cache L3 MPKI | L3 Hitrate Gain | IPC Gain |
|---|---|---|---|---|
| COD: Black Ops Cold War | 8.66 | ~0.28 | +47% | +19% |
| Cyberpunk 2077 | (high) | (lower) | +13.4% | +13.4% |
| GHPC | (moderate) | (low) | +33% | Significant |
| 7-Zip | (moderate) | (lower) | +29% | +9.75% |
| libx264 | 1.48 | ~0.36 | +17% | +4.9% |
| DCS | 0.35 | 0.28 | Minimal | Minimal |

### Zen 5 Gaming L3 Hitrates (non-X3D, follow-up article)

- Palworld: 64.5% L3 hitrate
- COD Cold War: 67.6%
- Cyberpunk 2077: 55.43% (lowest)
- Zen 5 is primarily **frontend-latency-bound** in games

### Decision Heuristics

| L3 MPKI (measured on 32MB CCD) | V-Cache Benefit | Recommended CCD |
|---|---|---|
| **> 5** | Strong (IPC gain > 9%, overcomes clock deficit) | **V-Cache** |
| **1 - 5** | Moderate (game-dependent) | **V-Cache (default)** |
| **< 0.5** | Minimal (frequency advantage wins) | **Frequency** |

**Key insight from Chester Lam:** V-Cache makes Zen 4 "less backend-bound" by improving cache service. Frontend bottlenecks (dominant in games) are unaffected by L3 expansion. The IPC gain from V-Cache must exceed the ~7-8% clock speed deficit to be worthwhile.

### MAB Occupancy Correlation

- Low MAB occupancy (~1-4 entries) = game is latency-bound, moderate cache sensitivity
- High MAB occupancy (>4 entries) = many outstanding misses, strong V-Cache benefit
- Very low MAB occupancy (<1) = game fits in L2/L3 already, frequency wins

---

## 1D. Existing Tools That Read AMD PMCs

| Tool | Access Method | L3 PMCs? | Reusable for Our App? |
|---|---|---|---|
| **AMD uProf** | Proprietary WHQL-signed kernel driver | Full | No (EULA prohibits redistribution) |
| **HWiNFO** | Own WHQL-signed driver | No (thermal/power/clock only) | No |
| **Ryzen Master** | AMD proprietary driver | No (OC controls only) | No |
| **Process Lasso** | Standard Win32 APIs | No | N/A |
| **PmcReader/MsrUtil** | WinRing0 (driver-blocklisted) | Full (all Zen gens) | License OK (Apache-2.0), but driver blocked |
| **likwid** (Linux) | /dev/msr or perf_event | Full | Event codes are transferable to Windows |
| **perf** (Linux) | perf_event_open syscall | Full | Event codes are transferable to Windows |
| **Intel VTune** | Intel-specific driver | Intel only | No |

**Most relevant:** Chips and Cheese's **PmcReader** (github.com/ChipsandCheese/MsrUtil) -- C#, Apache-2.0, reads AMD L3 hitrate on all Zen generations. Architecturally closest to what we'd build. But depends on WinRing0, which is on Microsoft's Vulnerable Driver Blocklist (CVE-2020-14979, blocked since October 2022).

**Most useful for event codes:** **likwid** (github.com/RRZE-HPC/likwid) -- its wiki documents AMD Zen 4 performance groups with exact event codes and unit masks. These hardware event codes are the same regardless of OS.

---

## 1E. Feasibility Assessment

### Q1: Can we read L3 cache miss counters from C# as admin, WITHOUT a separate driver?

**Partially.** ETW's `ProfileCacheMisses` (KPROFILE_SOURCE ID 29) provides LLC miss data attributed to processes via context-switch sampling. This is accessible from C# via the TraceEvent NuGet package (already a dependency). However:
- It measures core-level LLC events, not the dedicated per-CCD L3 CPMC counters
- The exact AMD event mapping of `ProfileCacheMisses` is undocumented by Microsoft
- It's sampling-based (counter deltas at context switches), not direct counter reads
- Limited to 3-4 simultaneous counters

For the dedicated L3 CPMC counters (event 0x04 at MSR 0xC0010230+), a kernel driver is required. No viable driver exists for Windows 11 + Secure Boot.

### Q2: If a driver is required, is there an open-source option?

**No viable option exists.** WinRing0 is blocklisted by Microsoft. A custom WHQL-signed driver would require an EV code signing certificate + Microsoft Hardware Dev Center submission + HLK testing -- significant cost and maintenance burden inappropriate for this project.

### Q3: Minimum overhead approach for L3 MPKI per-process?

**ETW context-switch PMC sampling** is the lowest-overhead approach. It piggybacks on existing OS context switches, adds no additional interrupts, and provides per-thread attribution. Reading 3-4 counters per context switch adds negligible overhead (~20-50 cycles per RDPMC instruction, done by the kernel).

### Q4: Per-process or system-wide?

- **Core PMCs via ETW:** Per-process attribution via context-switch deltas (with DPC/ISR noise)
- **L3 CPMCs (dedicated):** Per-CCD only, not per-process. But since our tool controls game affinity to a specific CCD, CCD-level L3 data effectively represents game L3 behavior during active gaming
- **If using ETW:** Per-process, but measures core-level LLC events, not CCD-level L3 events

### Q5: Can we sample every 5 seconds without performance impact?

**Yes.** ETW PMC sampling has no configurable interval -- it fires at every context switch automatically. The overhead is a few dozen cycles per context switch to read counter values. Completely negligible. For aggregation purposes, we'd accumulate ETW events and compute statistics over 5-second windows.

---

## Feasibility Verdict

### Can we do this? YES, with caveats.

**Recommended approach: ETW ProfileCacheMisses via TraceEvent (already a dependency)**

This gives us LLC miss counts per-process without any driver installation. The data is noisier and less specific than direct L3 CPMC reads, but it's:
- Zero additional dependencies (TraceEvent v3.1.30 already used for process detection via ETW)
- No driver installation needed (admin privileges sufficient)
- Per-process attributed
- Negligible overhead
- Works on Windows 11 with Secure Boot/HVCI enabled

**What we'd measure:** LLC miss rate per game process during the first N seconds of gameplay, then classify:
- High LLC MPKI (>5): Route to V-Cache CCD
- Low LLC MPKI (<0.5): Route to Frequency CCD
- Middle range: Default to V-Cache (safe choice)

**Key tradeoffs:**
1. `ProfileCacheMisses` may not map perfectly to L3 misses on AMD -- it's an architectural counter whose exact AMD event mapping is undocumented by Microsoft
2. Context-switch sampling means we only get data when the OS schedules the game thread out -- fine for long-running games, less reliable for very short measurements
3. We'd need a "learning period" where the game runs first to measure its cache behavior, then stores the classification for future launches -- introduces complexity and a brief suboptimal period on first run

### Alternative: Community Game Database (No PMC needed)

A simpler and more reliable approach:
- Maintain a curated database of game-to-CCD recommendations
- Seed with Chips and Cheese benchmark data and community testing
- Users can override per-game
- No runtime measurement needed, instant CCD assignment on first launch
- Can be combined with ETW approach: database provides instant assignment, ETW validates/refines over time

### Recommended Path Forward

**Phase 1 (Immediate value):** Game characteristics database with known V-Cache vs Frequency recommendations. This is the `GameProfile` system that already exists in the codebase (model + per-game config) -- just needs a default database and a `PreferredCcd` field.

**Phase 2 (If Phase 1 proves insufficient):** ETW-based LLC miss profiling during a "learning run" for unknown games. Measure LLC MPKI over first 60 seconds of gameplay, store classification in GameDatabase (LiteDB, already integrated), use classification for all future launches of that game.

**Phase 3 (If ETW data proves unreliable):** Investigate Windows custom PMC profile source registration (registry-based, Windows 10 1903+) to map specific AMD L3 events to ETW profile sources at `HKLM\SYSTEM\CurrentControlSet\Control\WMI\ProfileSource\<Model>\`. This is the most advanced option and least documented.

---

## Sources

### Chips and Cheese Articles
- [Hot Chips 2023: Characterizing Gaming Workloads on Zen 4](https://chipsandcheese.com/p/hot-chips-2023-characterizing-gaming-workloads-on-zen-4)
- [AMD's 7950X3D: Zen 4 Gets VCache](https://chipsandcheese.com/p/amds-7950x3d-zen-4-gets-vcache)
- [Running Gaming Workloads through AMD's Zen 5](https://chipsandcheese.com/p/running-gaming-workloads-through)

### Windows PMC Access
- [Collecting Hardware Performance Counters with ETW (Adam Sitnik)](https://adamsitnik.com/Hardware-Counters-ETW/)
- [CPU Performance Counters on Windows (Bruce Dawson)](https://randomascii.wordpress.com/2016/11/27/cpu-performance-counters-on-windows/)
- [Recording Hardware Performance (PMU) Events (Microsoft Learn)](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/recording-pmu-events)
- [Recording PMU Events with Complete Examples (Microsoft DevBlogs)](https://devblogs.microsoft.com/performance-diagnostics/recording-hardware-performance-pmu-events-with-complete-examples/)

### AMD Documentation
- AMD PPR for Family 19h Model 11h, Rev B1 (doc #55901)
- [Performance Monitor Counters for AMD Family 1Ah (doc #58550)](https://www.amd.com/content/dam/amd/en/documents/epyc-technical-docs/programmer-references/58550-0.01.pdf)
- [AMD OSRR for Family 17h (doc #56255)](https://www.amd.com/content/dam/amd/en/documents/processor-tech-docs/programmer-references/56255_OSRR.pdf)
- [AMD uProf User Guide: PMC Documentation](https://docs.amd.com/r/en-US/57368-uProf-user-guide/4.2.-Performance-Monitoring-Counters-PMC)

### Linux Kernel PMC Patches
- [Add Zen 4 events and metrics (Sandipan Das)](https://patchew.org/linux/20221214082652.419965-1-sandipan.das@amd.com/20221214082652.419965-4-sandipan.das@amd.com/)
- [Add Zen 4 core events](https://lore.kernel.org/lkml/20221214082652.419965-2-sandipan.das@amd.com/)
- [Zen 5 MAB allocation events fix](https://www.spinics.net/lists/kernel/msg6070044.html)
- [Add Zen 5 events and metrics](https://patchew.org/linux/cover.1710133771.git.sandipan.das@amd.com/)

### Tools and Libraries
- [PmcReader/MsrUtil (Chips and Cheese)](https://github.com/ChipsandCheese/MsrUtil) -- Apache-2.0, C#, reads AMD L3 hitrate
- [MsrUtil (clamchowder fork)](https://github.com/clamchowder/MsrUtil)
- [LIKWID Zen4 Wiki](https://github.com/RRZE-HPC/likwid/wiki/Zen4) -- best documentation of AMD event codes
- [Microsoft.Diagnostics.Tracing.TraceEvent NuGet](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent)
- [BenchmarkDotNet HardwareCounter Enum](https://benchmarkdotnet.org/api/BenchmarkDotNet.Diagnosers.HardwareCounter.html)
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)

### Security / Driver Signing
- [Microsoft Vulnerable Driver Blocklist](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules)
- [WinRing0 Vulnerable Driver Alert](https://support.microsoft.com/en-us/windows/microsoft-defender-antivirus-alert-vulnerabledriver-winnt-winring0-eb057830-d77b-41a2-9a34-015a5d203c42)

### Other
- [Playing with PMCs on Zen 2 Machines](https://reflexive.space/zen2-pmc/)
- [illumos: Zen 4 CPU Performance Counters](https://www.illumos.org/issues/15522)
- [AMD uProf EULA](https://www.amd.com/en/developer/uprof/uprof-eula.html)
