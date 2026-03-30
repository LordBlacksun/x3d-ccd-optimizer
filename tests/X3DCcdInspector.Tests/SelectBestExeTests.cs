using System.Reflection;
using Xunit;
using X3DCcdInspector.Core;

namespace X3DCcdInspector.Tests;

/// <summary>
/// Tests for GameLibraryScanner.SelectBestExe and ShouldSkipExe (both private static).
/// Uses reflection to invoke these methods with controlled temp directory structures.
/// </summary>
public class SelectBestExeTests : IDisposable
{
    private static readonly MethodInfo SelectBestExeMethod;
    private static readonly MethodInfo ShouldSkipExeMethod;
    private readonly List<string> _tempDirs = [];

    static SelectBestExeTests()
    {
        var type = typeof(GameLibraryScanner);
        SelectBestExeMethod = type.GetMethod("SelectBestExe",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        ShouldSkipExeMethod = type.GetMethod("ShouldSkipExe",
            BindingFlags.NonPublic | BindingFlags.Static)!;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"x3d_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static void CreateDummyExe(string dir, string name, int sizeKb = 100)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[sizeKb * 1024]);
    }

    private static string? InvokeSelectBestExe(string dir, string gameName)
    {
        return (string?)SelectBestExeMethod.Invoke(null, [dir, gameName]);
    }

    private static bool InvokeShouldSkipExe(string exeName)
    {
        return (bool)ShouldSkipExeMethod.Invoke(null, [exeName])!;
    }

    // --- ShouldSkipExe tests ---

    [Theory]
    [InlineData("uninstall.exe", true)]
    [InlineData("setup.exe", true)]
    [InlineData("install.exe", true)]
    [InlineData("redist.exe", true)]
    [InlineData("vcredist_x64.exe", true)]
    [InlineData("dxsetup.exe", true)]
    [InlineData("crashreporter.exe", true)]
    [InlineData("CrashReportClient.exe", true)]
    [InlineData("UnityCrashHandler64.exe", true)]
    public void ShouldSkipExe_ReturnsTrue_ForSkipPrefixes(string exeName, bool expected)
    {
        Assert.Equal(expected, InvokeShouldSkipExe(exeName));
    }

    [Theory]
    [InlineData("GameEditor.exe", true)]
    [InlineData("GameLauncher.exe", true)]
    [InlineData("AppCrash.exe", true)]
    [InlineData("MyReport.exe", true)]
    [InlineData("GameConfig.exe", true)]
    [InlineData("AutoUpdater.exe", true)]
    [InlineData("SteamHelper.exe", true)]
    [InlineData("SettingsTool.exe", true)]
    [InlineData("DedicatedServer.exe", true)]
    [InlineData("Benchmark.exe", true)]
    public void ShouldSkipExe_ReturnsTrue_ForSkipSuffixes(string exeName, bool expected)
    {
        Assert.Equal(expected, InvokeShouldSkipExe(exeName));
    }

    [Theory]
    [InlineData("MyGame.exe")]
    [InlineData("CoolRPG.exe")]
    [InlineData("shooter64.exe")]
    [InlineData("game.exe")]
    public void ShouldSkipExe_ReturnsFalse_ForValidGameExes(string exeName)
    {
        Assert.False(InvokeShouldSkipExe(exeName));
    }

    // --- SelectBestExe tests ---

    [Fact]
    public void SelectBestExe_SingleExe_ReturnsThatExe()
    {
        var dir = CreateTempDir();
        CreateDummyExe(dir, "OnlyGame.exe", 500);

        var result = InvokeSelectBestExe(dir, "Only Game");
        Assert.Equal("OnlyGame.exe", result);
    }

    [Fact]
    public void SelectBestExe_FiltersOutEditorSuffix()
    {
        var dir = CreateTempDir();
        CreateDummyExe(dir, "MyGame.exe", 500);
        CreateDummyExe(dir, "MyGameEditor.exe", 200);

        var result = InvokeSelectBestExe(dir, "My Game");
        Assert.Equal("MyGame.exe", result);
    }

    [Fact]
    public void SelectBestExe_FiltersOutLauncherSuffix()
    {
        var dir = CreateTempDir();
        CreateDummyExe(dir, "RealGame.exe", 500);
        CreateDummyExe(dir, "GameLauncher.exe", 200);

        var result = InvokeSelectBestExe(dir, "Real Game");
        Assert.Equal("RealGame.exe", result);
    }

    [Fact]
    public void SelectBestExe_FiltersOutSetupPrefix()
    {
        var dir = CreateTempDir();
        CreateDummyExe(dir, "RealGame.exe", 500);
        CreateDummyExe(dir, "setup.exe", 200);

        var result = InvokeSelectBestExe(dir, "Real Game");
        Assert.Equal("RealGame.exe", result);
    }

    [Fact]
    public void SelectBestExe_FiltersOutCrashReportPrefix()
    {
        var dir = CreateTempDir();
        CreateDummyExe(dir, "MyGame.exe", 500);
        CreateDummyExe(dir, "CrashReportClient.exe", 100);

        var result = InvokeSelectBestExe(dir, "My Game");
        Assert.Equal("MyGame.exe", result);
    }

    [Fact]
    public void SelectBestExe_NameMatch_ScoresHigherThanNonMatch()
    {
        var dir = CreateTempDir();
        CreateDummyExe(dir, "CoolGame.exe", 100);    // Name matches
        CreateDummyExe(dir, "BigBinary.exe", 5000);   // Larger but no name match

        var result = InvokeSelectBestExe(dir, "Cool Game");
        Assert.Equal("CoolGame.exe", result);
    }

    [Fact]
    public void SelectBestExe_RootExe_PreferredOverSubdirectory()
    {
        var dir = CreateTempDir();
        CreateDummyExe(dir, "game.exe", 500);
        CreateDummyExe(dir, Path.Combine("subfolder", "game2.exe"), 500);

        var result = InvokeSelectBestExe(dir, "game");
        Assert.Equal("game.exe", result);
    }

    [Fact]
    public void SelectBestExe_LargestExeWins_WhenNoNameMatch()
    {
        var dir = CreateTempDir();
        CreateDummyExe(dir, "small.exe", 100);
        CreateDummyExe(dir, "large.exe", 5000);

        // Neither matches the game name, so size wins
        var result = InvokeSelectBestExe(dir, "Completely Different Name");
        Assert.Equal("large.exe", result);
    }

    [Fact]
    public void SelectBestExe_EmptyDirectory_ReturnsNull()
    {
        var dir = CreateTempDir();
        var result = InvokeSelectBestExe(dir, "Any Game");
        Assert.Null(result);
    }

    [Fact]
    public void SelectBestExe_AllExesFiltered_ReturnsNull()
    {
        var dir = CreateTempDir();
        CreateDummyExe(dir, "setup.exe", 100);
        CreateDummyExe(dir, "uninstall.exe", 100);
        CreateDummyExe(dir, "CrashReportClient.exe", 100);

        var result = InvokeSelectBestExe(dir, "Some Game");
        Assert.Null(result);
    }

    [Fact]
    public void SelectBestExe_ExactNameMatch_WinsOverPartialMatch()
    {
        var dir = CreateTempDir();
        CreateDummyExe(dir, "mygame.exe", 100);
        CreateDummyExe(dir, "mygameplus.exe", 200);

        var result = InvokeSelectBestExe(dir, "My Game");
        Assert.Equal("mygame.exe", result);
    }

    [Fact]
    public void SelectBestExe_FolderNameMatch_IsConsidered()
    {
        // The folder name is used as a fallback matching heuristic
        var dir = CreateTempDir();
        var gameDir = Path.Combine(dir, "CoolGame");
        Directory.CreateDirectory(gameDir);
        CreateDummyExe(gameDir, "CoolGame.exe", 100);
        CreateDummyExe(gameDir, "other.exe", 200);

        var result = InvokeSelectBestExe(gameDir, "Something Else");
        // CoolGame.exe matches the folder name "CoolGame"
        Assert.Equal("CoolGame.exe", result);
    }
}
