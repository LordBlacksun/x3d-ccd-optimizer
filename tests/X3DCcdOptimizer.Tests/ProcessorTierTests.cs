using Xunit;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.Tests;

public class ProcessorTierTests
{
    [Fact]
    public void ProcessorTier_HasExactlyFourValues()
    {
        var values = Enum.GetValues<ProcessorTier>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(ProcessorTier.DualCcdX3D)]
    [InlineData(ProcessorTier.SingleCcdX3D)]
    [InlineData(ProcessorTier.SingleCcdStandard)]
    [InlineData(ProcessorTier.DualCcdStandard)]
    public void ProcessorTier_ContainsExpectedValue(ProcessorTier tier)
    {
        Assert.True(Enum.IsDefined(tier));
    }

    [Fact]
    public void DualCcdX3D_IsSupported_ReturnsTrue()
    {
        Assert.True(ProcessorTier.DualCcdX3D.IsSupported());
    }

    [Fact]
    public void DualCcdStandard_IsSupported_ReturnsTrue()
    {
        Assert.True(ProcessorTier.DualCcdStandard.IsSupported());
    }

    [Fact]
    public void SingleCcdX3D_IsSupported_ReturnsFalse()
    {
        Assert.False(ProcessorTier.SingleCcdX3D.IsSupported());
    }

    [Fact]
    public void SingleCcdStandard_IsSupported_ReturnsFalse()
    {
        Assert.False(ProcessorTier.SingleCcdStandard.IsSupported());
    }

    [Theory]
    [InlineData(ProcessorTier.DualCcdX3D, true)]
    [InlineData(ProcessorTier.DualCcdStandard, true)]
    [InlineData(ProcessorTier.SingleCcdX3D, false)]
    [InlineData(ProcessorTier.SingleCcdStandard, false)]
    public void CpuTopology_IsSupported_MatchesTierIsSupported(ProcessorTier tier, bool expected)
    {
        var topology = new CpuTopology { Tier = tier };
        Assert.Equal(expected, topology.IsSupported);
        Assert.Equal(expected, tier.IsSupported());
    }

    [Theory]
    [InlineData(ProcessorTier.DualCcdX3D, false)]
    [InlineData(ProcessorTier.DualCcdStandard, false)]
    [InlineData(ProcessorTier.SingleCcdX3D, true)]
    [InlineData(ProcessorTier.SingleCcdStandard, true)]
    public void CpuTopology_IsSingleCcd_CorrectForTier(ProcessorTier tier, bool expected)
    {
        var topology = new CpuTopology { Tier = tier };
        Assert.Equal(expected, topology.IsSingleCcd);
    }

    [Theory]
    [InlineData(ProcessorTier.DualCcdX3D, true)]
    [InlineData(ProcessorTier.DualCcdStandard, true)]
    [InlineData(ProcessorTier.SingleCcdX3D, false)]
    [InlineData(ProcessorTier.SingleCcdStandard, false)]
    public void CpuTopology_IsDualCcd_CorrectForTier(ProcessorTier tier, bool expected)
    {
        var topology = new CpuTopology { Tier = tier };
        Assert.Equal(expected, topology.IsDualCcd);
    }

    [Fact]
    public void CpuTopology_HasVCache_TrueWhenVCacheL3IsLargeEnough()
    {
        var topology = new CpuTopology
        {
            VCacheL3SizeMB = 96,
            StandardL3SizeMB = 32
        };
        Assert.True(topology.HasVCache);
    }

    [Fact]
    public void CpuTopology_HasVCache_FalseWhenBothEqual()
    {
        var topology = new CpuTopology
        {
            VCacheL3SizeMB = 32,
            StandardL3SizeMB = 32
        };
        Assert.False(topology.HasVCache);
    }

    [Fact]
    public void CpuTopology_GetCcdIndex_SingleCcd_AlwaysZero()
    {
        var topology = new CpuTopology
        {
            Tier = ProcessorTier.SingleCcdX3D,
            VCacheCores = [0, 1, 2, 3]
        };
        Assert.Equal(0, topology.GetCcdIndex(0));
        Assert.Equal(0, topology.GetCcdIndex(5));
        Assert.Equal(0, topology.GetCcdIndex(99));
    }

    [Fact]
    public void CpuTopology_GetCcdIndex_DualCcd_ReturnsCorrectCcd()
    {
        var topology = new CpuTopology
        {
            Tier = ProcessorTier.DualCcdX3D,
            VCacheCores = [0, 1, 2, 3, 4, 5, 6, 7]
        };
        // VCache cores are CCD 0
        Assert.Equal(0, topology.GetCcdIndex(0));
        Assert.Equal(0, topology.GetCcdIndex(7));
        // Non-VCache cores are CCD 1
        Assert.Equal(1, topology.GetCcdIndex(8));
        Assert.Equal(1, topology.GetCcdIndex(15));
    }

    [Fact]
    public void CpuTopology_VCacheMaskHex_FormatsCorrectly()
    {
        var topology = new CpuTopology { VCacheMask = new IntPtr(0xFF) };
        Assert.Equal("0x00FF", topology.VCacheMaskHex);
    }

    [Fact]
    public void CpuTopology_FrequencyMaskHex_ReturnsNA_WhenZero()
    {
        var topology = new CpuTopology { FrequencyMask = IntPtr.Zero };
        Assert.Equal("N/A", topology.FrequencyMaskHex);
    }

    [Fact]
    public void CpuTopology_FrequencyMaskHex_FormatsCorrectly_WhenNonZero()
    {
        var topology = new CpuTopology { FrequencyMask = new IntPtr(0xFF00) };
        Assert.Equal("0xFF00", topology.FrequencyMaskHex);
    }
}
