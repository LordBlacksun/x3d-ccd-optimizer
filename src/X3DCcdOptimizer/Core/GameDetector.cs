using System.Diagnostics;

namespace X3DCcdOptimizer.Core;

public class GameDetector
{
    private readonly HashSet<string> _gameNames;

    public ProcessInfo? CurrentGame { get; set; }

    public GameDetector(IEnumerable<string> manualGames)
    {
        _gameNames = new HashSet<string>(manualGames, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsGame(string processName)
    {
        // Match with or without .exe extension
        if (_gameNames.Contains(processName))
            return true;
        if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return _gameNames.Contains(processName + ".exe");
        return false;
    }

    public int GameCount => _gameNames.Count;
}

public record ProcessInfo
{
    public string Name { get; init; } = "";
    public int Pid { get; init; }
    public string DetectionSource { get; init; } = "manual list";
}
