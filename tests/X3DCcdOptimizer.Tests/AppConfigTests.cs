using Xunit;
using X3DCcdOptimizer.Config;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.Tests;

public class AppConfigTests
{
    [Fact]
    public void Default_Version_Is3()
    {
        var config = new AppConfig();
        Assert.Equal(3, config.Version);
    }

    [Fact]
    public void Default_OperationMode_IsMonitor()
    {
        var config = new AppConfig();
        Assert.Equal("monitor", config.OperationMode);
    }

    [Fact]
    public void Default_PollingInterval_Is2000()
    {
        var config = new AppConfig();
        Assert.Equal(2000, config.PollingIntervalMs);
    }

    [Fact]
    public void Default_DashboardRefreshMs_Is1000()
    {
        var config = new AppConfig();
        Assert.Equal(1000, config.DashboardRefreshMs);
    }

    [Fact]
    public void Default_GpuThreshold_Is50()
    {
        var config = new AppConfig();
        Assert.Equal(50, config.AutoDetection.GpuThresholdPercent);
    }

    [Fact]
    public void Default_OptimizeStrategy_IsAffinityPinning()
    {
        var config = new AppConfig();
        Assert.Equal("affinityPinning", config.OptimizeStrategy);
    }

    [Fact]
    public void Default_AutoDetectionEnabled_IsTrue()
    {
        var config = new AppConfig();
        Assert.True(config.AutoDetection.Enabled);
    }

    [Fact]
    public void Default_DetectionDelaySeconds_Is5()
    {
        var config = new AppConfig();
        Assert.Equal(5, config.AutoDetection.DetectionDelaySeconds);
    }

    [Fact]
    public void Default_ExitDelaySeconds_Is10()
    {
        var config = new AppConfig();
        Assert.Equal(10, config.AutoDetection.ExitDelaySeconds);
    }

    [Fact]
    public void Default_RequireForeground_IsTrue()
    {
        var config = new AppConfig();
        Assert.True(config.AutoDetection.RequireForeground);
    }

    [Fact]
    public void Default_EnableArtworkDownload_IsFalse()
    {
        var config = new AppConfig();
        Assert.False(config.EnableArtworkDownload);
    }

    [Fact]
    public void Default_ManualGames_Has6Entries()
    {
        var config = new AppConfig();
        Assert.Equal(6, config.ManualGames.Count);
    }

    [Fact]
    public void Default_ManualGames_ContainsExpectedEntries()
    {
        var config = new AppConfig();
        Assert.Contains("elitedangerous64.exe", config.ManualGames);
        Assert.Contains("ffxiv_dx11.exe", config.ManualGames);
        Assert.Contains("stellaris.exe", config.ManualGames);
        Assert.Contains("re4.exe", config.ManualGames);
        Assert.Contains("helldivers2.exe", config.ManualGames);
        Assert.Contains("starcitizen.exe", config.ManualGames);
    }

    [Fact]
    public void Default_ExcludedProcesses_IsNotEmpty()
    {
        var config = new AppConfig();
        Assert.True(config.ExcludedProcesses.Count > 0);
    }

    [Fact]
    public void Default_ExcludedProcesses_ContainsBrowsers()
    {
        var config = new AppConfig();
        Assert.Contains("chrome.exe", config.ExcludedProcesses);
        Assert.Contains("firefox.exe", config.ExcludedProcesses);
        Assert.Contains("msedge.exe", config.ExcludedProcesses);
    }

    [Fact]
    public void Default_ProtectedProcesses_ContainsAudiodg()
    {
        var config = new AppConfig();
        Assert.Contains("audiodg.exe", config.ProtectedProcesses);
    }

    // --- Validate ---

    [Fact]
    public void Validate_ClampsPollingInterval_ToMinimum500()
    {
        var config = new AppConfig { PollingIntervalMs = 100 };
        config.Validate();
        Assert.Equal(500, config.PollingIntervalMs);
    }

    [Fact]
    public void Validate_ClampsPollingInterval_ToMaximum30000()
    {
        var config = new AppConfig { PollingIntervalMs = 99999 };
        config.Validate();
        Assert.Equal(30000, config.PollingIntervalMs);
    }

    [Fact]
    public void Validate_ClampsGpuThreshold_ToMinimum1()
    {
        var config = new AppConfig();
        config.AutoDetection.GpuThresholdPercent = 0;
        config.Validate();
        Assert.Equal(1, config.AutoDetection.GpuThresholdPercent);
    }

    [Fact]
    public void Validate_ClampsGpuThreshold_ToMaximum100()
    {
        var config = new AppConfig();
        config.AutoDetection.GpuThresholdPercent = 200;
        config.Validate();
        Assert.Equal(100, config.AutoDetection.GpuThresholdPercent);
    }

    [Fact]
    public void Validate_ClampsDetectionDelay_ToMinimum1()
    {
        var config = new AppConfig();
        config.AutoDetection.DetectionDelaySeconds = 0;
        config.Validate();
        Assert.Equal(1, config.AutoDetection.DetectionDelaySeconds);
    }

    [Fact]
    public void Validate_ClampsDetectionDelay_ToMaximum60()
    {
        var config = new AppConfig();
        config.AutoDetection.DetectionDelaySeconds = 999;
        config.Validate();
        Assert.Equal(60, config.AutoDetection.DetectionDelaySeconds);
    }

    [Fact]
    public void Validate_ClampsExitDelay_ToMinimum1()
    {
        var config = new AppConfig();
        config.AutoDetection.ExitDelaySeconds = 0;
        config.Validate();
        Assert.Equal(1, config.AutoDetection.ExitDelaySeconds);
    }

    [Fact]
    public void Validate_ClampsExitDelay_ToMaximum120()
    {
        var config = new AppConfig();
        config.AutoDetection.ExitDelaySeconds = 999;
        config.Validate();
        Assert.Equal(120, config.AutoDetection.ExitDelaySeconds);
    }

    [Fact]
    public void Validate_ClampsDashboardRefresh_ToMinimum500()
    {
        var config = new AppConfig { DashboardRefreshMs = 10 };
        config.Validate();
        Assert.Equal(500, config.DashboardRefreshMs);
    }

    [Fact]
    public void Validate_ClampsOverlayAutoHide_ToRange1To300()
    {
        var config = new AppConfig();
        config.Overlay.AutoHideSeconds = 0;
        config.Validate();
        Assert.Equal(1, config.Overlay.AutoHideSeconds);

        config.Overlay.AutoHideSeconds = 999;
        config.Validate();
        Assert.Equal(300, config.Overlay.AutoHideSeconds);
    }

    [Fact]
    public void Validate_ClampsOverlayPixelShift_ToRange1To60()
    {
        var config = new AppConfig();
        config.Overlay.PixelShiftMinutes = 0;
        config.Validate();
        Assert.Equal(1, config.Overlay.PixelShiftMinutes);

        config.Overlay.PixelShiftMinutes = 999;
        config.Validate();
        Assert.Equal(60, config.Overlay.PixelShiftMinutes);
    }

    [Fact]
    public void Validate_ClampsOverlayOpacity_ToRange01To10()
    {
        var config = new AppConfig();
        config.Overlay.Opacity = 0.0;
        config.Validate();
        Assert.Equal(0.1, config.Overlay.Opacity);

        config.Overlay.Opacity = 2.0;
        config.Validate();
        Assert.Equal(1.0, config.Overlay.Opacity);
    }

    [Fact]
    public void Validate_NullsCcdOverride_WithOutOfRangeCores()
    {
        var config = new AppConfig
        {
            CcdOverride = new CcdOverrideConfig
            {
                VCacheCores = [0, 1, 2, 99],  // 99 is > 63
                FrequencyCores = [8, 9]
            }
        };
        config.Validate();
        Assert.Null(config.CcdOverride);
    }

    [Fact]
    public void Validate_PreservesCcdOverride_WithValidCores()
    {
        var config = new AppConfig
        {
            CcdOverride = new CcdOverrideConfig
            {
                VCacheCores = [0, 1, 2, 3],
                FrequencyCores = [8, 9, 10, 11]
            }
        };
        config.Validate();
        Assert.NotNull(config.CcdOverride);
        Assert.Equal(new[] { 0, 1, 2, 3 }, config.CcdOverride.VCacheCores);
    }

    [Fact]
    public void Validate_AcceptsDefaultValues_WithoutChange()
    {
        var config = new AppConfig();
        config.Validate();
        Assert.Equal(2000, config.PollingIntervalMs);
        Assert.Equal(50, config.AutoDetection.GpuThresholdPercent);
        Assert.Equal(1000, config.DashboardRefreshMs);
    }

    // --- GetOperationMode ---

    [Fact]
    public void GetOperationMode_ReturnsMonitor_ForMonitorString()
    {
        var config = new AppConfig { OperationMode = "monitor" };
        Assert.Equal(OperationMode.Monitor, config.GetOperationMode());
    }

    [Fact]
    public void GetOperationMode_ReturnsOptimize_ForOptimizeString()
    {
        var config = new AppConfig { OperationMode = "optimize" };
        Assert.Equal(OperationMode.Optimize, config.GetOperationMode());
    }

    [Fact]
    public void GetOperationMode_ReturnsMonitor_ForInvalidString()
    {
        var config = new AppConfig { OperationMode = "garbage" };
        Assert.Equal(OperationMode.Monitor, config.GetOperationMode());
    }

    [Fact]
    public void GetOperationMode_IsCaseInsensitive()
    {
        var config = new AppConfig { OperationMode = "OPTIMIZE" };
        Assert.Equal(OperationMode.Optimize, config.GetOperationMode());

        config.OperationMode = "Monitor";
        Assert.Equal(OperationMode.Monitor, config.GetOperationMode());
    }

    // --- GetOptimizeStrategy ---

    [Fact]
    public void GetOptimizeStrategy_ReturnsAffinityPinning_ForAffinityPinningString()
    {
        var config = new AppConfig { OptimizeStrategy = "affinityPinning" };
        Assert.Equal(OptimizeStrategy.AffinityPinning, config.GetOptimizeStrategy());
    }

    [Fact]
    public void GetOptimizeStrategy_ReturnsDriverPreference_ForDriverPreferenceString()
    {
        var config = new AppConfig { OptimizeStrategy = "driverPreference" };
        Assert.Equal(OptimizeStrategy.DriverPreference, config.GetOptimizeStrategy());
    }

    [Fact]
    public void GetOptimizeStrategy_ReturnsAffinityPinning_ForInvalidString()
    {
        var config = new AppConfig { OptimizeStrategy = "invalid" };
        Assert.Equal(OptimizeStrategy.AffinityPinning, config.GetOptimizeStrategy());
    }

    [Fact]
    public void GetOptimizeStrategy_IsCaseInsensitive()
    {
        var config = new AppConfig { OptimizeStrategy = "DRIVERPREFERENCE" };
        Assert.Equal(OptimizeStrategy.DriverPreference, config.GetOptimizeStrategy());
    }

    // --- Overlay defaults ---

    [Fact]
    public void Default_OverlayEnabled_IsFalse()
    {
        var config = new AppConfig();
        Assert.False(config.Overlay.Enabled);
    }

    [Fact]
    public void Default_OverlayHotkey_IsCtrlShiftO()
    {
        var config = new AppConfig();
        Assert.Equal("Ctrl+Shift+O", config.Overlay.Hotkey);
    }

    [Fact]
    public void Default_OverlayOpacity_Is08()
    {
        var config = new AppConfig();
        Assert.Equal(0.8, config.Overlay.Opacity);
    }

    [Fact]
    public void Default_OverlayPosition_IsTopRight()
    {
        var config = new AppConfig();
        Assert.Equal("TopRight", config.Overlay.OverlayPosition);
    }

    // --- Logging defaults ---

    [Fact]
    public void Default_LoggingLevel_IsInformation()
    {
        var config = new AppConfig();
        Assert.Equal("Information", config.Logging.Level);
    }

    [Fact]
    public void Default_LoggingMaxSizeMb_Is10()
    {
        var config = new AppConfig();
        Assert.Equal(10, config.Logging.MaxSizeMb);
    }

    // --- UI defaults ---

    [Fact]
    public void Default_StartWithWindows_IsTrue()
    {
        var config = new AppConfig();
        Assert.True(config.Ui.StartWithWindows);
    }

    [Fact]
    public void Default_StartMinimized_IsFalse()
    {
        var config = new AppConfig();
        Assert.False(config.Ui.StartMinimized);
    }

    [Fact]
    public void Default_HasDismissedAdminDialog_IsFalse()
    {
        var config = new AppConfig();
        Assert.False(config.HasDismissedAdminDialog);
    }
}
