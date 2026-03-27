using System.Management;
using System.Runtime.InteropServices;
using Serilog;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Models;
using X3DCcdOptimizer.Native;

namespace X3DCcdOptimizer.Core;

public static class CcdMapper
{
    /// <summary>
    /// Detects the CCD topology of the current AMD Ryzen processor.
    /// Supports dual-CCD X3D, single-CCD X3D, and dual-CCD standard Ryzen.
    /// </summary>
    public static CpuTopology Detect(AppConfig config)
    {
        var (model, physicalCores) = GetCpuInfo();
        var topology = new CpuTopology
        {
            CpuModel = model,
            TotalPhysicalCores = physicalCores,
            TotalLogicalCores = Environment.ProcessorCount
        };

        Log.Information("Detecting CCD topology for {CpuModel} ({Physical} cores, {Logical} threads)",
            topology.CpuModel, topology.TotalPhysicalCores, topology.TotalLogicalCores);

        try
        {
            DetectViaPInvoke(topology);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "P/Invoke CCD detection failed, trying WMI fallback");
            try
            {
                DetectViaWmi(topology);
            }
            catch (Exception wmiEx)
            {
                Log.Error(wmiEx, "WMI CCD detection also failed");

                if (config.CcdOverride is { VCacheCores: not null, FrequencyCores: not null })
                {
                    Log.Warning("Using CCD override from config");
                    ApplyOverride(topology, config.CcdOverride);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Failed to detect CCD topology for {topology.CpuModel}. " +
                        "This may not be an AMD Ryzen processor with identifiable L3 cache topology. " +
                        "You can set ccdOverride in config.json to manually specify core assignments.",
                        wmiEx);
                }
            }
        }

        ValidateTopology(topology);
        return topology;
    }

    private static void DetectViaPInvoke(CpuTopology topology)
    {
        // First call: get required buffer size
        uint bufferSize = 0;
        Kernel32.GetLogicalProcessorInformationEx(Kernel32.RelationCache, IntPtr.Zero, ref bufferSize);

        int lastError = Marshal.GetLastWin32Error();
        if (lastError != 122) // ERROR_INSUFFICIENT_BUFFER
            throw new InvalidOperationException(
                $"GetLogicalProcessorInformationEx sizing call failed with error {lastError}");

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            if (!Kernel32.GetLogicalProcessorInformationEx(Kernel32.RelationCache, buffer, ref bufferSize))
                throw new InvalidOperationException(
                    $"GetLogicalProcessorInformationEx failed with error {Marshal.GetLastWin32Error()}");

            var l3Caches = ParseL3Caches(buffer, bufferSize);

            if (l3Caches.Count == 0)
                throw new InvalidOperationException("No L3 caches detected");

            Log.Debug("Found {Count} L3 caches", l3Caches.Count);
            foreach (var (sizeMB, mask) in l3Caches)
                Log.Debug("  L3: {Size}MB, Mask: 0x{Mask:X}", sizeMB, mask);

            AssignTopologyFromCaches(topology, l3Caches);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static List<(int SizeMB, ulong Mask)> ParseL3Caches(IntPtr buffer, uint bufferSize)
    {
        var l3Caches = new List<(int SizeMB, ulong Mask)>();
        var offset = 0;

        while (offset < bufferSize)
        {
            var ptr = IntPtr.Add(buffer, offset);

            // Read the header: Relationship (4 bytes) + Size (4 bytes)
            int relationship = Marshal.ReadInt32(ptr, 0);
            uint entrySize = (uint)Marshal.ReadInt32(ptr, 4);

            if (entrySize == 0)
                break; // Safety: avoid infinite loop

            if (relationship == Kernel32.RelationCache)
            {
                // CACHE_RELATIONSHIP starts at offset 8 (after the header)
                var cachePtr = IntPtr.Add(ptr, 8);
                var cacheRel = Marshal.PtrToStructure<CACHE_RELATIONSHIP>(cachePtr);

                if (cacheRel.Level == 3) // L3 cache
                {
                    int sizeMB = (int)(cacheRel.CacheSize / (1024 * 1024));
                    ulong mask = (ulong)cacheRel.GroupMask.Mask;

                    Log.Debug("L3 cache found: Level={Level}, Size={Size}MB, Mask=0x{Mask:X}",
                        cacheRel.Level, sizeMB, mask);

                    l3Caches.Add((sizeMB, mask));
                }
            }

            offset += (int)entrySize;
        }

        return l3Caches;
    }

    private static void DetectViaWmi(CpuTopology topology)
    {
        Log.Information("Attempting WMI fallback for CCD detection");

        var l3Caches = new List<(int SizeMB, ulong Mask)>();

        using var cacheSearcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_CacheMemory WHERE Level = 5"); // Level 5 = L3 in WMI
        cacheSearcher.Options.Timeout = TimeSpan.FromSeconds(10);

        int coresSoFar = 0;
        foreach (var cache in cacheSearcher.Get())
        {
            int sizeKB = Convert.ToInt32(cache["MaxCacheSize"]);
            int sizeMB = sizeKB / 1024;

            // Build a mask covering the cores for this cache group
            // WMI doesn't directly give us masks, so we estimate based on core count
            int coresPerCcd = topology.TotalLogicalCores / 2;
            ulong mask = 0;
            for (int i = 0; i < coresPerCcd; i++)
            {
                mask |= 1UL << (coresSoFar + i);
            }

            l3Caches.Add((sizeMB, mask));
            coresSoFar += coresPerCcd;

            Log.Debug("WMI L3: {Size}MB, estimated mask: 0x{Mask:X}", sizeMB, mask);
        }

        if (l3Caches.Count == 0)
            throw new InvalidOperationException("WMI found no L3 caches");

        AssignTopologyFromCaches(topology, l3Caches);
    }

    private static void AssignTopologyFromCaches(CpuTopology topology, List<(int SizeMB, ulong Mask)> l3Caches)
    {
        if (l3Caches.Count == 1)
        {
            // Single-CCD processor (e.g., 7800X3D, 9800X3D)
            var entry = l3Caches[0];
            topology.Tier = ProcessorTier.SingleCcdX3D;
            topology.VCacheL3SizeMB = entry.SizeMB;
            topology.StandardL3SizeMB = 0;
            topology.VCacheMask = new IntPtr((long)entry.Mask);
            topology.FrequencyMask = IntPtr.Zero;
            topology.VCacheCores = MaskToCores(entry.Mask);
            topology.FrequencyCores = [];
        }
        else
        {
            // Dual-CCD: sort by L3 size to identify V-Cache vs standard
            var sorted = l3Caches.OrderByDescending(c => c.SizeMB).ToList();
            var largerEntry = sorted[0];
            var smallerEntry = sorted[1];

            topology.VCacheL3SizeMB = largerEntry.SizeMB;
            topology.StandardL3SizeMB = smallerEntry.SizeMB;
            topology.VCacheMask = new IntPtr((long)largerEntry.Mask);
            topology.FrequencyMask = new IntPtr((long)smallerEntry.Mask);
            topology.VCacheCores = MaskToCores(largerEntry.Mask);
            topology.FrequencyCores = MaskToCores(smallerEntry.Mask);

            // Determine tier: if one L3 is >2x the other, it's X3D; otherwise standard dual-CCD
            topology.Tier = largerEntry.SizeMB > smallerEntry.SizeMB * 2
                ? ProcessorTier.DualCcdX3D
                : ProcessorTier.DualCcdStandard;
        }
    }

    private static void ApplyOverride(CpuTopology topology, CcdOverrideConfig ovr)
    {
        topology.VCacheCores = ovr.VCacheCores!;
        topology.FrequencyCores = ovr.FrequencyCores!;
        topology.VCacheMask = CoresMask(ovr.VCacheCores!);
        topology.FrequencyMask = CoresMask(ovr.FrequencyCores!);
        topology.VCacheL3SizeMB = 96; // Assume standard X3D layout
        topology.StandardL3SizeMB = 32;
    }

    private static (string Model, int PhysicalCores) GetCpuInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores FROM Win32_Processor");
            searcher.Options.Timeout = TimeSpan.FromSeconds(10);
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                var cores = Convert.ToInt32(obj["NumberOfCores"]);
                return (name, cores);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to query CPU info via WMI");
        }
        return ("Unknown", Environment.ProcessorCount / 2);
    }

    private static int[] MaskToCores(ulong mask)
    {
        var cores = new List<int>();
        for (int i = 0; i < 64; i++)
        {
            if ((mask & (1UL << i)) != 0)
                cores.Add(i);
        }
        return cores.ToArray();
    }

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

    private static void ValidateTopology(CpuTopology topology)
    {
        if (topology.VCacheCores.Length == 0)
            throw new InvalidOperationException("Primary CCD has no cores");

        // FrequencyCores can be empty for single-CCD processors
        if (topology.IsDualCcd && topology.FrequencyCores.Length == 0)
            throw new InvalidOperationException("Second CCD has no cores");

        Log.Information("Tier: {Tier}", topology.Tier);
        Log.Information("CCD0: Cores {Cores}, L3: {L3}MB, Mask: {Mask}",
            string.Join(",", topology.VCacheCores), topology.VCacheL3SizeMB, topology.VCacheMaskHex);

        if (topology.IsDualCcd)
        {
            Log.Information("CCD1: Cores {Cores}, L3: {L3}MB, Mask: {Mask}",
                string.Join(",", topology.FrequencyCores), topology.StandardL3SizeMB, topology.FrequencyMaskHex);
        }
    }
}
