namespace X3DCcdOptimizer.Models;

public class CpuTopology
{
    public string CpuModel { get; set; } = "Unknown";
    public ProcessorTier Tier { get; set; } = ProcessorTier.DualCcdX3D;
    public IntPtr VCacheMask { get; set; }
    public IntPtr FrequencyMask { get; set; }
    public int[] VCacheCores { get; set; } = [];
    public int[] FrequencyCores { get; set; } = [];
    public int VCacheL3SizeMB { get; set; }
    public int StandardL3SizeMB { get; set; }
    public int TotalPhysicalCores { get; set; }
    public int TotalLogicalCores { get; set; }

    public bool HasVCache => VCacheL3SizeMB > StandardL3SizeMB * 2;
    public bool IsSingleCcd => Tier is ProcessorTier.SingleCcdX3D or ProcessorTier.SingleCcdStandard;
    public bool IsDualCcd => !IsSingleCcd;

    public string VCacheMaskHex => $"0x{VCacheMask.ToInt64():X4}";
    public string FrequencyMaskHex => FrequencyMask != IntPtr.Zero ? $"0x{FrequencyMask.ToInt64():X4}" : "N/A";

    public int GetCcdIndex(int coreIndex)
    {
        // For single-CCD, all cores are CCD 0
        if (IsSingleCcd) return 0;
        return Array.Exists(VCacheCores, c => c == coreIndex) ? 0 : 1;
    }
}
