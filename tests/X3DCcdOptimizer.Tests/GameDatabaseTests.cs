using Xunit;
using LiteDB;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.Tests;

/// <summary>
/// Tests for GameDatabase logic patterns using LiteDB directly.
/// Since GameDatabase hardcodes its path to %APPDATA%, we replicate the logic
/// with temp LiteDB files to avoid polluting user data.
/// </summary>
public class GameDatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<ScannedGame> _games;

    public GameDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"x3d_test_{Guid.NewGuid():N}.db");
        _db = new LiteDatabase($"Filename={_dbPath};Connection=shared");
        _games = _db.GetCollection<ScannedGame>("games");
        _games.EnsureIndex(g => g.ProcessName);
        _games.EnsureIndex(g => g.Source);
    }

    public void Dispose()
    {
        _db?.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void Constructor_CreatesDatabaseFile()
    {
        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public void Insert_AddsNewGame()
    {
        var game = CreateGame("test.exe", "Test Game", "steam");
        _games.Insert(game);

        var all = _games.FindAll().ToList();
        Assert.Single(all);
        Assert.Equal("test.exe", all[0].ProcessName);
    }

    [Fact]
    public void Upsert_InsertsNewGame()
    {
        var game = CreateGame("test.exe", "Test Game", "steam");
        var now = DateTime.UtcNow;
        game.FirstSeen = now;
        game.LastSeen = now;
        _games.Insert(game);

        var found = _games.FindOne(g => g.ProcessName == "test.exe");
        Assert.NotNull(found);
        Assert.Equal("Test Game", found.DisplayName);
    }

    [Fact]
    public void Upsert_UpdatesExistingGame_PreservesFirstSeen()
    {
        var now = DateTime.UtcNow;
        var firstSeen = now.AddDays(-7);
        var original = CreateGame("test.exe", "Test Game", "steam");
        original.FirstSeen = firstSeen;
        original.LastSeen = firstSeen;
        _games.Insert(original);

        // Simulate upsert logic from GameDatabase.UpsertGames
        var existing = _games.FindOne(g => g.ProcessName == "test.exe" && g.Source == "steam");
        Assert.NotNull(existing);
        existing.DisplayName = "Test Game Updated";
        existing.LastSeen = now;
        _games.Update(existing);

        var updated = _games.FindOne(g => g.ProcessName == "test.exe");
        Assert.NotNull(updated);
        Assert.Equal("Test Game Updated", updated.DisplayName);
        // LiteDB converts UTC to local on read, so compare using ToUniversalTime
        var timeDiff = Math.Abs((updated.FirstSeen.ToUniversalTime() - firstSeen.ToUniversalTime()).TotalSeconds);
        Assert.True(timeDiff < 1, $"FirstSeen should be preserved (diff: {timeDiff}s)");
        Assert.True(updated.LastSeen.ToUniversalTime() > updated.FirstSeen.ToUniversalTime());
    }

    [Fact]
    public void GetAllGames_ReturnsAllInserted()
    {
        _games.Insert(CreateGame("game1.exe", "Game 1", "steam"));
        _games.Insert(CreateGame("game2.exe", "Game 2", "epic"));
        _games.Insert(CreateGame("game3.exe", "Game 3", "gog"));

        var all = _games.FindAll().ToList();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void FindByExe_ReturnsMatchingGame()
    {
        _games.Insert(CreateGame("target.exe", "Target Game", "steam"));
        _games.Insert(CreateGame("other.exe", "Other Game", "epic"));

        var found = _games.FindOne(g => g.ProcessName == "target.exe");
        Assert.NotNull(found);
        Assert.Equal("Target Game", found.DisplayName);
    }

    [Fact]
    public void FindByExe_ReturnsNull_ForNonExistent()
    {
        _games.Insert(CreateGame("existing.exe", "Existing Game", "steam"));

        var found = _games.FindOne(g => g.ProcessName == "nonexistent.exe");
        Assert.Null(found);
    }

    [Fact]
    public void ToDictionary_ReturnsExeToNameMapping()
    {
        _games.Insert(CreateGame("game1.exe", "Game One", "steam"));
        _games.Insert(CreateGame("game2.exe", "Game Two", "epic"));

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in _games.FindAll())
        {
            dict.TryAdd(game.ProcessName, game.DisplayName);
        }

        Assert.Equal(2, dict.Count);
        Assert.Equal("Game One", dict["game1.exe"]);
        Assert.Equal("Game Two", dict["game2.exe"]);
    }

    [Fact]
    public void Deduplicate_RemovesDuplicatesWithSameNameAndSource()
    {
        _games.Insert(CreateGame("game.exe", "My Game", "steam", steamAppId: 100));
        _games.Insert(CreateGame("game2.exe", "My Game", "steam", steamAppId: 100));

        // Deduplicate logic
        var all = _games.FindAll().ToList();
        var groups = all.GroupBy(g => (
            Name: g.DisplayName.ToLowerInvariant(),
            g.Source
        )).Where(g => g.Count() > 1);

        int removed = 0;
        foreach (var group in groups)
        {
            var entries = group.ToList();
            var distinctAppIds = entries.Where(e => e.SteamAppId.HasValue)
                .Select(e => e.SteamAppId!.Value).Distinct().Count();
            if (distinctAppIds > 1) continue;

            var best = entries.OrderByDescending(e => e.SteamAppId.HasValue ? 1 : 0).First();
            foreach (var entry in entries.Where(e => e.Id != best.Id))
            {
                _games.Delete(entry.Id);
                removed++;
            }
        }

        Assert.Equal(1, removed);
        Assert.Single(_games.FindAll().ToList());
    }

    [Fact]
    public void Deduplicate_PreservesEntriesWithDifferentSteamAppIds()
    {
        _games.Insert(CreateGame("game.exe", "My Game", "steam", steamAppId: 100));
        _games.Insert(CreateGame("game2.exe", "My Game", "steam", steamAppId: 200));

        var all = _games.FindAll().ToList();
        var groups = all.GroupBy(g => (
            Name: g.DisplayName.ToLowerInvariant(),
            g.Source
        )).Where(g => g.Count() > 1);

        int removed = 0;
        foreach (var group in groups)
        {
            var entries = group.ToList();
            var distinctAppIds = entries.Where(e => e.SteamAppId.HasValue)
                .Select(e => e.SteamAppId!.Value).Distinct().Count();
            if (distinctAppIds > 1) continue; // This should trigger — skip dedup

            var best = entries.First();
            foreach (var entry in entries.Where(e => e.Id != best.Id))
            {
                _games.Delete(entry.Id);
                removed++;
            }
        }

        Assert.Equal(0, removed);
        Assert.Equal(2, _games.FindAll().ToList().Count);
    }

    [Fact]
    public void UpdateArtworkPath_UpdatesTheArtworkField()
    {
        var game = CreateGame("art.exe", "Art Game", "steam");
        _games.Insert(game);

        var inserted = _games.FindOne(g => g.ProcessName == "art.exe");
        Assert.NotNull(inserted);
        Assert.Null(inserted.ArtworkPath);

        inserted.ArtworkPath = "/path/to/artwork.jpg";
        _games.Update(inserted);

        var updated = _games.FindOne(g => g.ProcessName == "art.exe");
        Assert.Equal("/path/to/artwork.jpg", updated!.ArtworkPath);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"x3d_dispose_{Guid.NewGuid():N}.db");
        var db = new LiteDatabase($"Filename={tempPath};Connection=shared");
        var exception = Record.Exception(() => db.Dispose());
        Assert.Null(exception);
        try { File.Delete(tempPath); } catch { }
    }

    [Fact]
    public void MultipleInserts_GenerateUniqueIds()
    {
        var g1 = CreateGame("game1.exe", "Game 1", "steam");
        var g2 = CreateGame("game2.exe", "Game 2", "steam");
        _games.Insert(g1);
        _games.Insert(g2);

        var all = _games.FindAll().ToList();
        Assert.Equal(2, all.Count);
        Assert.NotEqual(all[0].Id, all[1].Id);
    }

    [Fact]
    public void Index_OnProcessName_AllowsFastLookup()
    {
        for (int i = 0; i < 100; i++)
            _games.Insert(CreateGame($"game{i}.exe", $"Game {i}", "steam"));

        var found = _games.FindOne(g => g.ProcessName == "game50.exe");
        Assert.NotNull(found);
        Assert.Equal("Game 50", found.DisplayName);
    }

    private static ScannedGame CreateGame(string exe, string name, string source, int? steamAppId = null)
    {
        return new ScannedGame
        {
            ProcessName = exe,
            DisplayName = name,
            Source = source,
            SteamAppId = steamAppId,
            FirstSeen = DateTime.UtcNow,
            LastSeen = DateTime.UtcNow
        };
    }
}
