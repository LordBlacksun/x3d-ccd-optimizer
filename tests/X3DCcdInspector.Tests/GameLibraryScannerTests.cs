using Xunit;
using X3DCcdInspector.Core;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.Tests;

public class GameLibraryScannerTests
{
    [Fact]
    public void ScanAll_ReturnsList_EvenIfNoLaunchersInstalled()
    {
        // On CI or machines without Steam/Epic/GOG, this should still succeed
        var result = GameLibraryScanner.ScanAll();
        Assert.NotNull(result);
        Assert.IsType<List<ScannedGame>>(result);
    }

    [Fact]
    public void ScanAll_ResultsHaveRequiredFields()
    {
        var result = GameLibraryScanner.ScanAll();
        foreach (var game in result)
        {
            Assert.False(string.IsNullOrEmpty(game.ProcessName), "ProcessName should not be empty");
            Assert.False(string.IsNullOrEmpty(game.DisplayName), "DisplayName should not be empty");
            Assert.False(string.IsNullOrEmpty(game.Source), "Source should not be empty");
            Assert.Contains(game.Source, new[] { "steam", "epic", "gog" });
        }
    }

    [Fact]
    public void ScanAll_NoProcessNameEndsWithSkipSuffix()
    {
        var result = GameLibraryScanner.ScanAll();
        var skipSuffixes = new[] { "Editor", "Launcher", "Crash", "Report", "Config",
            "Updater", "Helper", "Tool", "Server", "Benchmark", "Redistributable", "Shipping" };

        foreach (var game in result)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(game.ProcessName);
            foreach (var suffix in skipSuffixes)
            {
                Assert.False(
                    nameWithoutExt.EndsWith(suffix, StringComparison.OrdinalIgnoreCase),
                    $"Game '{game.ProcessName}' should have been filtered (suffix: {suffix})");
            }
        }
    }

    [Fact]
    public void ScanAll_NoProcessNameStartsWithSkipPrefix()
    {
        var result = GameLibraryScanner.ScanAll();
        var skipPrefixes = new[] { "unins", "uninstall", "setup", "install", "redist",
            "vcredist", "dxsetup", "crashreport", "crashhandl" };

        foreach (var game in result)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(game.ProcessName);
            foreach (var prefix in skipPrefixes)
            {
                Assert.False(
                    nameWithoutExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase),
                    $"Game '{game.ProcessName}' should have been filtered (prefix: {prefix})");
            }
        }
    }
}
