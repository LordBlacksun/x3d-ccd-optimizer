using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Serilog;

namespace X3DCcdOptimizer.Core;

public enum DetectionMethod
{
    Manual,
    Database,
    LauncherScan,
    Auto
}

public class GameDetector
{
    private readonly HashSet<string> _manualGames;
    private readonly Dictionary<string, string> _knownGames; // exe -> display name
    private volatile Dictionary<string, string> _launcherGames; // exe -> display name (from launcher scan)
    private readonly HashSet<string> _excludedProcesses;
    private readonly object _gameLock = new();
    private ProcessInfo? _currentGame;

    public ProcessInfo? CurrentGame
    {
        get { lock (_gameLock) return _currentGame; }
        set { lock (_gameLock) _currentGame = value; }
    }
    public int GameCount => _manualGames.Count + _knownGames.Count + _launcherGames.Count;

    public GameDetector(IEnumerable<string> manualGames, IEnumerable<string>? excludedProcesses = null,
        Dictionary<string, string>? launcherGames = null)
    {
        _manualGames = new HashSet<string>(manualGames, StringComparer.OrdinalIgnoreCase);
        _excludedProcesses = new HashSet<string>(
            excludedProcesses ?? [], StringComparer.OrdinalIgnoreCase);
        _knownGames = LoadKnownGames();
        _launcherGames = launcherGames ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_knownGames.Count > 0)
            Log.Information("Loaded {Count} games from known_games.json", _knownGames.Count);
        if (_launcherGames.Count > 0)
            Log.Information("Loaded {Count} games from launcher scan", _launcherGames.Count);
    }

    /// <summary>
    /// Replaces launcher-scanned games dictionary. Safe to call from a background thread.
    /// </summary>
    public void UpdateLauncherGames(Dictionary<string, string> games)
    {
        _launcherGames = games;
        Log.Information("Updated launcher-scanned games: {Count} entries", games.Count);
    }

    /// <summary>
    /// Checks manual list and known games DB. Returns detection method if matched, null if not.
    /// </summary>
    public DetectionMethod? CheckGame(string processName)
    {
        var nameWithExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName : processName + ".exe";
        var nameWithoutExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;

        // Priority 1: Manual list
        if (_manualGames.Contains(nameWithExe) || _manualGames.Contains(nameWithoutExe))
            return DetectionMethod.Manual;

        // Priority 2: Known games database
        if (_knownGames.ContainsKey(nameWithExe) || _knownGames.ContainsKey(nameWithoutExe))
            return DetectionMethod.Database;

        // Priority 3: Launcher-scanned games
        var launcher = _launcherGames;
        if (launcher.ContainsKey(nameWithExe) || launcher.ContainsKey(nameWithoutExe))
            return DetectionMethod.LauncherScan;

        return null;
    }

    /// <summary>
    /// Legacy compatibility — checks manual list and known DB.
    /// </summary>
    public bool IsGame(string processName) => CheckGame(processName) != null;

    public bool IsExcluded(string processName)
    {
        var nameWithExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName : processName + ".exe";
        var nameWithoutExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;

        return _excludedProcesses.Contains(nameWithExe) || _excludedProcesses.Contains(nameWithoutExe);
    }

    public string GetDisplayName(string processName)
    {
        var nameWithExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName : processName + ".exe";
        var nameWithoutExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;

        // Check known games DB first, then launcher scan
        if (_knownGames.TryGetValue(nameWithExe, out var name))
            return name;

        var launcher = _launcherGames;
        if (launcher.TryGetValue(nameWithExe, out name))
            return name;

        // Fallback: strip .exe extension for a cleaner display
        return nameWithoutExe;
    }

    private static Dictionary<string, string> LoadKnownGames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Look for known_games.json next to the assembly
            var dir = AppContext.BaseDirectory;
            var path = Path.Combine(dir, "Data", "known_games.json");

            if (!File.Exists(path))
            {
                // Also check working directory
                path = Path.Combine("Data", "known_games.json");
                if (!File.Exists(path))
                    return result;
            }

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("games", out var gamesArray))
            {
                foreach (var game in gamesArray.EnumerateArray())
                {
                    if (game.TryGetProperty("exe", out var exeEl) && game.TryGetProperty("name", out var nameEl))
                    {
                        var exe = exeEl.GetString();
                        var name = nameEl.GetString();
                        if (exe != null && name != null)
                            result[exe] = name;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load known_games.json");
        }

        return result;
    }
}

public record ProcessInfo
{
    public string Name { get; init; } = "";
    public string? DisplayName { get; init; }
    public int Pid { get; init; }
    public string DetectionSource { get; init; } = "manual list";
    public DetectionMethod Method { get; init; } = DetectionMethod.Manual;
    public float GpuUsage { get; init; }
}
