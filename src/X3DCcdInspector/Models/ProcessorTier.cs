namespace X3DCcdOptimizer.Models;

public enum ProcessorTier
{
    DualCcdX3D,
    SingleCcdX3D,
    SingleCcdStandard,
    DualCcdStandard
}

public static class ProcessorTierExtensions
{
    /// <summary>
    /// Returns true for dual-CCD tiers that this application supports.
    /// Single-CCD processors don't have the CCD scheduling problem this tool solves.
    /// </summary>
    public static bool IsSupported(this ProcessorTier tier) =>
        tier is ProcessorTier.DualCcdX3D or ProcessorTier.DualCcdStandard;
}
