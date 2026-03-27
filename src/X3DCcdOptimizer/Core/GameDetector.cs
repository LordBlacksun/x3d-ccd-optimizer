using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Serilog;

namespace X3DCcdOptimizer.Core;

public enum DetectionMethod
{
    Manual,
    Database,
    Auto
}

public class GameDetector
{
    private readonly HashSet<string> _manualGames;
    private readonly Dictionary<string, string> _knownGames; // exe -> display name
    private readonly HashSet<string> _excludedProcesses;
    private readonly object _gameLock = new();
    private ProcessInfo? _currentGame;

    public ProcessInfo? CurrentGame
    {
        get { lock (_gameLock) return _currentGame; }
        set { lock (_gameLock) _currentGame = value; }
    }
    public int GameCount => _manualGames.Count + _knownGames.Count;

    public GameDetector(IEnumerable<string> manualGames, IEnumerable<string>? excludedProcesses = null)
    {
        _manualGames = new HashSet<string>(manualGames, StringComparer.OrdinalIgnoreCase);
        _excludedProcesses = new HashSet<string>(
            excludedProcesses ?? [], StringComparer.OrdinalIgnoreCase);
        _knownGames = LoadKnownGames();

        if (_knownGames.Count > 0)
            Log.Information("Loaded {Count} games from known_games.json", _knownGames.Count);
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
        return _knownGames.TryGetValue(nameWithExe, out var name) ? name : processName;
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
                    var exe = game.GetProperty("exe").GetString();
                    var name = game.GetProperty("name").GetString();
                    if (exe != null && name != null)
                        result[exe] = name;
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
    public int Pid { get; init; }
    public string DetectionSource { get; init; } = "manual list";
    public DetectionMethod Method { get; init; } = DetectionMethod.Manual;
    public float GpuUsage { get; init; }
}
