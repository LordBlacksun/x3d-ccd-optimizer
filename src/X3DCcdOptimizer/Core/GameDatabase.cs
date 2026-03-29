using System.IO;
using LiteDB;
using Serilog;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.Core;

/// <summary>
/// Persistent storage for scanned game library results using LiteDB.
/// Thread-safe for concurrent reads; writes are serialized by LiteDB.
/// </summary>
public class GameDatabase : IDisposable
{
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "X3DCCDOptimizer", "user_games.db");

    private readonly LiteDatabase _db;
    private readonly ILiteCollection<ScannedGame> _games;

    public GameDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        _db = new LiteDatabase($"Filename={DbPath};Connection=shared");
        _games = _db.GetCollection<ScannedGame>("games");
        _games.EnsureIndex(g => g.ProcessName);
        _games.EnsureIndex(g => g.Source);
    }

    /// <summary>
    /// Upserts scanned games. Matches on ProcessName — updates LastSeen and metadata,
    /// preserves FirstSeen and ArtworkPath from existing records.
    /// </summary>
    public void UpsertGames(IEnumerable<ScannedGame> games)
    {
        var now = DateTime.UtcNow;
        foreach (var game in games)
        {
            var existing = _games.FindOne(g => g.ProcessName == game.ProcessName && g.Source == game.Source);
            if (existing != null)
            {
                existing.DisplayName = game.DisplayName;
                existing.InstallPath = game.InstallPath;
                existing.SteamAppId = game.SteamAppId ?? existing.SteamAppId;
                existing.LastSeen = now;
                _games.Update(existing);
            }
            else
            {
                game.FirstSeen = now;
                game.LastSeen = now;
                _games.Insert(game);
            }
        }
    }

    public List<ScannedGame> GetAllGames() => _games.FindAll().ToList();

    public ScannedGame? FindByExe(string exeName) =>
        _games.FindOne(g => g.ProcessName == exeName);

    public void UpdateArtworkPath(int id, string path)
    {
        var game = _games.FindById(id);
        if (game != null)
        {
            game.ArtworkPath = path;
            _games.Update(game);
        }
    }

    /// <summary>
    /// Returns exe → displayName dictionary for GameDetector compatibility.
    /// </summary>
    public Dictionary<string, string> ToDictionary()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in _games.FindAll())
        {
            dict.TryAdd(game.ProcessName, game.DisplayName);
        }
        return dict;
    }

    /// <summary>
    /// Migrates from the old JSON cache format if it exists.
    /// </summary>
    public void MigrateFromJsonCache()
    {
        var cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "X3DCCDOptimizer", "installed_games.json");

        if (!File.Exists(cachePath)) return;
        if (_games.Count() > 0) return; // Already migrated

        try
        {
            var json = File.ReadAllText(cachePath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("games", out var arr)) return;

            var now = DateTime.UtcNow;
            foreach (var entry in arr.EnumerateArray())
            {
                var exe = entry.TryGetProperty("exe", out var e) ? e.GetString() : null;
                var name = entry.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (exe == null || name == null) continue;

                _games.Insert(new ScannedGame
                {
                    ProcessName = exe,
                    DisplayName = name,
                    Source = "launcher",
                    FirstSeen = now,
                    LastSeen = now
                });
            }

            Log.Information("Migrated {Count} games from JSON cache to LiteDB", _games.Count());
            File.Delete(cachePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to migrate JSON cache to LiteDB");
        }
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
