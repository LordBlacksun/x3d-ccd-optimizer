namespace X3DCcdInspector.Models;

/// <summary>
/// Represents a game discovered by scanning installed game launchers.
/// Stored in LiteDB at %APPDATA%\X3DCCDInspector\user_games.db.
/// </summary>
public class ScannedGame
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Source { get; set; } = "";        // "steam", "epic", "gog"
    public string? InstallPath { get; set; }
    public int? SteamAppId { get; set; }
    public string? ArtworkPath { get; set; }
    public string CcdPreference { get; set; } = "Auto"; // "Auto", "VCache", "Frequency"
    public string FallbackCcdPin { get; set; } = "None"; // "None", "VCache", "Frequency"
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}
