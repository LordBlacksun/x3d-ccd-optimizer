# CPPC2 Preferred Core Behavior on AMD Dual-CCD X3D Processors

**Date:** 2026-03-30 | **Session:** 64 | **Agent:** Claude Opus 4.6 (1M context)

Research into how CPPC2 (Collaborative Processor Performance Control v2) preferred core settings affect OS scheduler behavior on AMD Ryzen dual-CCD X3D processors (7950X3D, 7900X3D, 9950X3D, 9900X3D). All four processors use V-Cache on one CCD only — the 9950X3D/9900X3D do NOT have dual V-Cache despite early rumors; AMD confirmed this at CES 2025.

---

## Table of Contents

1. [How CPPC2 Communicates Core Preference to Windows](#1-how-cppc2-communicates-core-preference-to-windows)
2. [How the Windows Scheduler Responds](#2-how-the-windows-scheduler-responds)
3. [How the amd3dvcache Driver Interacts with CPPC](#3-how-the-amd3dvcache-driver-interacts-with-cppc)
4. [Per-Setting Analysis: Real-World Scheduling Outcomes](#4-per-setting-analysis-real-world-scheduling-outcomes)
5. [Implications for Our Tool](#5-implications-for-our-tool)
6. [Summary Table](#6-summary-table)
7. [Open Questions](#7-open-questions)
8. [Sources](#8-sources)

---

## 1. How CPPC2 Communicates Core Preference to Windows

### 1.1 The ACPI _CPC Object

Each logical processor exposes an ACPI `_CPC` (Continuous Performance Control) package containing performance-related registers. CPPC v2 defines 21 entries; CPPC v3 adds 2 more (lowest/nominal frequency in MHz).

The key registers for core preference:

| Index | Register | R/W | Description |
|-------|----------|-----|-------------|
| 0 | Highest Performance | RO | Maximum performance this core can reach under ideal conditions |
| 1 | Nominal Performance | RO | Maximum sustained performance level |
| 4 | Guaranteed Performance | RO | Performance guaranteed under worst-case conditions |
| 5 | Desired Performance | RW | OS-requested performance target (0 = autonomous) |
| 17 | Energy Performance Preference | RW | Power/performance tradeoff hint (0x00=max perf, 0xFF=max efficiency) |

All performance values are abstract, unit-less numbers on a 0-255 scale.

**Source:** ACPI 6.5 Specification Section 8.4.7.1; Linux kernel `include/acpi/cppc_acpi.h` ([source](https://github.com/torvalds/linux/blob/master/include/acpi/cppc_acpi.h))

### 1.2 AMD's MSR-Based CPPC Implementation

AMD Zen 2+ processors with the `X86_FEATURE_CPPC` CPUID flag implement CPPC via direct MSR access rather than ACPI shared memory:

| MSR Address | Name | Description |
|-------------|------|-------------|
| 0xC00102B0 | CPPC_CAP1 | Performance capabilities (read-only) |
| 0xC00102B1 | CPPC_ENABLE | CPPC enable register |
| 0xC00102B3 | CPPC_REQ | Performance request register (read-write) |

**CPPC_CAP1 (0xC00102B0) bit layout:**

| Bits | Field | Role |
|------|-------|------|
| 31:24 | **Highest Performance** | **THE core ranking signal** |
| 23:16 | Nominal Performance | Max sustained performance |
| 15:8 | Lowest Non-Linear Performance | Efficiency knee point |
| 7:0 | Lowest Performance | Absolute minimum |

**Source:** Linux kernel `arch/x86/include/asm/msr-index.h` ([source](https://github.com/torvalds/linux/blob/master/arch/x86/include/asm/msr-index.h))

### 1.3 The Core Ranking Signal: Highest Performance

**[CONFIRMED — Linux kernel source, AMD documentation]**

CPPC2 ranks cores via the `Highest Performance` field. Each core's MSR_CPPC_CAP1 register contains a potentially different value. **Higher values = higher priority for the OS scheduler.**

The Linux kernel detects preferred core support by comparing `highest_perf` across cores:

```c
// From arch/x86/kernel/acpi/cppc.c
for_each_online_cpu(cpu) {
    ret = amd_get_highest_perf(cpu, &tmp);
    if (!count || (count == 1 && tmp != highest_perf[0]))
        highest_perf[count++] = tmp;
    if (count == 2) break;  // Two different values = prefcore detected
}
```

If all cores report the same `highest_perf`, preferred core is NOT detected. If at least two different values exist, preferred core IS active.

**Key constants from the kernel:**

| Constant | Value | Meaning |
|----------|-------|---------|
| CPPC_HIGHEST_PERF_PERFORMANCE | 196 | Boost numerator for high-frequency cores (Zen 4 X3D) |
| CPPC_HIGHEST_PERF_PREFCORE | 166 | Default prefcore boost numerator |
| 255 (U8_MAX) | 255 | All cores equal — no preferred core differentiation |

The firmware encodes both the performance ceiling and the relative core quality into this single 8-bit field. The kernel separates these into `prefcore_ranking` (raw value, used for ordering) and `perf.highest_perf` (normalized, used for frequency calculations).

**Source:** Linux kernel `arch/x86/kernel/acpi/cppc.c` ([source](https://github.com/torvalds/linux/blob/master/arch/x86/kernel/acpi/cppc.c)), `drivers/cpufreq/amd-pstate.h`

### 1.4 Rankings Are Dynamic, Not Static

The firmware can update `highest_perf` at runtime based on:
- Thermal conditions
- Power budget constraints
- CCD mode switch (the amd3dvcache driver mechanism)
- Silicon aging

When rankings change, the firmware fires a System Control Interrupt (SCI) so the OS re-reads the new values.

**Source:** Linux kernel amd-pstate driver ([docs](https://docs.kernel.org/admin-guide/pm/amd-pstate.html))

### 1.5 How Dual-CCD X3D Processors Are Classified

The Linux kernel recognizes dual-CCD X3D processors as heterogeneous via `X86_FEATURE_AMD_HTR_CORES`:

| Core Type | Kernel Classification | Boost Numerator |
|-----------|----------------------|-----------------|
| Frequency CCD cores | `TOPO_CPU_TYPE_PERFORMANCE` | 196 (hardcoded) |
| V-Cache CCD cores | `TOPO_CPU_TYPE_EFFICIENCY` | Actual CAP1 value (varies per core) |

**Important nuance:** The "efficiency" label is misleading — V-Cache cores are not less capable for cache-sensitive workloads, they just boost to lower frequencies. The classification exists because the kernel needs to distinguish the two core types.

**Source:** Linux kernel `arch/x86/kernel/acpi/cppc.c` heterogeneous core handling

### 1.6 BIOS Settings and What They Change

Two related but distinct BIOS settings exist under **Advanced > AMD CBS > SMU Common Options**:

#### Setting 1: "CPPC" (the base feature)
- **Enabled** (default): Activates CPPC v2 interface. ACPI `_CPC` objects are populated with per-core performance data. Windows performance state type becomes "ACPI Collaborative Processor Performance Control."
- **Disabled**: Falls back to legacy ACPI P-States. No CPPC data exposed to OS.

#### Setting 2: "CPPC Preferred Cores"
- **Enabled** (default): Each core reports a unique `highest_perf` value based on silicon quality binning. Creates a ranking hierarchy visible to the OS. Windows shows varying "Maximum Performance Percentage" per core in Event Viewer (Kernel-Processor-Power, Event 55).
- **Disabled**: All cores report the same `highest_perf` value. Windows shows identical "Maximum Performance Percentage" of 100% for all cores. OS treats all cores as equal.

#### Setting 3: "CPPC Dynamic Preferred Cores" (X3D-specific)

| Setting | Behavior | amd3dvcache Driver Loaded? |
|---------|----------|---------------------------|
| **Auto** | Firmware delegates to OS/driver for CCD preference | **Yes** |
| **Driver** | Explicitly enables OS/driver control | **Yes** |
| **Cache** | Firmware hardcodes V-Cache CCD cores as preferred | **No** — device removed from Device Manager |
| **Frequency** | Firmware hardcodes frequency CCD cores as preferred | **No** — device removed from Device Manager |

**[CONFIRMED]** Setting "Cache" or "Frequency" removes the `AMDI0101` ACPI device entirely. The amd3dvcache driver cannot load and GameMode switching is disabled.

**Sources:** Overclock.net CPPC threads ([link](https://www.overclock.net/threads/cppc-and-cppc-preferred-cores.1792460/)); Tom's Hardware AMD clarification ([link](https://www.tomshardware.com/news/amd-no-windows-scheduler-isnt-selecting-wrong-ryzen-3000-cores-to-boost)); community Event Viewer observations

---

## 2. How the Windows Scheduler Responds

### 2.1 Soft Bias, Not Hard Pin

**[CONFIRMED — Windows kernel behavior, multiple sources]**

Windows uses **soft affinity** for CPPC preferred cores. The scheduler hierarchy for placing a thread is:

1. Check if the thread's **previous processor** is available (cache locality)
2. Check if the **ideal processor** (set via CPPC ranking) is available
3. Find any available processor

CPPC preferred cores are a scheduling preference, not a constraint. High-priority threads CAN and DO land on non-preferred cores when preferred cores are busy.

**Source:** Windows kernel thread scheduling analysis ([link](https://medium.com/@amitmoshel70/mysteries-of-the-windows-kernel-pt-2-threads-scheduling-cpus-30125fbb46a3))

### 2.2 Windows Treats Ryzen as Heterogeneous

**[CONFIRMED — Microsoft documentation]**

Windows classifies cores into efficiency classes based on CPPC rankings, even though AMD cores are architecturally identical:

- **Efficiency Class 0** (lower CPPC-ranked cores): Treated like "E-cores"
- **Efficiency Class 1** (higher CPPC-ranked cores): Treated like "P-cores"

The heterogeneous scheduling policies then apply:

| Policy | Value | Behavior |
|--------|-------|----------|
| SchedulingPolicy | 0 | All processors |
| | 1 | Performant only |
| | 2 | Prefer performant |
| | 3 | Efficient only |
| | 4 | Prefer efficient |
| | 5 | Automatic |

`ShortSchedulingPolicy` controls where short-running threads (below `ShortThreadRuntimeThreshold`) are placed. Each QoS level (High, Medium, Low, Eco, etc.) can have its own `SchedulingPolicy` and `ShortSchedulingPolicy`.

**Source:** Microsoft Learn — Heterogeneous Power Scheduling ([link](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/static-configuration-options-for-heterogeneous-power-scheduling)); Microsoft Learn — ShortSchedulingPolicy ([link](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/configuration-for-hetero-power-scheduling-shortschedulingpolicy))

### 2.3 CCX Pair Rotation for Thermals

**[CONFIRMED — AMD official, Robert Hallock]**

AMD's firmware does NOT simply rank the single fastest core as #1. When running single-threaded workloads, Windows rotates the thread between a **pair** of physical cores for thermal management (~5ms scheduling quantum). AMD's firmware selects the **pair of cores within the same CCX with the highest average frequency** as the preferred pair.

This explains why Ryzen Master "best cores" (single fastest silicon) may differ from Windows "preferred cores" (best pair on same CCX).

**Source:** Tom's Hardware — AMD clarification ([link](https://www.tomshardware.com/news/amd-no-windows-scheduler-isnt-selecting-wrong-ryzen-3000-cores-to-boost))

### 2.4 Scheduling Under Different Load Levels

| Load Level | Behavior |
|-----------|----------|
| **Light (1-2 threads)** | Threads land on highest CPPC-ranked cores. Thread rotation spreads across top-ranked pair within same CCX. Cache locality can override CPPC preference. |
| **Medium (4-8 threads)** | Windows fills highest-ranked cores first, then spills to lower-ranked cores. Top 4 cores may get loaded disproportionately, potentially causing thermal throttling. |
| **Heavy (all cores busy)** | CPPC ranking becomes irrelevant for placement — all cores are running. Ranking may still affect boost allocation (firmware prioritizes boost headroom for higher-ranked cores). |

**[COMMUNITY OBSERVATION]** Some users report that disabling CPPC Preferred Cores improves all-core performance by ~6-9% because work distributes more evenly, avoiding thermal hotspots on preferred cores.

**Source:** Overclock.net — Ryzen 5000 CPPC discussion ([link](https://www.overclock.net/threads/windows-11-ryzen-5000-cppc-cppc-pc-whats-your-view.1795283/))

### 2.5 EcoQoS / Quality of Service Integration

**[CONFIRMED — Microsoft documentation]**

Windows QoS levels directly influence CPPC-based core selection:

| QoS Level | Core Selection |
|-----------|---------------|
| **High** | Preferred/performant CPPC cores |
| **Deadline** | Performant cores to meet deadlines |
| **Medium** | Platform-dependent |
| **Eco** | Always efficient/lower-ranked CPPC cores, lowest frequency |

**Key APIs:**
- `SetProcessInformation` with `PROCESS_POWER_THROTTLING_EXECUTION_SPEED` → tags process as EcoQoS
- `SetThreadInformation` with `THREAD_POWER_THROTTLING_EXECUTION_SPEED` → tags thread as EcoQoS

Below-normal priority processes may be confined to efficiency-class cores.

**Source:** Microsoft Learn — Quality of Service ([link](https://learn.microsoft.com/en-us/windows/win32/procthread/quality-of-service)); Microsoft DevBlog — Introducing EcoQoS ([link](https://devblogs.microsoft.com/performance-diagnostics/introducing-ecoqos/))

### 2.6 Cross-CCD Latency (Not NUMA-Exposed)

**[CONFIRMED]**

Ryzen desktop CPUs do **NOT** expose dual CCDs as separate NUMA nodes (unlike EPYC). Both CCDs appear as a single NUMA domain. The scheduler has no explicit NUMA-level guidance to avoid cross-CCD scheduling — its only CCD-awareness comes from CPPC rankings and heterogeneous scheduling classification.

Cross-CCD latency penalties are significant:

| Processor | Intra-CCD Latency | Inter-CCD Latency |
|-----------|-------------------|-------------------|
| 7950X3D (Zen 4) | ~17-18 ns | ~80-84 ns |
| 9950X3D (Zen 5) | ~17-18 ns | ~75 ns (halved from initial ~180 ns via AGESA update) |

**Source:** Tom's Hardware — AMD cross-CCD latency ([link](https://www.tomshardware.com/pc-components/cpus/amd-microcode-improves-cross-ccd-latency-on-ryzen-9000-cpus-ryzen-9-9900x-and-ryzen-9-9950x-cross-ccd-latency-cut-in-half-to-match-previous-gen-models)); TechPowerUp — 7950X CCD disabled benchmarks ([link](https://www.techpowerup.com/299959/amd-ryzen-9-7950x-posts-significantly-higher-gaming-performance-with-a-ccd-disabled?cp=2))

---

## 3. How the amd3dvcache Driver Interacts with CPPC

### 3.1 Architecture: Three Components

**[CONFIRMED — Linux kernel source, cocafe/vcache-tray reverse engineering]**

The AMD 3D V-Cache optimization on Windows consists of three components:

| Component | Role |
|-----------|------|
| `amd3dvcache.sys` | Kernel driver — communicates with AGESA firmware via ACPI DSM |
| `amd3dvcacheSvc.exe` | Service — monitors power mode changes, triggers driver |
| `AMD PPM Provisioning File Driver` | Modifies GameMode power profile to configure core parking |

### 3.2 The Full Scheduling Chain

```
Xbox Game Bar detects game in foreground
    │
    ▼
Windows activates GameMode power profile
    │
    ▼
amd3dvcacheSvc receives PowerRegisterForEffectivePowerModeNotifications() callback
    │
    ▼
amd3dvcache.sys calls ACPI DSM (GUID dff8e55f-bcfd-46fb-ba0a-efd0450f34ee)
    Function: DSM_SET_X3D_MODE (1), Argument: 1 (cache mode)
    │
    ▼
AGESA firmware in BIOS:
    - Rewrites CPPC highest_perf values for all cores
    - V-Cache CCD cores get higher highest_perf
    - Fires System Control Interrupt (SCI) to notify OS
    │
    ▼
Windows ACPI subsystem re-reads _CPC objects for all processors
    │
    ▼
Windows scheduler uses updated CPPC rankings:
    - New threads preferentially placed on V-Cache CCD cores
    - PPM Provisioning driver parks frequency CCD cores
    - Game threads concentrated on V-Cache CCD
```

When the game exits, the reverse occurs — PREFER_FREQ mode restores frequency CCD cores as preferred.

**Source:** Linux kernel `drivers/platform/x86/amd/x3d_vcache.c` ([direct source](https://raw.githubusercontent.com/torvalds/linux/master/drivers/platform/x86/amd/x3d_vcache.c)); HardForum AMD technical chat ([link](https://hardforum.com/threads/ryzen-7000x3d-series-a-brief-technical-chat-with-amd.2027211/))

### 3.3 The ACPI DSM Mechanism (from Linux kernel source)

```c
// ACPI Device ID: AMDI0101
static guid_t x3d_guid = GUID_INIT(0xdff8e55f, 0xbcfd, 0x46fb,
                                     0xba, 0x0a, 0xef, 0xd0, 0x45, 0x0f, 0x34, 0xee);

#define DSM_SET_X3D_MODE   1

enum amd_x3d_mode_type {
    MODE_INDEX_FREQ,    // 0 = frequency preference
    MODE_INDEX_CACHE,   // 1 = cache preference
};
```

The driver is a thin shim — it contains **zero CPPC logic**. It only tells firmware "switch to cache mode" or "switch to frequency mode." The firmware handles all CPPC ranking manipulation.

**Source:** Linux kernel `drivers/platform/x86/amd/x3d_vcache.c`

### 3.4 Registry Interface

**[CONFIRMED — cocafe/vcache-tray source code]**

The service exposes a registry-based control interface:

```
HKLM\SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences
    DefaultType (DWORD): 0 = PREFER_FREQ, 1 = PREFER_CACHE
```

Per-application profiles:

```
HKLM\...\amd3dvcache\Preferences\App\{ProfileName}
    EndsWith (REG_SZ): process name to match (e.g., "Cyberpunk2077.exe")
    Type (DWORD): 0 = freq, 1 = cache
```

**Timing:** Registry changes take **up to several minutes** to propagate without restarting the service. Restarting `amd3dvcacheSvc` applies changes immediately.

**Source:** cocafe/vcache-tray `src/registry.h` and `src/registry.c` ([GitHub](https://github.com/cocafe/vcache-tray)); DarthAffe/AMD3DConfigurator ([GitHub](https://github.com/DarthAffe/AMD3DConfigurator))

### 3.5 What the Driver Does NOT Do

**[CONFIRMED]**

- Does **NOT** use `SetProcessAffinityMask` or `SetThreadAffinityMask`
- Does **NOT** set per-process affinity at the userspace level
- Operates at the **ACPI/firmware level**, changing what the scheduler sees as "preferred" cores
- Cannot override explicit thread affinity set by other tools (like Process Lasso)
- The per-app `\Preferences\App` profiles switch the **entire system's** CCD preference, not per-process affinity

**Source:** cocafe/vcache-tray README — "Prefer CCD may not affect programs or threads that have been set affinity by themselves"

### 3.6 Can the Driver Override BIOS CPPC Preference?

**[CONFIRMED]**

| BIOS Setting | Driver Can Override? | Reason |
|-------------|---------------------|--------|
| Auto | **Yes** — this is the intended mode | Driver's ACPI device (AMDI0101) is present |
| Driver | **Yes** — explicitly enables driver control | Driver's ACPI device is present |
| Cache | **No** | ACPI device not exposed; driver not loaded |
| Frequency | **No** | ACPI device not exposed; driver not loaded |
| CPPC Disabled | **No** | No CPPC infrastructure at all; driver not loaded |

The BIOS "Cache" and "Frequency" settings achieve similar outcomes to the driver's PREFER_CACHE/PREFER_FREQ, but through static firmware-level CPPC rankings with no dynamic switching capability.

### 3.7 Zen 5 X3D Driver Updates

**[CONFIRMED — VideoCardz, AMD chipset release notes]**

The Zen 5 X3D chipset driver (v7.01.08.129+) added:

1. **Updated 3D V-Cache Performance Optimizer** — same ACPI DSM mechanism, updated for Zen 5
2. **AMD Application Compatibility Database** — per-game whitelist applying optimizations (e.g., `ProcessorCountLie` to reduce thread pool size for games that misbehave with high core counts):
   - Current game list: Deus Ex: Mankind Divided, Dying Light 2, Far Cry 6, Metro Exodus, Metro Exodus Enhanced Edition, Total War: Three Kingdoms, Total War: Warhammer III, Wolfenstein: Young Blood

The fundamental mechanism (ACPI DSM to AGESA firmware) is identical between Zen 4 X3D and Zen 5 X3D.

**Source:** VideoCardz — AMD chipset updates for 9950X3D ([link](https://videocardz.com/newz/amd-confirms-chipset-driver-updates-and-new-features-for-ryzen-9-9950x3d-9900x3d))

### 3.8 Xbox Game Bar Dependency Risk

**[CONFIRMED — Neowin, March 2025]**

Microsoft reportedly disabled critical Xbox Game Bar functionality on Windows 10 Pro and Enterprise editions in March 2025. This directly impacts AMD X3D dual-CCD processor performance because Game Bar is the mechanism that triggers CCD switching.

**Source:** Neowin — Microsoft disables vital Windows feature ([link](https://www.neowin.net/news/report-microsoft-quietly-disables-vital-windows-feature-crippling-many-amd-ryzen-cpus/))

---

## 4. Per-Setting Analysis: Real-World Scheduling Outcomes

### 4.1 CPPC Dynamic Preferred Cores = Auto (Recommended Default)

**Non-gaming (PREFER_FREQ default):**
- Frequency CCD cores ranked highest by CPPC
- Standard apps, background tasks, single-threaded workloads → frequency CCD
- CCD1 cores boost to ~5750 MHz (7950X3D) / ~5700 MHz (9950X3D)
- V-Cache CCD is mostly idle or receives overflow work

**Gaming (GameMode triggers PREFER_CACHE):**
- Xbox Game Bar detects game → GameMode activates → amd3dvcacheSvc triggers mode switch
- CPPC rankings invert: V-Cache CCD cores ranked highest
- PPM Provisioning parks frequency CCD cores
- Game threads land on V-Cache CCD
- Background threads go to whatever unpacked cores remain

**AMD's official recommendation:** Auto with Balanced power plan, Game Mode ON, Xbox Game Bar running.

**Where threads land:**
- Game threads → V-Cache CCD (CCD0 cores 0-15 on 7950X3D)
- Background threads → frequency CCD in non-gaming; leftover cores during gaming

**Benchmark impact:** This is the baseline. Our FFXIV testing showed ~34,000 in Monitor mode with Auto/PREFER_FREQ — the OS and driver were already placing threads effectively without intervention.

**Source:** Hardware Times — AMD recommendation ([link](https://hardwaretimes.com/amd-enable-the-xbox-game-bar-on-the-ryzen-9-7900x3d-7950x3d-processors-for-better-performance/)); project's BENCHMARK_RESEARCH.md

### 4.2 CPPC Dynamic Preferred Cores = Driver

**[PARTIALLY CONFIRMED — functionally identical to Auto for X3D parts]**

Appears to be functionally identical to "Auto" when the AMD chipset driver is installed. Both allow the amd3dvcache driver to control CCD preference dynamically. The distinction may matter for non-X3D Ryzen chips where "Auto" lets firmware handle CPPC rankings statically while "Driver" explicitly defers to the OS.

Some community reports suggest "Driver" may produce slightly better core parking behavior on specific motherboards (e.g., ASRock Taichi Lite with 9950X3D).

**Source:** Overclock.net — Core parking on 9950X3D and Taichi Lite ([link](https://www.overclock.net/threads/how-i-fixed-core-parking-on-my-9950x3d-and-taichi-lite.1815819/))

### 4.3 CPPC Dynamic Preferred Cores = Frequency

**Does V-Cache CCD get starved?**

**[CONFIRMED — Yes.]** With "Frequency" set, 99% of activity occurs on the frequency CCD (CCD1). CCD0 (V-Cache) is effectively idle except under heavy all-core loads. Even single-core Cinebench loads run on CCD1 cores.

**Do games still get scheduled on V-Cache CCD?**

**[CONFIRMED — No.]** The GameMode inversion mechanism is disabled because the amd3dvcache driver is not loaded. Games receive zero V-Cache benefit. Shadow of the Tomb Raider showed ~20% FPS loss when running on frequency cores vs V-Cache cores.

**Use case:** Productivity-only workloads that don't benefit from extra cache. For gaming, this is the worst setting — it defeats the entire purpose of V-Cache.

**Known bugs:** A known AGESA-level bug exists where enabling fmax override breaks CPPC preferred core function when set to "Frequency," causing threads to land randomly.

**Sources:** PCWorld — How AMD Ryzen 7950X3D V-Cache works ([link](https://www.pcworld.com/article/1524857/how-to-use-amds-ryzen-7000-v-cache-on-windows.html)); Overclock.net — 7950X3D ignoring Prefer Frequency ([link](https://www.overclock.net/threads/7950x3d-is-ignoring-prefer-frequency-in-win11-win10-and-cores-do-not-boost-quite-to-their-max-under-load.1805640/))

### 4.4 CPPC Dynamic Preferred Cores = Cache

**Does Frequency CCD get starved?**

**[CONFIRMED — Yes.]** The frequency CCD becomes the "Efficient" tier and receives minimal scheduling unless the V-Cache CCD is fully loaded.

**Interaction with amd3dvcache driver:**

**There is no conflict** because they cannot coexist. Setting "Cache" in BIOS removes the amd3dvcache device entirely. The driver's registry preferences (DefaultType) become irrelevant — never read.

| Mechanism | How CCD preference is set | Dynamic switching? |
|-----------|--------------------------|-------------------|
| BIOS "Cache" | Static firmware CPPC rankings | **No** — always V-Cache preferred |
| Driver PREFER_CACHE | Runtime ACPI DSM to firmware | **Yes** — can toggle back to PREFER_FREQ |

**Performance impact:** All workloads are routed to the V-Cache CCD (lower boost clocks). Productivity/single-threaded workloads that don't benefit from cache lose the ~450 MHz (Zen 4) or ~150 MHz (Zen 5) clock advantage.

**Source:** CachyOS Wiki — General system tweaks ([link](https://wiki.cachyos.org/configuration/general_system_tweaks/))

### 4.5 CPPC Disabled (Flat Ranking)

**How does the scheduler choose?**

**[CONFIRMED — NOT random.]** With flat CPPC rankings, the scheduler uses:
1. **Cache locality** — prefers keeping threads on the same L3 domain (same CCD/CCX) as last execution
2. **C-state/idle status** — prefers waking idle cores before overloading busy ones
3. **Power plan settings** — still respects core parking policies, but without CPPC quality information
4. **NUMA proximity** — if "ACPI SRAT L3 Cache as NUMA Domain" is enabled in BIOS, each CCD appears as a NUMA node

Work distributes more evenly across all cores. No CCD is systematically preferred.

**Performance impact:**

| Workload | Effect of CPPC Disabled vs Enabled |
|----------|------------------------------------|
| Multi-threaded (AVX2) | **+7-9%** — even distribution avoids thermal hotspots |
| Multi-threaded (SSE4.x) | **+6%** |
| Single-threaded | **-1.5%** — scheduler can't identify best-boosting core |
| Gaming | **Mixed** — no mechanism to direct games to V-Cache CCD |

**Critical limitation:** Disabling CPPC also removes the amd3dvcache device. There is **no mechanism** to direct game threads to the V-Cache CCD — the driver cannot function without CPPC.

**[COMMUNITY OBSERVATION]** Some users with 8kHz polling rate mice report reduced micro-stutters with CPPC Preferred Cores disabled. The theory is that CPPC concentrates work on 2-4 "best" cores, and 8,000 mouse interrupts/second create scheduling contention when competing with game threads on those same cores.

**Sources:** Overclock.net — Disable CPPC Preferred Cores for less 8kHz stutters ([link](https://www.overclock.net/threads/disable-cppc-preferred-cores-for-less-8khz-stutters.1811970/)); Overclock.net — CPPC discussion ([link](https://www.overclock.net/threads/windows-11-ryzen-5000-cppc-cppc-pc-whats-your-view.1795283/))

### 4.6 Prerequisite: C-States Must Be Enabled

**[COMMUNITY OBSERVATION with technical basis]**

On dual-CCD X3D processors, BIOS "C-States = Auto" may effectively disable C-states, disrupting CPPC's preferred core mechanism. CPPC requires C-state transitions to update preferred core rankings dynamically. Community recommendation: explicitly enable C-states in BIOS for proper CPPC function on X3D chips.

**Source:** WindowsForum — X3D C-state tweaks ([link](https://windowsforum.com/threads/optimize-ryzen-x3d-gaming-performance-simple-bios-c-state-tweaks-for-smoothness.371073/))

---

## 5. Implications for Our Tool

### 5.1 Per-CPPC Setting: What Intervention Adds Value?

#### Auto/Driver (Recommended — Driver Active)

| Intervention | Value | Risk |
|-------------|-------|------|
| **DriverPreference (set PREFER_CACHE via registry)** | **Low to negative.** AMD's GameMode already switches to PREFER_CACHE automatically when a game is detected. Setting it manually is redundant if Game Bar works. Could cause the V-Cache CCD to be preferred for non-game workloads when it shouldn't be. | Overriding the driver's dynamic switching removes the automatic PREFER_FREQ restoration when gaming ends. Desktop/productivity performance may degrade. |
| **AffinityPinning (game to V-Cache CCD)** | **Marginal, with significant risks.** Adds a hard guarantee beyond CPPC's soft preference. But our FFXIV testing showed that the soft preference was already working — 34k with no intervention vs 31k with intervention. | Background migration of ~166 processes caused the regression, not the game pin itself. However, hard-pinning the game also prevents it from using extra cores on the frequency CCD if it needs them. |
| **Background app migration** | **Negative.** Our testing proved this conclusively — migrating ~166 processes caused ~9% regression by disrupting AMD driver services, GPU drivers, and OS scheduler optimization. | Disrupts `amd3dvcacheSvc`, Game Bar, GPU driver services, Windows shell. |
| **Selective background migration (user-configured apps only)** | **Low positive for specific use cases.** Moving Discord, OBS, or browser to the frequency CCD could reduce cache pollution on V-Cache CCD. But the OS and driver already handle this via CPPC rankings and core parking. | Must never touch system services, AMD driver, GPU drivers. Must be explicitly user-opted. |
| **Detect game, write to `\Preferences\App` registry** | **Moderate positive.** Works *with* the AMD driver rather than against it. Provides per-game CCD preference that persists across the AMD driver's own mechanism. Does not fight the scheduler or use affinity masks. | Requires service restart for immediate effect. The driver's per-app profile switches the entire system, not just the game process. |

#### Cache (Static — Driver Not Active)

| Intervention | Value | Risk |
|-------------|-------|------|
| **Any CCD-routing intervention** | **Redundant.** The firmware already hardcodes V-Cache CCD as preferred for everything. Our tool cannot improve on this. | Could interfere with the static ranking by setting conflicting affinity masks. |
| **Selective migration of frequency-hungry apps** | **Low positive.** If user runs both games and productivity apps simultaneously, migrating the productivity app to the frequency CCD could help. | Edge case. |

#### Frequency (Static — Driver Not Active)

| Intervention | Value | Risk |
|-------------|-------|------|
| **AffinityPinning (game to V-Cache CCD)** | **High positive.** This is the one scenario where our tool provides clear value — the BIOS has set frequency CCD as preferred, defeating V-Cache benefit for games. Hard-pinning the game to V-Cache CCD restores V-Cache benefit. | The user probably set "Frequency" intentionally for productivity. Tool should warn, not override silently. |
| **DriverPreference** | **Cannot work.** The amd3dvcache driver is not loaded. Registry writes are ignored. | N/A |

#### Disabled (No CPPC — Driver Not Active)

| Intervention | Value | Risk |
|-------------|-------|------|
| **AffinityPinning (game to V-Cache CCD)** | **Moderate positive.** Without CPPC, there is zero mechanism to route games to V-Cache CCD. Affinity pinning is the only available tool. | The user disabled CPPC intentionally (possibly for multi-threaded performance or 8kHz mouse latency). Tool should respect this choice and only pin if explicitly configured. |
| **DriverPreference** | **Cannot work.** No CPPC infrastructure. | N/A |

### 5.2 Settings Where Our Tool Is Redundant

- **Auto/Driver with Game Bar working correctly**: The AMD driver already handles CCD switching for games. Our DriverPreference mode duplicates what GameMode already does. Our FFXIV baseline (34k with zero intervention) proves the system works without us.

### 5.3 Settings Where Our Tool Is Harmful

- **Auto/Driver with background migration**: Our testing proved that migrating ~166 processes causes ~9% regression by disrupting scheduling infrastructure. This is harmful regardless of CPPC setting.
- **Cache with any affinity intervention**: Fighting the firmware's static preference creates unpredictable scheduling.

### 5.4 Settings Where Our Tool Adds Value

- **Auto/Driver when Game Bar is broken/disabled**: Microsoft disabled Game Bar features on Windows 10 Pro/Enterprise (March 2025). If Game Bar can't trigger GameMode, the automatic CCD switch never happens. Our tool can fill this gap by writing to the driver's `\Preferences\App` registry or using affinity masks.
- **Frequency setting**: Users who set "Frequency" for productivity but sometimes game. Affinity pinning to V-Cache CCD is the only way to get V-Cache benefit.
- **Disabled setting**: Users who disabled CPPC for multi-threaded performance but want V-Cache benefit for specific games. Affinity pinning is the only option.
- **Per-game CCD preference**: Some games perform better on the frequency CCD (low cache sensitivity, high clock sensitivity). The AMD driver's per-app profiles support this, but users have no UI for it. Our tool can provide this UI.
- **Games not in Xbox Game Bar's KGL whitelist**: New or obscure games that Game Bar doesn't recognize won't trigger GameMode → no automatic CCD switch. Our tool can detect these games and set the appropriate CCD preference.

### 5.5 Should the Tool Detect CPPC Setting and Adapt?

**Yes. Strongly recommended.**

The tool should detect:

| Detection | Method | Action |
|-----------|--------|--------|
| Is `amd3dvcacheSvc` running? | `Process.GetProcessesByName("amd3dvcacheSvc")` or WMI service query | If yes: work WITH the driver (use registry interface). If no: use affinity masks as fallback. |
| What is `DefaultType`? | Read `HKLM\...\amd3dvcache\Preferences\DefaultType` | Know current system-wide preference. Display in UI. |
| Is CPPC enabled? | Check if `AMDI0101` device exists in Device Manager, or check `amd3dvcache` service status | If driver not loaded: CPPC is likely set to Cache/Frequency/Disabled. Adapt strategy. |
| Is Game Bar running? | Check for `GameBarPresenceWriter.exe` | If not running: the automatic CCD switch won't happen. Our tool fills the gap. |

**Recommended behavior matrix:**

| AMD Driver Running? | Game Bar Running? | Our Tool's Best Strategy |
|--------------------|-------------------|--------------------------|
| Yes | Yes | **Minimal intervention.** System is handling CCD switching. Offer per-game overrides via `\Preferences\App` registry only. Monitor mode is optimal. |
| Yes | No | **Write `\Preferences\App` registry** for detected games. Use the driver's own mechanism without Game Bar as trigger. |
| No | N/A | **Affinity pinning** is the only available mechanism. Use `SetProcessAffinityMask` for game-to-CCD routing. |

---

## 6. Summary Table

| CPPC Dynamic Preferred Cores | amd3dvcache Driver | GameMode Switching | Non-Gaming Preferred CCD | Gaming Preferred CCD | Our Tool Value | Recommended Intervention |
|------------------------------|-------------------|-------------------|-------------------------|---------------------|---------------|--------------------------|
| **Auto** | Loaded | Yes (dynamic) | Frequency | V-Cache | **Low** (system works without us) | Monitor; offer per-game `\Preferences\App` registry overrides; fill Game Bar gap |
| **Driver** | Loaded | Yes (dynamic) | Frequency | V-Cache | **Low** (identical to Auto) | Same as Auto |
| **Cache** | Not loaded | No | V-Cache (always) | V-Cache (always) | **Redundant** | None needed — firmware handles everything |
| **Frequency** | Not loaded | No | Frequency (always) | Frequency (always) | **High** (only way to get V-Cache benefit) | AffinityPinning game to V-Cache CCD |
| **Disabled** | Not loaded | No | Flat (topology-based) | Flat (topology-based) | **Moderate** (only way to route to V-Cache) | AffinityPinning game to V-Cache CCD if user opts in |

---

## 7. Open Questions

### Cannot Be Answered from Available Sources

1. **Exact Windows mapping of CPPC to scheduling weight**: How does Windows convert `highest_perf` values into scheduler priority internally? Is there a linear mapping, threshold-based classification, or something else? Microsoft has not documented this.

2. **PPM Provisioning core parking specifics**: Which exact cores does the AMD PPM Provisioning driver park during GameMode? Does it park the entire frequency CCD, or only lower-ranked cores? The parking behavior is not documented.

3. **Latency of CCD mode switch**: How many milliseconds does it take from GameMode activation to the CPPC rankings being fully updated and the scheduler acting on them? Community reports say "up to several minutes" for registry-based changes, but the GameMode path may be faster.

4. **Interaction between affinity masks and CPPC-driven core parking**: If our tool sets an affinity mask that includes cores the PPM driver has parked, does the parked core wake up? Or does the thread wait on a non-parked core within the mask? Undocumented.

5. **Windows 10 vs Windows 11 CPPC behavior**: Are there differences in how Windows 10 and Windows 11 handle CPPC rankings? The heterogeneous scheduling policies were introduced in Windows 11, but CPPC support exists in both. How does Windows 10 use the rankings?

6. **Game Bar KGL whitelist contents**: What games are in Xbox Game Bar's Known Game List? Is it updated through Windows Update or Game Bar updates? Can it be modified by users?

7. **`ProfileCacheMisses` mapping on AMD**: Our PMC research (docs/PMC_RESEARCH.md) identified that ETW's `ProfileCacheMisses` (KPROFILE_SOURCE ID 29) could measure LLC misses, but the exact AMD event it maps to is undocumented by Microsoft. Does it map to L3 misses or L2 misses on Zen 4/5?

8. **CPPC behavior with PBO2/CO**: Does Precision Boost Overdrive 2 / Curve Optimizer interact with CPPC preferred core rankings? Several community reports mention ASUS-specific bugs where PBO fmax override breaks CPPC.

### Conflicting Information Across Sources

9. **"Several minutes" vs instant mode switching**: cocafe/vcache-tray documents "up to several minutes" for registry changes to propagate. But the GameMode path (via `PowerRegisterForEffectivePowerModeNotifications`) should be near-instant. Are these different code paths in the service? Or is the "several minutes" claim outdated?

10. **Process Lasso vs Driver performance**: LinusTechTips forum claims "major difference" favoring Process Lasso; Overclock.net X3D Owner's Club reports Process Lasso causes *worse* performance in multi-CCD games. The answer is likely game-dependent, but no systematic comparison exists.

---

## 8. Sources

### Primary Sources (AMD Documentation, Kernel Source)

- [Linux kernel `include/acpi/cppc_acpi.h`](https://github.com/torvalds/linux/blob/master/include/acpi/cppc_acpi.h) — CPPC register definitions
- [Linux kernel `arch/x86/include/asm/msr-index.h`](https://github.com/torvalds/linux/blob/master/arch/x86/include/asm/msr-index.h) — AMD CPPC MSR addresses
- [Linux kernel `arch/x86/kernel/acpi/cppc.c`](https://github.com/torvalds/linux/blob/master/arch/x86/kernel/acpi/cppc.c) — AMD preferred core detection and ranking
- [Linux kernel `drivers/platform/x86/amd/x3d_vcache.c`](https://raw.githubusercontent.com/torvalds/linux/master/drivers/platform/x86/amd/x3d_vcache.c) — AMD 3D V-Cache optimizer driver (ACPI DSM mechanism)
- [Linux kernel amd-pstate documentation](https://docs.kernel.org/admin-guide/pm/amd-pstate.html) — AMD P-State driver with CPPC integration
- [Linux kernel CPPC sysfs documentation](https://docs.kernel.org/admin-guide/acpi/cppc_sysfs.html) — CPPC userspace interface
- [ACPI 6.5 Specification Section 8](https://uefi.org/specs/ACPI/6.5/08_Processor_Configuration_and_Control.html?highlight=cppc) — CPPC standard definition
- AMD PPR for Family 19h Model 11h, Rev B1 (doc #55901) — Zen 4 performance monitoring registers
- [AMD Performance Monitor Counters for Family 1Ah (doc #58550)](https://www.amd.com/content/dam/amd/en/documents/epyc-technical-docs/programmer-references/58550-0.01.pdf) — Zen 5 PMC reference

### Microsoft Documentation

- [Microsoft Learn — Quality of Service](https://learn.microsoft.com/en-us/windows/win32/procthread/quality-of-service) — EcoQoS and thread QoS levels
- [Microsoft Learn — Power Performance Tuning](https://learn.microsoft.com/en-us/windows-server/administration/performance-tuning/hardware/power/power-performance-tuning) — CPPC boost modes
- [Microsoft Learn — Processor Power Management Options](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/configure-processor-power-management-options) — Power profile configuration
- [Microsoft Learn — Heterogeneous Power Scheduling](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/static-configuration-options-for-heterogeneous-power-scheduling) — SchedulingPolicy options
- [Microsoft Learn — ShortSchedulingPolicy](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/configuration-for-hetero-power-scheduling-shortschedulingpolicy) — Short thread scheduling on heterogeneous cores
- [Microsoft DevBlog — Introducing EcoQoS](https://devblogs.microsoft.com/performance-diagnostics/introducing-ecoqos/) — EcoQoS API and behavior

### Reverse Engineering / Open Source Tools

- [cocafe/vcache-tray](https://github.com/cocafe/vcache-tray) — Registry interface discovery, per-app profiles, driver behavior documentation
- [cocafe/vcache-tray `src/registry.h`](https://github.com/cocafe/vcache-tray/blob/master/src/registry.h) — Registry key definitions
- [cocafe/vcache-tray `src/registry.c`](https://github.com/cocafe/vcache-tray/blob/master/src/registry.c) — Registry read/write implementation
- [DarthAffe/AMD3DConfigurator](https://github.com/DarthAffe/AMD3DConfigurator) — Confirms same registry structure
- [Coldblackice/AMD-X3D-Vcache-tray](https://github.com/Coldblackice/AMD-X3D-Vcache-tray) — Active fork of vcache-tray
- [pyrotiger/x3d-toggle](https://github.com/pyrotiger/x3d-toggle) — Linux X3D mode switching tool
- [LWN.net — AMD 3D V-Cache optimizer driver patch](https://lwn.net/Articles/995003/) — Kernel driver review
- [LWN.net — AMD Pstate Preferred Core](https://lwn.net/Articles/941664/) — Preferred core kernel implementation

### Hardware Reviews and Technical Analysis

- [Tom's Hardware — AMD says Windows Scheduler isn't selecting wrong cores](https://www.tomshardware.com/news/amd-no-windows-scheduler-isnt-selecting-wrong-ryzen-3000-cores-to-boost) — AMD official CCX pair rotation explanation
- [Tom's Hardware — AMD cross-CCD latency fix](https://www.tomshardware.com/pc-components/cpus/amd-microcode-improves-cross-ccd-latency-on-ryzen-9000-cpus-ryzen-9-9900x-and-ryzen-9-9950x-cross-ccd-latency-cut-in-half-to-match-previous-gen-models) — Zen 5 inter-CCD latency improvement
- [Tom's Hardware — AMD Ryzen 9 7950X3D Review](https://www.tomshardware.com/reviews/amd-ryzen-9-7950x3d-cpu-review/3) — Benchmark data and CCD scheduling
- [Tom's Hardware — AMD Ryzen 9 9950X3D Review](https://www.tomshardware.com/pc-components/cpus/amd-ryzen-9-9950x3d-review) — Zen 5 X3D benchmark data
- [GamersNexus — AMD Ryzen 9 9950X3D Review](https://gamersnexus.net/cpus/amd-ryzen-9-9950x3d-cpu-review-benchmarks-vs-9800x3d-285k-9950x-more) — Confirms parking works properly on 9950X3D
- [Chips and Cheese — AMD's 9800X3D: 2nd Generation V-Cache](https://old.chipsandcheese.com/2024/11/06/amds-9800x3d-2nd-generation-v-cache/) — V-Cache thermal and architectural improvements
- [PCWorld — How AMD's Ryzen 7950X3D V-Cache works on Windows](https://www.pcworld.com/article/1524857/how-to-use-amds-ryzen-7000-v-cache-on-windows.html) — CCD scheduling overview
- [TechPowerUp — 7950X single CCD performance](https://www.techpowerup.com/299959/amd-ryzen-9-7950x-posts-significantly-higher-gaming-performance-with-a-ccd-disabled?cp=2) — Cross-CCD penalty benchmarks
- [TechSpot — AMD Ryzen 9 9950X3D Review](https://www.techspot.com/review/2965-amd-ryzen-9-9950x3d/) — Gaming and productivity benchmarks
- [VideoCardz — AMD chipset updates for 9950X3D/9900X3D](https://videocardz.com/newz/amd-confirms-chipset-driver-updates-and-new-features-for-ryzen-9-9950x3d-9900x3d) — Application Compatibility Database
- [IDC — Review of AMD Ryzen 9 9950X3D](https://www.idc.com/resource-center/blog/review-of-the-amd-ryzen-9-9950x3d/) — Technical deep dive
- [Phoronix — AMD 3D V-Cache Optimizer 9950X3D](https://www.phoronix.com/review/amd-3d-vcache-optimizer-9950x3d) — Linux driver testing
- [HotHardware — AMD Reveals Reason for No Dual V-Cache](https://hothardware.com/news/amd-no-dual-vcache-cpus) — AMD CES 2025 confirmation
- [Alois Kraus — Hybrid CPU Performance on Windows 10 and 11](https://aloiskraus.wordpress.com/2024/02/08/hybrid-cpu-performance-on-windows-10-and-11/) — Detailed Windows scheduler analysis

### Community Sources (Forums and Discussions)

- [HardForum — Ryzen 7000X3D technical chat with AMD](https://hardforum.com/threads/ryzen-7000x3d-series-a-brief-technical-chat-with-amd.2027211/) — AMD confirms CCD switching mechanism
- [Hardware Times — AMD: Enable Xbox Game Bar on 7900X3D/7950X3D](https://hardwaretimes.com/amd-enable-the-xbox-game-bar-on-the-ryzen-9-7900x3d-7950x3d-processors-for-better-performance/) — AMD's Game Bar recommendation
- [Neowin — Microsoft disables Game Bar on Windows 10](https://www.neowin.net/news/report-microsoft-quietly-disables-vital-windows-feature-crippling-many-amd-ryzen-cpus/) — Game Bar dependency risk
- [Overclock.net — CPPC and CPPC Preferred Cores](https://www.overclock.net/threads/cppc-and-cppc-preferred-cores.1792460/) — BIOS setting explanations
- [Overclock.net — Zen 4 X3D Owners Club](https://www.overclock.net/threads/official-zen-4-x3d-owners-club-7800x3d-7900x3d-7950x3d.1803292/) — Community testing and observations
- [Overclock.net — Zen 5 X3D Owners Club](https://www.overclock.net/threads/official-zen-5-x3d-owners-club-9800x3d-9900x3d-9950x3d.1812505/) — 9950X3D scheduling observations
- [Overclock.net — Disable CPPC Preferred Cores for less 8kHz stutters](https://www.overclock.net/threads/disable-cppc-preferred-cores-for-less-8khz-stutters.1811970/) — High polling rate interaction
- [Overclock.net — 7950X3D ignoring Prefer Frequency](https://www.overclock.net/threads/7950x3d-is-ignoring-prefer-frequency-in-win11-win10-and-cores-do-not-boost-quite-to-their-max-under-load.1805640/) — AGESA bug with fmax override
- [Overclock.net — Windows 11 Ryzen 5000 CPPC discussion](https://www.overclock.net/threads/windows-11-ryzen-5000-cppc-cppc-pc-whats-your-view.1795283/) — CPPC performance impact
- [Overclock.net — Core parking on 9950X3D and Taichi Lite](https://www.overclock.net/threads/how-i-fixed-core-parking-on-my-9950x3d-and-taichi-lite.1815819/) — Motherboard-specific issues
- [Overclock.net — How does the 7950X3D/9950X3D CCDs work](https://www.overclock.net/threads/how-does-the-amd-7950x3d-and-9950x3d-ccds-work.1814692/) — CCD architecture
- [Overclock.net — Ryzen Custom Power Plans](https://www.overclock.net/threads/ryzen-custom-power-plans-for-windows-10-11-snappy-lowpower-highpower.1776353/) — Power plan tuning for CPPC
- [LinusTechTips Forum — 9950X3D benchmarks with Process Lasso](https://linustechtips.com/topic/1606170-major-difference-in-9950x3d-benchmarks-with-process-lasso/) — Process Lasso vs driver comparison
- [Tom's Hardware Forum — 9950X3D refuses to use CCD0](https://forums.tomshardware.com/threads/dual-ccd-issue-ryzen-9950x3d-refuses-to-use-ccd0-despite-multiple-tweaks-in-warzone-cs2-etc.3891051/) — CCD0 refusal bug
- [WindowsForum — X3D C-state tweaks](https://windowsforum.com/threads/optimize-ryzen-x3d-gaming-performance-simple-bios-c-state-tweaks-for-smoothness.371073/) — C-state prerequisite
- [VRChat Wiki — AMD X3D Series Processors](https://wiki.vrchat.com/wiki/Guides:AMD_X3D_Series_Processors) — Practical guide
- [CachyOS Wiki — General System Tweaks](https://wiki.cachyos.org/configuration/general_system_tweaks/) — Linux X3D configuration
- [Windows kernel thread scheduling analysis](https://medium.com/@amitmoshel70/mysteries-of-the-windows-kernel-pt-2-threads-scheduling-cpus-30125fbb46a3) — Scheduler internals

### Windows Kernel / Driver References

- [Tom's Hardware — Branch prediction backport](https://www.tomshardware.com/software/windows/microsoft-backports-branch-prediction-improvements-to-windows-11-23h2-more-users-will-see-ryzen-performance-improvements) — Windows 11 AMD optimization
- [WCCFTech — Ryzen L3/CPPC2 fix](https://wccftech.com/amd-ryzen-cpus-l3-latency-performance-fix-to-be-resolved-by-microsoft-through-windows-11-update-on-19th-october-cppc-driver-on-21st/) — Windows 11 launch bugs
- [Phoronix — AMD CPPC Performance Priority](https://www.phoronix.com/news/AMD-CPPC-Performance-Priority) — Upcoming Zen 6 CPPC extension
- [Neowin — AMD Zen 6 CPPC Performance Priority](https://www.neowin.net/news/windows-11-26h2-27h2-could-get-this-new-amd-zen-6-performance-boost-feature/) — Windows support for new CPPC features
- [HWiNFO Forum — CPPC Preferred Cores data query](https://www.hwinfo.com/forum/threads/how-does-hwinfo-query-the-cppc-preferred-cores-data.7927/) — How monitoring tools read CPPC
