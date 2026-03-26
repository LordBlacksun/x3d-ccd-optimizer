namespace X3DCcdOptimizer.Models;

public enum AffinityAction
{
    Engaged,
    Migrated,
    Restored,
    Skipped,
    Error
}

public record AffinityEvent
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string ProcessName { get; init; } = "";
    public int Pid { get; init; }
    public AffinityAction Action { get; init; }
    public string Detail { get; init; } = "";
}
