using Xunit;
using LiteDB;
using X3DCcdInspector.Core;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.Tests;

/// <summary>
/// Tests for Phase 5 — affinity pin fallback features:
/// protected process list, fallback pin LiteDB persistence,
/// profile name sanitization (shared with Phase 4).
/// Note: actual SetProcessAffinityMask calls require admin and
/// a running target process — not tested here.
/// </summary>
public class AffinityPinTests : IDisposable
{
    private readonly string _dbPath;
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<ScannedGame> _games;

    public AffinityPinTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"x3d_pin_test_{Guid.NewGuid():N}.db");
        _db = new LiteDatabase($"Filename={_dbPath};Connection=shared");
        _games = _db.GetCollection<ScannedGame>("games");
        _games.EnsureIndex(g => g.ProcessName);
    }

    public void Dispose()
    {
        _db?.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    // ── Protected process list ───────────────────────────────────

    [Theory]
    [InlineData("amd3dvcacheSvc")]
    [InlineData("amd3dvcacheUser")]
    [InlineData("GameBarPresenceWriter")]
    [InlineData("GameBar")]
    [InlineData("GameBarFTServer")]
    [InlineData("XboxGameBarWidgets")]
    [InlineData("gamingservices")]
    [InlineData("gamingservicesnet")]
    [InlineData("NVDisplay.Container")]
    [InlineData("atiesrxx")]
    [InlineData("atieclxx")]
    [InlineData("explorer")]
    public void SchedulingInfrastructure_IsProtected(string processName)
    {
        Assert.Contains(processName,
            AffinityManager.SchedulingInfrastructureProcesses);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("csrss")]
    [InlineData("lsass")]
    [InlineData("svchost")]
    [InlineData("dwm")]
    public void CriticalSystemProcess_IsDetected(string processName)
    {
        Assert.True(AffinityManager.IsCriticalSystemProcess(processName));
    }

    [Theory]
    [InlineData("ffxiv_dx11")]
    [InlineData("Cyberpunk2077")]
    [InlineData("elitedangerous64")]
    public void GameProcess_IsNotProtected(string processName)
    {
        // Games should not be in the scheduling infrastructure list
        Assert.DoesNotContain(processName,
            AffinityManager.SchedulingInfrastructureProcesses);
        Assert.False(AffinityManager.IsCriticalSystemProcess(processName));
    }

    [Fact]
    public void IsProtectedProcess_ChecksBothLists()
    {
        var topology = new CpuTopology
        {
            VCacheMask = new IntPtr(0xFFFF),
            FrequencyMask = new IntPtr(unchecked((int)0xFFFF0000)),
            VCacheCores = [0, 1, 2, 3, 4, 5, 6, 7],
            FrequencyCores = [8, 9, 10, 11, 12, 13, 14, 15]
        };
        var mgr = new AffinityManager(topology, []);

        Assert.True(mgr.IsProtectedProcess("explorer"));
        Assert.True(mgr.IsProtectedProcess("explorer.exe"));
        Assert.True(mgr.IsProtectedProcess("csrss"));
        Assert.True(mgr.IsProtectedProcess("amd3dvcacheSvc"));
        Assert.False(mgr.IsProtectedProcess("ffxiv_dx11"));
        Assert.False(mgr.IsProtectedProcess("game.exe"));

        mgr.Dispose();
    }

    [Fact]
    public void IsProtectedProcess_IncludesConfigProtected()
    {
        var topology = new CpuTopology
        {
            VCacheMask = new IntPtr(0xFFFF),
            FrequencyMask = new IntPtr(unchecked((int)0xFFFF0000)),
            VCacheCores = [0, 1, 2, 3, 4, 5, 6, 7],
            FrequencyCores = [8, 9, 10, 11, 12, 13, 14, 15]
        };
        var mgr = new AffinityManager(topology, ["audiodg.exe", "myprotected"]);

        Assert.True(mgr.IsProtectedProcess("audiodg"));
        Assert.True(mgr.IsProtectedProcess("audiodg.exe"));
        Assert.True(mgr.IsProtectedProcess("myprotected"));

        mgr.Dispose();
    }

    // ── Fallback CCD pin persistence ─────────────────────────────

    [Fact]
    public void NewGame_HasNoneFallbackPin()
    {
        var game = CreateGame("test.exe", "Test Game");
        _games.Insert(game);

        var loaded = _games.FindOne(g => g.ProcessName == "test.exe");
        Assert.Equal("None", loaded!.FallbackCcdPin);
    }

    [Fact]
    public void UpdateFallbackPin_PersistsVCache()
    {
        _games.Insert(CreateGame("game.exe", "My Game"));

        var game = _games.FindOne(g => g.ProcessName == "game.exe");
        game!.FallbackCcdPin = "VCache";
        _games.Update(game);

        var loaded = _games.FindOne(g => g.ProcessName == "game.exe");
        Assert.Equal("VCache", loaded!.FallbackCcdPin);
    }

    [Fact]
    public void UpdateFallbackPin_PersistsFrequency()
    {
        _games.Insert(CreateGame("game.exe", "My Game"));

        var game = _games.FindOne(g => g.ProcessName == "game.exe");
        game!.FallbackCcdPin = "Frequency";
        _games.Update(game);

        var loaded = _games.FindOne(g => g.ProcessName == "game.exe");
        Assert.Equal("Frequency", loaded!.FallbackCcdPin);
    }

    [Fact]
    public void UpdateFallbackPin_RevertToNone()
    {
        _games.Insert(CreateGame("game.exe", "My Game"));

        var game = _games.FindOne(g => g.ProcessName == "game.exe");
        game!.FallbackCcdPin = "VCache";
        _games.Update(game);

        game = _games.FindOne(g => g.ProcessName == "game.exe");
        game!.FallbackCcdPin = "None";
        _games.Update(game);

        var loaded = _games.FindOne(g => g.ProcessName == "game.exe");
        Assert.Equal("None", loaded!.FallbackCcdPin);
    }

    [Fact]
    public void ReplaceGames_PreservesFallbackPin()
    {
        var initial = CreateGame("game.exe", "My Game", "steam");
        initial.FallbackCcdPin = "VCache";
        _games.Insert(initial);

        // Simulate ReplaceGames logic
        var oldEntries = _games.Find(g => g.Source == "steam").ToList();
        var fallbackMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var old in oldEntries)
        {
            if (old.FallbackCcdPin != "None")
                fallbackMap.TryAdd(old.ProcessName, old.FallbackCcdPin);
        }

        _games.DeleteMany(g => g.Source == "steam");

        var fresh = CreateGame("game.exe", "My Game Updated", "steam");
        if (fallbackMap.TryGetValue(fresh.ProcessName, out var fb))
            fresh.FallbackCcdPin = fb;
        _games.Insert(fresh);

        var loaded = _games.FindOne(g => g.ProcessName == "game.exe");
        Assert.Equal("VCache", loaded!.FallbackCcdPin);
        Assert.Equal("My Game Updated", loaded.DisplayName);
    }

    [Fact]
    public void FallbackAndPreference_AreIndependent()
    {
        var game = CreateGame("game.exe", "My Game");
        game.CcdPreference = "VCache";
        game.FallbackCcdPin = "Frequency";
        _games.Insert(game);

        var loaded = _games.FindOne(g => g.ProcessName == "game.exe");
        Assert.Equal("VCache", loaded!.CcdPreference);
        Assert.Equal("Frequency", loaded.FallbackCcdPin);
    }

    // ── AffinityAction enum values ──────────────────────────────

    [Fact]
    public void AffinityAction_HasPinValues()
    {
        Assert.True(Enum.IsDefined(typeof(AffinityAction), AffinityAction.AffinityPinApplied));
        Assert.True(Enum.IsDefined(typeof(AffinityAction), AffinityAction.AffinityPinRestored));
    }

    private static ScannedGame CreateGame(string exe, string name, string source = "steam")
    {
        return new ScannedGame
        {
            ProcessName = exe,
            DisplayName = name,
            Source = source,
            FirstSeen = DateTime.UtcNow,
            LastSeen = DateTime.UtcNow
        };
    }
}
