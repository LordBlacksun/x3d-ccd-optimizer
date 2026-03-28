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
}
