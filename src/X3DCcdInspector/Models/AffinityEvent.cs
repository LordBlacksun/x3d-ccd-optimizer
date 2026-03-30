namespace X3DCcdInspector.Models;

public enum AffinityAction
{
    GameDetected,
    GameExited,
    Error,
    DetectionSkipped,
    DriverStateChanged,
    GameBarStatus,
    CcdObservation,
    CcdPreferenceSet,
    CcdPreferenceRemoved,
    AffinityPinApplied,
    AffinityPinRestored
}

public record AffinityEvent
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string ProcessName { get; init; } = "";
    public string? DisplayName { get; init; }
    public int Pid { get; init; }
    public AffinityAction Action { get; init; }
    public string Detail { get; init; } = "";
}
