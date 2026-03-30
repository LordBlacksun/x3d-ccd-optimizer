using Xunit;
using LiteDB;
using X3DCcdInspector.Core;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.Tests;

/// <summary>
/// Tests for Phase 4 — per-game CCD preference persistence in LiteDB
/// and VCacheDriverManager profile name sanitization.
/// Registry operations are not tested here (require admin + real driver).
/// </summary>
public class CcdPreferenceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<ScannedGame> _games;

    public CcdPreferenceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"x3d_pref_test_{Guid.NewGuid():N}.db");
        _db = new LiteDatabase($"Filename={_dbPath};Connection=shared");
        _games = _db.GetCollection<ScannedGame>("games");
        _games.EnsureIndex(g => g.ProcessName);
    }

    public void Dispose()
    {
        _db?.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    // ── CcdPreference default ────────────────────────────────────

    [Fact]
    public void NewGame_HasAutoPreference()
    {
        var game = CreateGame("test.exe", "Test Game");
        _games.Insert(game);

        var loaded = _games.FindOne(g => g.ProcessName == "test.exe");
        Assert.Equal("Auto", loaded!.CcdPreference);
    }

    // ── CcdPreference persistence ────────────────────────────────

    [Fact]
    public void UpdateCcdPreference_PersistsVCache()
    {
        _games.Insert(CreateGame("ffxiv_dx11.exe", "Final Fantasy XIV"));

        var game = _games.FindOne(g => g.ProcessName == "ffxiv_dx11.exe");
        game!.CcdPreference = "VCache";
        _games.Update(game);

        var loaded = _games.FindOne(g => g.ProcessName == "ffxiv_dx11.exe");
        Assert.Equal("VCache", loaded!.CcdPreference);
    }

    [Fact]
    public void UpdateCcdPreference_PersistsFrequency()
    {
        _games.Insert(CreateGame("stellaris.exe", "Stellaris"));

        var game = _games.FindOne(g => g.ProcessName == "stellaris.exe");
        game!.CcdPreference = "Frequency";
        _games.Update(game);

        var loaded = _games.FindOne(g => g.ProcessName == "stellaris.exe");
        Assert.Equal("Frequency", loaded!.CcdPreference);
    }

    [Fact]
    public void UpdateCcdPreference_RevertToAuto()
    {
        _games.Insert(CreateGame("game.exe", "My Game"));

        var game = _games.FindOne(g => g.ProcessName == "game.exe");
        game!.CcdPreference = "VCache";
        _games.Update(game);

        game = _games.FindOne(g => g.ProcessName == "game.exe");
        game!.CcdPreference = "Auto";
        _games.Update(game);

        var loaded = _games.FindOne(g => g.ProcessName == "game.exe");
        Assert.Equal("Auto", loaded!.CcdPreference);
    }

    // ── CcdPreference preserved on rescan ────────────────────────

    [Fact]
    public void ReplaceGames_PreservesCcdPreference()
    {
        // Insert initial game with VCache preference
        var initial = CreateGame("game.exe", "My Game", "steam");
        initial.CcdPreference = "VCache";
        _games.Insert(initial);

        // Simulate ReplaceGames: capture preferences, delete, re-insert
        var oldEntries = _games.Find(g => g.Source == "steam").ToList();
        var prefMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var old in oldEntries)
        {
            if (old.CcdPreference != "Auto")
                prefMap.TryAdd(old.ProcessName, old.CcdPreference);
        }

        _games.DeleteMany(g => g.Source == "steam");

        var fresh = CreateGame("game.exe", "My Game Updated", "steam");
        if (prefMap.TryGetValue(fresh.ProcessName, out var pref))
            fresh.CcdPreference = pref;
        _games.Insert(fresh);

        var loaded = _games.FindOne(g => g.ProcessName == "game.exe");
        Assert.NotNull(loaded);
        Assert.Equal("My Game Updated", loaded.DisplayName);
        Assert.Equal("VCache", loaded.CcdPreference);
    }

    [Fact]
    public void ReplaceGames_AutoPreferenceNotPreservedExplicitly()
    {
        // Auto is the default, so it shouldn't be in prefMap
        var initial = CreateGame("game.exe", "My Game", "steam");
        _games.Insert(initial);

        var oldEntries = _games.Find(g => g.Source == "steam").ToList();
        var prefMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var old in oldEntries)
        {
            if (old.CcdPreference != "Auto")
                prefMap.TryAdd(old.ProcessName, old.CcdPreference);
        }

        Assert.Empty(prefMap);
    }

    // ── GetGamesWithPreference ───────────────────────────────────

    [Fact]
    public void GetGamesWithPreference_ReturnsOnlyNonAuto()
    {
        _games.Insert(CreateGame("auto.exe", "Auto Game"));
        var vcache = CreateGame("vcache.exe", "VCache Game");
        vcache.CcdPreference = "VCache";
        _games.Insert(vcache);
        var freq = CreateGame("freq.exe", "Freq Game");
        freq.CcdPreference = "Frequency";
        _games.Insert(freq);

        var withPref = _games.Find(g => g.CcdPreference != "Auto").ToList();
        Assert.Equal(2, withPref.Count);
        Assert.Contains(withPref, g => g.ProcessName == "vcache.exe");
        Assert.Contains(withPref, g => g.ProcessName == "freq.exe");
    }

    // ── Profile name sanitization ────────────────────────────────

    [Fact]
    public void SanitizeProfileName_RemovesSpecialCharacters()
    {
        Assert.Equal("Final_Fantasy_XIV", VCacheDriverManager.SanitizeProfileName("Final Fantasy XIV"));
    }

    [Fact]
    public void SanitizeProfileName_RemovesColons()
    {
        Assert.Equal("Cyberpunk_2077_Phantom_Liberty",
            VCacheDriverManager.SanitizeProfileName("Cyberpunk 2077: Phantom Liberty"));
    }

    [Fact]
    public void SanitizeProfileName_PreservesLettersAndDigits()
    {
        Assert.Equal("Game123", VCacheDriverManager.SanitizeProfileName("Game123"));
    }

    [Fact]
    public void SanitizeProfileName_ReplacesHyphensWithUnderscores()
    {
        Assert.Equal("Half_Life_2", VCacheDriverManager.SanitizeProfileName("Half-Life 2"));
    }

    [Fact]
    public void SanitizeProfileName_HandlesEmptyString()
    {
        Assert.Equal("Unknown", VCacheDriverManager.SanitizeProfileName(""));
    }

    [Fact]
    public void SanitizeProfileName_HandlesAllSpecialChars()
    {
        Assert.Equal("Unknown", VCacheDriverManager.SanitizeProfileName("!!!@@@###"));
    }

    [Fact]
    public void SanitizeProfileName_HandlesUnicode()
    {
        // Japanese characters should be preserved (IsLetterOrDigit)
        var result = VCacheDriverManager.SanitizeProfileName("ファイナルファンタジー XIV");
        Assert.Contains("XIV", result);
    }

    // ── Multiple games CCD preference tracking ───────────────────

    [Fact]
    public void MultipleGames_IndependentPreferences()
    {
        var g1 = CreateGame("game1.exe", "Game 1");
        g1.CcdPreference = "VCache";
        _games.Insert(g1);

        var g2 = CreateGame("game2.exe", "Game 2");
        g2.CcdPreference = "Frequency";
        _games.Insert(g2);

        var g3 = CreateGame("game3.exe", "Game 3");
        _games.Insert(g3);

        var loaded1 = _games.FindOne(g => g.ProcessName == "game1.exe");
        var loaded2 = _games.FindOne(g => g.ProcessName == "game2.exe");
        var loaded3 = _games.FindOne(g => g.ProcessName == "game3.exe");

        Assert.Equal("VCache", loaded1!.CcdPreference);
        Assert.Equal("Frequency", loaded2!.CcdPreference);
        Assert.Equal("Auto", loaded3!.CcdPreference);
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
