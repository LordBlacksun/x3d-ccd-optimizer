using System.Reflection;
using Xunit;
using X3DCcdInspector.Core;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.Tests;

/// <summary>
/// Tests for Phase 3 — SystemStateMonitor logic, SystemState model,
/// and CCD distribution determination.
/// Uses reflection to test private static helpers in SystemStateMonitor.
/// </summary>
public class SystemStateTests
{
    private static readonly Type MonitorType = typeof(SystemStateMonitor);

    // ── DetermineActiveCcd ───────────────────────────────────────

    [Fact]
    public void DetermineActiveCcd_BothZero_ReturnsUnknown()
    {
        Assert.Equal("Unknown", InvokeDetermineActiveCcd(0, 0));
    }

    [Fact]
    public void DetermineActiveCcd_AllOnCcd0_ReturnsCCD0()
    {
        Assert.Equal("CCD0", InvokeDetermineActiveCcd(10, 0));
    }

    [Fact]
    public void DetermineActiveCcd_AllOnCcd1_ReturnsCCD1()
    {
        Assert.Equal("CCD1", InvokeDetermineActiveCcd(0, 10));
    }

    [Fact]
    public void DetermineActiveCcd_MajorityCcd0_ReturnsCCD0()
    {
        // 8 out of 10 = 80% → CCD0
        Assert.Equal("CCD0", InvokeDetermineActiveCcd(8, 2));
    }

    [Fact]
    public void DetermineActiveCcd_MajorityCcd1_ReturnsCCD1()
    {
        // 2 out of 10 = 20% on CCD0 → CCD1
        Assert.Equal("CCD1", InvokeDetermineActiveCcd(2, 8));
    }

    [Fact]
    public void DetermineActiveCcd_EvenSplit_ReturnsBoth()
    {
        Assert.Equal("Both", InvokeDetermineActiveCcd(5, 5));
    }

    [Fact]
    public void DetermineActiveCcd_SlightMajority_ReturnsBoth()
    {
        // 6 out of 10 = 60% → still "Both" (threshold is 70%)
        Assert.Equal("Both", InvokeDetermineActiveCcd(6, 4));
    }

    [Fact]
    public void DetermineActiveCcd_AtThreshold70_ReturnsCCD0()
    {
        // 7 out of 10 = 70% → CCD0
        Assert.Equal("CCD0", InvokeDetermineActiveCcd(7, 3));
    }

    // ── DetermineGameMode ────────────────────────────────────────

    [Fact]
    public void DetermineGameMode_DriverNotInstalled_ReturnsUnknown()
    {
        Assert.Equal("Unknown", InvokeDetermineGameMode(null, true, false));
    }

    [Fact]
    public void DetermineGameMode_GameActive_PreferCache_ReturnsActive()
    {
        Assert.Equal("Active", InvokeDetermineGameMode(1, true, true));
    }

    [Fact]
    public void DetermineGameMode_GameActive_PreferFreq_ReturnsInactive()
    {
        Assert.Equal("Inactive", InvokeDetermineGameMode(0, true, true));
    }

    [Fact]
    public void DetermineGameMode_NoGame_ReturnsInactive()
    {
        Assert.Equal("Inactive", InvokeDetermineGameMode(1, false, true));
    }

    [Fact]
    public void DetermineGameMode_DriverInstalled_NullPref_ReturnsInactive()
    {
        Assert.Equal("Inactive", InvokeDetermineGameMode(null, false, true));
    }

    // ── CountBits ────────────────────────────────────────────────

    [Fact]
    public void CountBits_Zero_ReturnsZero()
    {
        Assert.Equal(0, InvokeCountBits(0L));
    }

    [Fact]
    public void CountBits_AllOnes16_Returns16()
    {
        Assert.Equal(16, InvokeCountBits(0xFFFFL));
    }

    [Fact]
    public void CountBits_AlternatingBits_ReturnsHalf()
    {
        // 0xAAAA = 1010_1010_1010_1010 = 8 bits
        Assert.Equal(8, InvokeCountBits(0xAAAAL));
    }

    [Fact]
    public void CountBits_SingleBit_ReturnsOne()
    {
        Assert.Equal(1, InvokeCountBits(1L));
        Assert.Equal(1, InvokeCountBits(0x8000L));
    }

    [Fact]
    public void CountBits_FullMask32_Returns32()
    {
        Assert.Equal(32, InvokeCountBits(0xFFFFFFFFL));
    }

    // ── SystemState record ───────────────────────────────────────

    [Fact]
    public void SystemState_DefaultValues()
    {
        var state = new SystemState();
        Assert.False(state.IsDriverInstalled);
        Assert.False(state.IsDriverServiceRunning);
        Assert.Null(state.DriverPreference);
        Assert.False(state.IsGameBarRunning);
        Assert.Equal("Unknown", state.GameModeStatus);
        Assert.False(state.IsGameForeground);
        Assert.Equal(0, state.Ccd0ThreadCount);
        Assert.Equal(0, state.Ccd1ThreadCount);
        Assert.Equal("Unknown", state.ActiveCcd);
    }

    [Fact]
    public void SystemState_WithInit_SetsAllFields()
    {
        var state = new SystemState
        {
            IsDriverInstalled = true,
            IsDriverServiceRunning = true,
            DriverPreference = 1,
            IsGameBarRunning = true,
            GameModeStatus = "Active",
            IsGameForeground = true,
            Ccd0ThreadCount = 10,
            Ccd1ThreadCount = 2,
            ActiveCcd = "CCD0"
        };

        Assert.True(state.IsDriverInstalled);
        Assert.True(state.IsDriverServiceRunning);
        Assert.Equal(1, state.DriverPreference);
        Assert.True(state.IsGameBarRunning);
        Assert.Equal("Active", state.GameModeStatus);
        Assert.True(state.IsGameForeground);
        Assert.Equal(10, state.Ccd0ThreadCount);
        Assert.Equal(2, state.Ccd1ThreadCount);
        Assert.Equal("CCD0", state.ActiveCcd);
    }

    [Fact]
    public void SystemState_RecordEquality()
    {
        var a = new SystemState { IsDriverInstalled = true, DriverPreference = 0 };
        var b = new SystemState { IsDriverInstalled = true, DriverPreference = 0 };
        Assert.Equal(a, b);
    }

    [Fact]
    public void SystemState_RecordInequality()
    {
        var a = new SystemState { DriverPreference = 0 };
        var b = new SystemState { DriverPreference = 1 };
        Assert.NotEqual(a, b);
    }

    // ── CpuTopology CCD mapping ──────────────────────────────────

    [Fact]
    public void CpuTopology_GetCcdIndex_DualCcd()
    {
        var topology = new CpuTopology
        {
            Tier = ProcessorTier.DualCcdX3D,
            VCacheCores = [0, 1, 2, 3, 4, 5, 6, 7],
            FrequencyCores = [8, 9, 10, 11, 12, 13, 14, 15]
        };

        Assert.Equal(0, topology.GetCcdIndex(0));
        Assert.Equal(0, topology.GetCcdIndex(7));
        Assert.Equal(1, topology.GetCcdIndex(8));
        Assert.Equal(1, topology.GetCcdIndex(15));
    }

    [Fact]
    public void CpuTopology_GetCcdIndex_SingleCcd_AlwaysZero()
    {
        var topology = new CpuTopology
        {
            Tier = ProcessorTier.SingleCcdX3D,
            VCacheCores = [0, 1, 2, 3, 4, 5, 6, 7]
        };

        Assert.Equal(0, topology.GetCcdIndex(0));
        Assert.Equal(0, topology.GetCcdIndex(7));
        Assert.Equal(0, topology.GetCcdIndex(15)); // Even out-of-range returns 0 for single CCD
    }

    // ── Reflection helpers ───────────────────────────────────────

    private static string InvokeDetermineActiveCcd(int ccd0, int ccd1)
    {
        var method = MonitorType.GetMethod("DetermineActiveCcd",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string)method!.Invoke(null, [ccd0, ccd1])!;
    }

    private static string InvokeDetermineGameMode(int? driverPref, bool gameActive, bool driverInstalled)
    {
        var method = MonitorType.GetMethod("DetermineGameMode",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string)method!.Invoke(null, [driverPref, gameActive, driverInstalled])!;
    }

    private static int InvokeCountBits(long value)
    {
        var method = MonitorType.GetMethod("CountBits",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (int)method!.Invoke(null, [value])!;
    }
}
