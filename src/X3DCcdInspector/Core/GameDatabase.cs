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
    /// Replaces all games for the given sources with fresh scan results.
    /// Preserves ArtworkPath from previous entries where the exe matches.
    /// </summary>
    public void ReplaceGames(IEnumerable<ScannedGame> games)
    {
        var now = DateTime.UtcNow;
        var grouped = games.GroupBy(g => g.Source).ToList();

        foreach (var group in grouped)
        {
            // Capture key — LiteDB can't translate group.Key in LINQ expressions
            var source = group.Key;

            // Preserve artwork paths from old entries
            var oldEntries = _games.Find(g => g.Source == source).ToList();
            var artworkMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var old in oldEntries)
            {
                if (!string.IsNullOrEmpty(old.ArtworkPath))
                    artworkMap.TryAdd(old.ProcessName, old.ArtworkPath);
            }

            // Wipe old entries for this source
            _games.DeleteMany(g => g.Source == source);

            // Insert fresh
            foreach (var game in group)
            {
                game.FirstSeen = now;
                game.LastSeen = now;
                if (artworkMap.TryGetValue(game.ProcessName, out var art))
                    game.ArtworkPath = art;
                _games.Insert(game);
            }

            Log.Information("Replaced {Count} {Source} entries in game database", group.Count(), group.Key);
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
    /// Removes duplicate entries — keeps one entry per DisplayName+Source combination.
    /// Prefers entries whose ProcessName best matches the DisplayName.
    /// </summary>
    public void Deduplicate()
    {
        var all = _games.FindAll().ToList();
        var groups = all.GroupBy(g => (
            Name: g.DisplayName.ToLowerInvariant(),
            g.Source
        )).Where(g => g.Count() > 1);

        int removed = 0;
        foreach (var group in groups)
        {
            // Keep the entry whose exe name best matches the display name, or largest SteamAppId (legitimately different apps)
            var entries = group.ToList();

            // If entries have different SteamAppIds, they're legitimately different apps — skip
            var distinctAppIds = entries.Where(e => e.SteamAppId.HasValue).Select(e => e.SteamAppId!.Value).Distinct().Count();
            if (distinctAppIds > 1) continue;

            var normalizedName = NormalizeName(group.Key.Name);
            var best = entries
                .OrderByDescending(e => NameMatchScore(NormalizeName(Path.GetFileNameWithoutExtension(e.ProcessName)), normalizedName))
                .ThenByDescending(e => e.SteamAppId.HasValue ? 1 : 0)
                .First();

            foreach (var entry in entries.Where(e => e.Id != best.Id))
            {
                _games.Delete(entry.Id);
                removed++;
            }
        }

        if (removed > 0)
            Log.Information("Deduplicated game library: removed {Count} duplicate entries", removed);
    }

    private static string NormalizeName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static int NameMatchScore(string exeNorm, string gameNorm)
    {
        if (exeNorm == gameNorm) return 100;
        if (exeNorm.Contains(gameNorm) || gameNorm.Contains(exeNorm)) return 50;
        return 0;
    }

    /// <summary>
    /// Removes legacy entries with source="launcher" from the old JSON cache migration.
    /// These are stale data — replaced by per-source scanning (steam/epic/gog).
    /// </summary>
    public void PurgeLegacyEntries()
    {
        var removed = _games.DeleteMany(g => g.Source == "launcher");
        if (removed > 0)
            Log.Information("Purged {Count} legacy 'launcher' entries from game database", removed);
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
