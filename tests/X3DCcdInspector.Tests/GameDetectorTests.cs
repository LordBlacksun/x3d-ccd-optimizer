using Xunit;
using X3DCcdInspector.Core;

namespace X3DCcdInspector.Tests;

public class GameDetectorTests
{
    private static GameDetector CreateDetector(
        IEnumerable<string>? manualGames = null,
        IEnumerable<string>? excludedProcesses = null,
        Dictionary<string, string>? launcherGames = null,
        IEnumerable<string>? backgroundApps = null)
    {
        return new GameDetector(
            manualGames ?? [],
            excludedProcesses,
            launcherGames,
            backgroundApps);
    }

    // --- CheckGame: Manual list ---

    [Fact]
    public void CheckGame_ReturnsManual_ForManualListEntry()
    {
        var detector = CreateDetector(manualGames: ["mygame.exe"]);
        Assert.Equal(DetectionMethod.Manual, detector.CheckGame("mygame.exe"));
    }

    [Fact]
    public void CheckGame_ReturnsManual_WithoutExeSuffix()
    {
        var detector = CreateDetector(manualGames: ["mygame.exe"]);
        Assert.Equal(DetectionMethod.Manual, detector.CheckGame("mygame"));
    }

    [Fact]
    public void CheckGame_ReturnsManual_WhenManualListHasNoExeSuffix()
    {
        var detector = CreateDetector(manualGames: ["mygame"]);
        Assert.Equal(DetectionMethod.Manual, detector.CheckGame("mygame.exe"));
    }

    // --- CheckGame: Launcher scan ---

    [Fact]
    public void CheckGame_ReturnsLauncherScan_ForLauncherEntry()
    {
        var launcher = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["launchergame.exe"] = "Launcher Game"
        };
        var detector = CreateDetector(launcherGames: launcher);
        Assert.Equal(DetectionMethod.LibraryScan, detector.CheckGame("launchergame.exe"));
    }

    [Fact]
    public void CheckGame_ReturnsLauncherScan_WithoutExeSuffix()
    {
        var launcher = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["launchergame.exe"] = "Launcher Game"
        };
        var detector = CreateDetector(launcherGames: launcher);
        Assert.Equal(DetectionMethod.LibraryScan, detector.CheckGame("launchergame"));
    }

    // --- CheckGame: null for unknown ---

    [Fact]
    public void CheckGame_ReturnsNull_ForUnknownProcess()
    {
        var detector = CreateDetector(manualGames: ["mygame.exe"]);
        Assert.Null(detector.CheckGame("notepad.exe"));
    }

    // --- Priority: Manual > LibraryScan ---

    [Fact]
    public void CheckGame_ManualTakesPriority_OverLauncherScan()
    {
        var launcher = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["overlap.exe"] = "Launcher Name"
        };
        var detector = CreateDetector(
            manualGames: ["overlap.exe"],
            launcherGames: launcher);
        Assert.Equal(DetectionMethod.Manual, detector.CheckGame("overlap.exe"));
    }

    // --- Case-insensitive matching ---

    [Theory]
    [InlineData("MYGAME.EXE")]
    [InlineData("MyGame.Exe")]
    [InlineData("mygame.exe")]
    [InlineData("MYGAME")]
    [InlineData("MyGame")]
    public void CheckGame_IsCaseInsensitive(string processName)
    {
        var detector = CreateDetector(manualGames: ["mygame.exe"]);
        Assert.Equal(DetectionMethod.Manual, detector.CheckGame(processName));
    }

    // --- .exe suffix handling ---

    [Fact]
    public void CheckGame_WorksWithExeSuffix()
    {
        var detector = CreateDetector(manualGames: ["test.exe"]);
        Assert.Equal(DetectionMethod.Manual, detector.CheckGame("test.exe"));
    }

    [Fact]
    public void CheckGame_WorksWithoutExeSuffix()
    {
        var detector = CreateDetector(manualGames: ["test.exe"]);
        Assert.Equal(DetectionMethod.Manual, detector.CheckGame("test"));
    }

    // --- IsExcluded ---

    [Fact]
    public void IsExcluded_ReturnsTrue_ForExcludedProcess()
    {
        var detector = CreateDetector(excludedProcesses: ["chrome.exe"]);
        Assert.True(detector.IsExcluded("chrome.exe"));
    }

    [Fact]
    public void IsExcluded_ReturnsFalse_ForNonExcludedProcess()
    {
        var detector = CreateDetector(excludedProcesses: ["chrome.exe"]);
        Assert.False(detector.IsExcluded("mygame.exe"));
    }

    [Theory]
    [InlineData("CHROME.EXE")]
    [InlineData("Chrome.Exe")]
    [InlineData("chrome")]
    [InlineData("CHROME")]
    public void IsExcluded_IsCaseInsensitive(string processName)
    {
        var detector = CreateDetector(excludedProcesses: ["chrome.exe"]);
        Assert.True(detector.IsExcluded(processName));
    }

    [Fact]
    public void IsExcluded_WorksWithoutExeSuffix_InConfig()
    {
        var detector = CreateDetector(excludedProcesses: ["chrome"]);
        Assert.True(detector.IsExcluded("chrome.exe"));
    }

    // --- IsBackgroundApp ---

    [Fact]
    public void IsBackgroundApp_ReturnsTrue_ForBackgroundApp()
    {
        var detector = CreateDetector(backgroundApps: ["steamwebhelper.exe"]);
        Assert.True(detector.IsBackgroundApp("steamwebhelper.exe"));
    }

    [Fact]
    public void IsBackgroundApp_ReturnsFalse_ForNonBackgroundApp()
    {
        var detector = CreateDetector(backgroundApps: ["steamwebhelper.exe"]);
        Assert.False(detector.IsBackgroundApp("mygame.exe"));
    }

    [Fact]
    public void IsBackgroundApp_IsCaseInsensitive()
    {
        var detector = CreateDetector(backgroundApps: ["steamwebhelper.exe"]);
        Assert.True(detector.IsBackgroundApp("SteamWebHelper.EXE"));
    }

    // --- Background apps are never games ---

    [Fact]
    public void CheckGame_ReturnsNull_ForBackgroundApp_EvenIfInManualList()
    {
        var detector = CreateDetector(
            manualGames: ["steamwebhelper.exe"],
            backgroundApps: ["steamwebhelper.exe"]);
        Assert.Null(detector.CheckGame("steamwebhelper.exe"));
    }

    // --- GetDisplayName ---

    [Fact]
    public void GetDisplayName_ReturnsStrippedExeName_ForUnknownProcess()
    {
        var detector = CreateDetector();
        Assert.Equal("unknownprocess", detector.GetDisplayName("unknownprocess.exe"));
    }

    [Fact]
    public void GetDisplayName_ReturnsProcessName_WhenNoExeSuffix()
    {
        var detector = CreateDetector();
        Assert.Equal("unknownprocess", detector.GetDisplayName("unknownprocess"));
    }

    [Fact]
    public void GetDisplayName_ReturnsLauncherName_ForLauncherScannedGame()
    {
        var launcher = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mygame.exe"] = "My Awesome Game"
        };
        var detector = CreateDetector(launcherGames: launcher);
        Assert.Equal("My Awesome Game", detector.GetDisplayName("mygame.exe"));
    }

    // --- IsGame (legacy) ---

    [Fact]
    public void IsGame_ReturnsTrue_ForManualGame()
    {
        var detector = CreateDetector(manualGames: ["mygame.exe"]);
        Assert.True(detector.IsGame("mygame.exe"));
    }

    [Fact]
    public void IsGame_ReturnsFalse_ForUnknownProcess()
    {
        var detector = CreateDetector(manualGames: ["mygame.exe"]);
        Assert.False(detector.IsGame("notepad.exe"));
    }

    // --- UpdateLauncherGames ---

    [Fact]
    public void UpdateLauncherGames_ReplacesExistingDictionary()
    {
        var initial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["old.exe"] = "Old Game"
        };
        var detector = CreateDetector(launcherGames: initial);
        Assert.Equal(DetectionMethod.LibraryScan, detector.CheckGame("old.exe"));

        var updated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["new.exe"] = "New Game"
        };
        detector.UpdateLauncherGames(updated);

        Assert.Null(detector.CheckGame("old.exe"));
        Assert.Equal(DetectionMethod.LibraryScan, detector.CheckGame("new.exe"));
    }

    // --- GameCount ---

    [Fact]
    public void GameCount_ReflectsAllSources()
    {
        var launcher = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["launcher1.exe"] = "Launcher Game 1"
        };
        var detector = CreateDetector(
            manualGames: ["manual1.exe", "manual2.exe"],
            launcherGames: launcher);

        // GameCount = manual(2) + knownDB(loaded from file, may be 0 in test) + launcher(1)
        Assert.True(detector.GameCount >= 3);
    }

    // --- CurrentGame property ---

    [Fact]
    public void CurrentGame_DefaultsToNull()
    {
        var detector = CreateDetector();
        Assert.Null(detector.CurrentGame);
    }

    [Fact]
    public void CurrentGame_CanBeSetAndRetrieved()
    {
        var detector = CreateDetector();
        var game = new ProcessInfo { Name = "test.exe", Pid = 1234 };
        detector.CurrentGame = game;
        Assert.NotNull(detector.CurrentGame);
        Assert.Equal("test.exe", detector.CurrentGame.Name);
        Assert.Equal(1234, detector.CurrentGame.Pid);
    }
}
