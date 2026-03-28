using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.Config;

public class AutoDetectionConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("gpuThresholdPercent")]
    public int GpuThresholdPercent { get; set; } = 50;

    [JsonPropertyName("requireForeground")]
    public bool RequireForeground { get; set; } = true;

    [JsonPropertyName("detectionDelaySeconds")]
    public int DetectionDelaySeconds { get; set; } = 5;

    [JsonPropertyName("exitDelaySeconds")]
    public int ExitDelaySeconds { get; set; } = 10;
}

public class LoggingConfig
{
    [JsonPropertyName("level")]
    public string Level { get; set; } = "Information";

    [JsonPropertyName("maxSizeMb")]
    public int MaxSizeMb { get; set; } = 10;
}

public class UiConfig
{
    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = true;

    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; }

    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; }

    [JsonPropertyName("notifications")]
    public bool Notifications { get; set; } = true;

    [JsonPropertyName("windowPosition")]
    public int[]? WindowPosition { get; set; }

    [JsonPropertyName("windowSize")]
    public int[]? WindowSize { get; set; }
}

public class OverlayConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("autoHideSeconds")]
    public int AutoHideSeconds { get; set; } = 10;

    [JsonPropertyName("pixelShiftMinutes")]
    public int PixelShiftMinutes { get; set; } = 3;

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "Ctrl+Shift+O";

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 0.8;

    [JsonPropertyName("showLoadBars")]
    public bool ShowLoadBars { get; set; } = true;

    [JsonPropertyName("position")]
    public int[]? Position { get; set; }
}

public class CcdOverrideConfig
{
    [JsonPropertyName("vcacheCores")]
    public int[]? VCacheCores { get; set; }

    [JsonPropertyName("frequencyCores")]
    public int[]? FrequencyCores { get; set; }
}

public class AppConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "X3DCCDOptimizer");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("version")]
    public int Version { get; set; } = 3;

    [JsonPropertyName("operationMode")]
    public string OperationMode { get; set; } = "monitor";

    [JsonPropertyName("optimizeStrategy")]
    public string OptimizeStrategy { get; set; } = "affinityPinning";

    [JsonPropertyName("pollingIntervalMs")]
    public int PollingIntervalMs { get; set; } = 2000;

    [JsonPropertyName("dashboardRefreshMs")]
    public int DashboardRefreshMs { get; set; } = 1000;

    [JsonPropertyName("autoDetection")]
    public AutoDetectionConfig AutoDetection { get; set; } = new();

    [JsonPropertyName("manualGames")]
    public List<string> ManualGames { get; set; } =
    [
        "elitedangerous64.exe",
        "ffxiv_dx11.exe",
        "stellaris.exe",
        "re4.exe",
        "helldivers2.exe",
        "starcitizen.exe"
    ];

    [JsonPropertyName("excludedProcesses")]
    public List<string> ExcludedProcesses { get; set; } =
    [
        "chrome.exe",
        "firefox.exe",
        "msedge.exe",
        "obs64.exe",
        "obs.exe",
        "discord.exe",
        "spotify.exe",
        "devenv.exe",
        "explorer.exe",
        "dwm.exe",
        "vlc.exe",
        "mpc-hc64.exe",
        "photoshop.exe",
        "premiere pro.exe",
        "aftereffects.exe",
        "davinci resolve.exe",
        "blender.exe",
        "code.exe"
    ];

    [JsonPropertyName("backgroundApps")]
    public List<string> BackgroundApps { get; set; } = [];

    [JsonPropertyName("protectedProcesses")]
    public List<string> ProtectedProcesses { get; set; } =
    [
        "audiodg.exe",
        "svchost.exe"
    ];

    [JsonPropertyName("ccdOverride")]
    public CcdOverrideConfig? CcdOverride { get; set; }

    [JsonPropertyName("logging")]
    public LoggingConfig Logging { get; set; } = new();

    [JsonPropertyName("ui")]
    public UiConfig Ui { get; set; } = new();

    [JsonPropertyName("overlay")]
    public OverlayConfig Overlay { get; set; } = new();

    [JsonPropertyName("hasDismissedAdminDialog")]
    public bool HasDismissedAdminDialog { get; set; }

    /// <summary>True if no config.json existed at load time (first launch).</summary>
    [JsonIgnore]
    public bool IsFirstRun { get; private set; }

    public static AppConfig Load()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);

            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                    return config;
            }
        }
        catch (Exception ex)
        {
            // Corrupted config, permission error, etc. — fall back to defaults
            try { Serilog.Log.Warning(ex, "Failed to load config, using defaults"); } catch { }
        }

        var defaultConfig = CreateDefault();
        defaultConfig.IsFirstRun = true;
        defaultConfig.Save();
        return defaultConfig;
    }

    public void Save()
    {
        var tempPath = ConfigPath + ".tmp";
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, ConfigPath, overwrite: true);
        }
        catch (Exception ex)
        {
            try { File.Delete(tempPath); } catch { }
            try { Serilog.Log.Warning(ex, "Failed to save config"); } catch { }
        }
    }

    public void Validate()
    {
        PollingIntervalMs = Math.Clamp(PollingIntervalMs, 500, 30000);
        DashboardRefreshMs = Math.Clamp(DashboardRefreshMs, 500, 30000);
        AutoDetection.GpuThresholdPercent = Math.Clamp(AutoDetection.GpuThresholdPercent, 1, 100);
        AutoDetection.DetectionDelaySeconds = Math.Clamp(AutoDetection.DetectionDelaySeconds, 1, 60);
        AutoDetection.ExitDelaySeconds = Math.Clamp(AutoDetection.ExitDelaySeconds, 1, 120);
        Overlay.AutoHideSeconds = Math.Clamp(Overlay.AutoHideSeconds, 1, 300);
        Overlay.PixelShiftMinutes = Math.Clamp(Overlay.PixelShiftMinutes, 1, 60);
        Overlay.Opacity = Math.Clamp(Overlay.Opacity, 0.1, 1.0);

        if (CcdOverride is { VCacheCores: { } vc, FrequencyCores: { } fc })
        {
            if (vc.Any(c => c < 0 || c > 63) || fc.Any(c => c < 0 || c > 63))
            {
                try { Serilog.Log.Warning("CcdOverride contains out-of-range core indices — ignoring override"); } catch { }
                CcdOverride = null;
            }
        }
    }

    public Models.OperationMode GetOperationMode()
    {
        return string.Equals(OperationMode, "optimize", StringComparison.OrdinalIgnoreCase)
            ? Models.OperationMode.Optimize
            : Models.OperationMode.Monitor;
    }

    public Models.OptimizeStrategy GetOptimizeStrategy()
    {
        return string.Equals(OptimizeStrategy, "driverPreference", StringComparison.OrdinalIgnoreCase)
            ? Models.OptimizeStrategy.DriverPreference
            : Models.OptimizeStrategy.AffinityPinning;
    }

    private static AppConfig CreateDefault() => new();
}
