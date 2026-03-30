using Xunit;
using X3DCcdInspector.Core;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.Tests;

public class AffinityManagerTests
{
    private static CpuTopology CreateTestTopology() => new()
    {
        Tier = ProcessorTier.DualCcdX3D,
        VCacheMask = new IntPtr(0xFFFF),
        FrequencyMask = new IntPtr(0xFFFF0000),
        VCacheCores = [0, 1, 2, 3, 4, 5, 6, 7],
        FrequencyCores = [8, 9, 10, 11, 12, 13, 14, 15],
        TotalPhysicalCores = 16,
        TotalLogicalCores = 32
    };

    // --- SchedulingInfrastructureProcesses (pure data) ---

    [Theory]
    [InlineData("amd3dvcacheSvc")]
    [InlineData("amd3dvcacheUser")]
    [InlineData("GameBarPresenceWriter")]
    [InlineData("GameBar")]
    [InlineData("GameBarFTServer")]
    [InlineData("XboxGameBarWidgets")]
    [InlineData("gamingservices")]
    [InlineData("gamingservicesnet")]
    [InlineData("NVDisplay.Container")]
    [InlineData("atiesrxx")]
    [InlineData("atieclxx")]
    [InlineData("explorer")]
    public void SchedulingInfrastructureProcesses_ContainsExpectedEntry(string name)
    {
        Assert.Contains(name, AffinityManager.SchedulingInfrastructureProcesses);
    }

    [Fact]
    public void SchedulingInfrastructureProcesses_HasExactCount()
    {
        Assert.Equal(12, AffinityManager.SchedulingInfrastructureProcesses.Count);
    }

    [Fact]
    public void SchedulingInfrastructureProcesses_IsCaseInsensitive()
    {
        Assert.Contains("EXPLORER", AffinityManager.SchedulingInfrastructureProcesses);
        Assert.Contains("gamebar", AffinityManager.SchedulingInfrastructureProcesses);
        Assert.Contains("AMD3DVCACHESVC", AffinityManager.SchedulingInfrastructureProcesses);
    }

    // --- ProtectedProcesses.Names (pure data) ---

    [Theory]
    [InlineData("System")]
    [InlineData("csrss")]
    [InlineData("dwm")]
    [InlineData("audiodg")]
    [InlineData("svchost")]
    [InlineData("X3DCcdInspector")]
    public void ProtectedProcesses_ContainsExpectedEntry(string name)
    {
        Assert.Contains(name, ProtectedProcesses.Names);
    }

    [Fact]
    public void ProtectedProcesses_IsCaseInsensitive()
    {
        Assert.Contains("SYSTEM", ProtectedProcesses.Names);
        Assert.Contains("DWM", ProtectedProcesses.Names);
    }

    // --- IsCriticalSystemProcess (pure static) ---

    [Theory]
    [InlineData("System", true)]
    [InlineData("csrss", true)]
    [InlineData("dwm", true)]
    [InlineData("svchost", true)]
    [InlineData("taskhostw", true)]
    [InlineData("notepad", false)]
    [InlineData("chrome", false)]
    public void IsCriticalSystemProcess_ReturnsExpected(string name, bool expected)
    {
        Assert.Equal(expected, AffinityManager.IsCriticalSystemProcess(name));
    }

    // --- Game tracking ---

    [Fact]
    public void OnGameDetected_EmitsGameDetectedEvent()
    {
        var manager = new AffinityManager(CreateTestTopology(), configProtected: []);

        var events = new List<AffinityEvent>();
        manager.AffinityChanged += e => events.Add(e);

        var game = new ProcessInfo
        {
            Name = "testgame.exe",
            Pid = 99999,
            DetectionSource = "test"
        };

        manager.OnGameDetected(game);

        Assert.Single(events);
        Assert.Equal(AffinityAction.GameDetected, events[0].Action);
        Assert.Equal("testgame.exe", events[0].ProcessName);
    }

    [Fact]
    public void OnGameExited_EmitsGameExitedEvent()
    {
        var manager = new AffinityManager(CreateTestTopology(), configProtected: []);

        var game = new ProcessInfo
        {
            Name = "testgame.exe",
            Pid = 99999,
            DetectionSource = "test"
        };

        manager.OnGameDetected(game);

        var events = new List<AffinityEvent>();
        manager.AffinityChanged += e => events.Add(e);

        manager.OnGameExited(game);

        Assert.Single(events);
        Assert.Equal(AffinityAction.GameExited, events[0].Action);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var manager = new AffinityManager(CreateTestTopology(), configProtected: []);
        manager.Dispose();
        manager.Dispose();
    }
}
