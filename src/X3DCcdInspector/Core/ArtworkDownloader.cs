using System.IO;
using System.Net.Http;
using Serilog;

namespace X3DCcdOptimizer.Core;

/// <summary>
/// Downloads game box art from Steam's public CDN.
/// Only active when user explicitly enables artwork downloads in Settings.
/// </summary>
public static class ArtworkDownloader
{
    private static readonly string ArtworkDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "X3DCCDOptimizer", "artwork");

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>
    /// Downloads artwork for all Steam games that have a SteamAppId but no cached artwork.
    /// Throttles requests to be polite to the CDN.
    /// </summary>
    public static async Task DownloadAllMissing(GameDatabase db, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ArtworkDir);

        var games = db.GetAllGames()
            .Where(g => g.SteamAppId.HasValue && string.IsNullOrEmpty(g.ArtworkPath))
            .ToList();

        if (games.Count == 0) return;

        Log.Information("Downloading artwork for {Count} Steam games", games.Count);
        int downloaded = 0, failed = 0;

        foreach (var game in games)
        {
            if (ct.IsCancellationRequested) break;

            var localPath = GetLocalPath(game.SteamAppId!.Value);
            if (File.Exists(localPath))
            {
                db.UpdateArtworkPath(game.Id, localPath);
                downloaded++;
                continue;
            }

            try
            {
                var url = $"https://cdn.akamai.steamstatic.com/steam/apps/{game.SteamAppId}/library_600x900_2x.jpg";
                var response = await HttpClient.GetAsync(url, ct);

                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                    var tempPath = localPath + ".tmp";
                    await File.WriteAllBytesAsync(tempPath, bytes, ct);
                    File.Move(tempPath, localPath, overwrite: true);
                    db.UpdateArtworkPath(game.Id, localPath);
                    downloaded++;
                }
                else
                {
                    failed++;
                    Log.Debug("Artwork not available for {Name} (AppId {Id}): {Status}",
                        game.DisplayName, game.SteamAppId, response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                Log.Debug("Failed to download artwork for {Name}: {Error}", game.DisplayName, ex.Message);
            }

            // Throttle: 100ms between requests
            await Task.Delay(100, ct);
        }

        Log.Information("Artwork download complete: {Downloaded} downloaded, {Failed} unavailable", downloaded, failed);
    }

    public static string GetLocalPath(int steamAppId)
    {
        return Path.Combine(ArtworkDir, $"{steamAppId}.jpg");
    }
}
