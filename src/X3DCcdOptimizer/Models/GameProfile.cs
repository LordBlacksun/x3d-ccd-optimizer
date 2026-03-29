using System.Text.Json.Serialization;

namespace X3DCcdOptimizer.Models;

/// <summary>
/// Per-game optimization strategy override. When a game with a matching ProcessName
/// is detected, the profile's strategy is used instead of the global strategy.
/// </summary>
public class GameProfile
{
    [JsonPropertyName("processName")]
    public string ProcessName { get; set; } = "";

    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = "global";
}
