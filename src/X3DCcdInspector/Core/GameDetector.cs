using System.IO;
using Serilog;

namespace X3DCcdInspector.Core;

public enum DetectionMethod
{
    Manual,
    LibraryScan,
    Auto
}

public class GameDetector
{
    private readonly HashSet<string> _manualGames;
    private volatile Dictionary<string, string> _launcherGames; // exe -> display name (from library scan)
    private readonly HashSet<string> _excludedProcesses;
    private readonly HashSet<string> _backgroundApps;
    private readonly object _gameLock = new();
    private ProcessInfo? _currentGame;

    public ProcessInfo? CurrentGame
    {
        get { lock (_gameLock) return _currentGame; }
        set { lock (_gameLock) _currentGame = value; }
    }
    public int GameCount => _manualGames.Count + _launcherGames.Count;

    public GameDetector(IEnumerable<string> manualGames, IEnumerable<string>? excludedProcesses = null,
        Dictionary<string, string>? launcherGames = null, IEnumerable<string>? backgroundApps = null)
    {
        _manualGames = new HashSet<string>(manualGames, StringComparer.OrdinalIgnoreCase);
        _excludedProcesses = new HashSet<string>(
            excludedProcesses ?? [], StringComparer.OrdinalIgnoreCase);
        _backgroundApps = new HashSet<string>(
            backgroundApps ?? [], StringComparer.OrdinalIgnoreCase);
        _launcherGames = launcherGames ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_launcherGames.Count > 0)
            Log.Information("Loaded {Count} games from library scan", _launcherGames.Count);
    }

    /// <summary>
    /// Replaces library-scanned games dictionary. Safe to call from a background thread.
    /// </summary>
    public void UpdateLauncherGames(Dictionary<string, string> games)
    {
        _launcherGames = games;
        Log.Information("Updated library-scanned games: {Count} entries", games.Count);
    }

    /// <summary>
    /// Checks manual rules and library-scanned games. Returns detection method if matched, null if not.
    /// Detection priority: Manual rules -> Library scan -> null (GPU heuristic handled by ProcessWatcher).
    /// </summary>
    public DetectionMethod? CheckGame(string processName)
    {
        // Background apps are never games
        if (IsBackgroundApp(processName))
            return null;

        var nameWithExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName : processName + ".exe";
        var nameWithoutExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;

        // Priority 1: Manual rules
        if (_manualGames.Contains(nameWithExe) || _manualGames.Contains(nameWithoutExe))
            return DetectionMethod.Manual;

        // Priority 2: Library-scanned games (Steam/Epic/GOG from LiteDB)
        var launcher = _launcherGames;
        if (launcher.ContainsKey(nameWithExe) || launcher.ContainsKey(nameWithoutExe))
            return DetectionMethod.LibraryScan;

        return null;
    }

    public bool IsGame(string processName) => CheckGame(processName) != null;

    public bool IsExcluded(string processName)
    {
        var nameWithExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName : processName + ".exe";
        var nameWithoutExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;

        return _excludedProcesses.Contains(nameWithExe) || _excludedProcesses.Contains(nameWithoutExe);
    }

    public bool IsBackgroundApp(string processName)
    {
        var nameWithExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName : processName + ".exe";
        var nameWithoutExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;

        return _backgroundApps.Contains(nameWithExe) || _backgroundApps.Contains(nameWithoutExe);
    }

    public string GetDisplayName(string processName)
    {
        var nameWithExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName : processName + ".exe";
        var nameWithoutExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;

        var launcher = _launcherGames;
        if (launcher.TryGetValue(nameWithExe, out var name))
            return name;

        return nameWithoutExe;
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
