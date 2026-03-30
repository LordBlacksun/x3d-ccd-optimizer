namespace X3DCcdInspector.Models;

/// <summary>
/// Immutable snapshot of system state captured by SystemStateMonitor.
/// </summary>
public record SystemState
{
    public bool IsDriverInstalled { get; init; }
    public bool IsDriverServiceRunning { get; init; }
    public int? DriverPreference { get; init; }
    public bool IsGameBarRunning { get; init; }
    public string GameModeStatus { get; init; } = "Unknown";
    public bool IsGameForeground { get; init; }
    public int Ccd0ThreadCount { get; init; }
    public int Ccd1ThreadCount { get; init; }
    public string ActiveCcd { get; init; } = "Unknown";
}
