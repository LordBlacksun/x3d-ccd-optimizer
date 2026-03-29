using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Serilog;

namespace X3DCcdOptimizer.Core;

/// <summary>
/// Checks for new releases via the GitHub Releases API.
/// Opt-in only — disabled by default. No data is sent; only a single GET request.
/// </summary>
public static class UpdateChecker
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private const string ReleasesUrl =
        "https://api.github.com/repos/LordBlacksun/X3D-CCD-Optimizer/releases/latest";

    /// <summary>
    /// Checks if a newer version is available on GitHub.
    /// Returns the new version string (e.g., "1.1.0") if an update exists, or null.
    /// </summary>
    public static async Task<string?> CheckForUpdate()
    {
        try
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion == null) return null;

            var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.UserAgent.ParseAdd($"X3DCcdOptimizer/{currentVersion.ToString(3)}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log.Debug("Update check failed: HTTP {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                return null;

            var tag = tagEl.GetString()?.TrimStart('v');
            if (tag == null || !Version.TryParse(tag, out var latestVersion))
                return null;

            if (latestVersion > currentVersion)
            {
                Log.Information("Update available: v{Latest} (current: v{Current})",
                    latestVersion.ToString(3), currentVersion.ToString(3));
                return latestVersion.ToString(3);
            }

            Log.Debug("No update available (current: v{Current}, latest: v{Latest})",
                currentVersion.ToString(3), latestVersion.ToString(3));
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("Update check failed: {Error}", ex.Message);
            return null;
        }
    }
}
