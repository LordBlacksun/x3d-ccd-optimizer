namespace X3DCcdOptimizer.Models;

public class CpuTopology
{
    public string CpuModel { get; set; } = "Unknown";
    public IntPtr VCacheMask { get; set; }
    public IntPtr FrequencyMask { get; set; }
    public int[] VCacheCores { get; set; } = [];
    public int[] FrequencyCores { get; set; } = [];
    public int VCacheL3SizeMB { get; set; }
    public int StandardL3SizeMB { get; set; }
    public int TotalLogicalCores { get; set; }

    public string VCacheMaskHex => $"0x{VCacheMask.ToInt64():X4}";
    public string FrequencyMaskHex => $"0x{FrequencyMask.ToInt64():X4}";

    public int GetCcdIndex(int coreIndex)
    {
        return Array.Exists(VCacheCores, c => c == coreIndex) ? 0 : 1;
    }
}
