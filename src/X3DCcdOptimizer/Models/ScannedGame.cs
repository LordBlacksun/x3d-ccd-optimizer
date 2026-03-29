namespace X3DCcdOptimizer.Models;

/// <summary>
/// Represents a game discovered by scanning installed game launchers.
/// Stored in LiteDB at %APPDATA%\X3DCCDOptimizer\user_games.db.
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
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}
