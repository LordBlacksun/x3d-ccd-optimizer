using System.Text.Json.Serialization;

namespace X3DCcdOptimizer.Models;

public class RecoveryState
{
    [JsonPropertyName("engaged")]
    public bool Engaged { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("gameProcess")]
    public string GameProcess { get; set; } = "";

    [JsonPropertyName("gamePid")]
    public int GamePid { get; set; }

    [JsonPropertyName("modifiedProcesses")]
    public List<RecoveryProcessEntry> ModifiedProcesses { get; set; } = [];
}

public class RecoveryProcessEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("originalMask")]
    public string OriginalMask { get; set; } = "";
}
