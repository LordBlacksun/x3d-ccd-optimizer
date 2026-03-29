using System.Diagnostics;
using System.IO;
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
        Timeout = TimeSpan.FromSeconds(30)
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

    /// <summary>
    /// Downloads the latest release exe from GitHub and applies the update.
    /// Downloads to temp, writes a PowerShell script that waits for this process to exit,
    /// replaces the exe, and relaunches. Then exits the current app.
    /// </summary>
    public static async Task<bool> DownloadAndApply(Action<string> onProgress, CancellationToken ct = default)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (currentExe == null)
            {
                Log.Warning("Cannot determine current exe path for update");
                return false;
            }

            onProgress("Checking release...");

            // Get the latest release asset URL
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.UserAgent.ParseAdd($"X3DCcdOptimizer/{version?.ToString(3) ?? "0"}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            // Find the exe asset in the release (look for .exe in assets, or fall back to .zip)
            string? downloadUrl = null;
            string? assetName = null;

            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (name == null || url == null) continue;

                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = url;
                        assetName = name;
                        break;
                    }
                }
            }

            if (downloadUrl == null)
            {
                Log.Warning("No downloadable asset found in latest release");
                return false;
            }

            // Download to temp
            onProgress("Downloading...");
            Log.Information("Downloading update from {Url}", downloadUrl);

            var downloadReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            downloadReq.Headers.UserAgent.ParseAdd($"X3DCcdOptimizer/{version?.ToString(3) ?? "0"}");
            var downloadResp = await Http.SendAsync(downloadReq, HttpCompletionOption.ResponseHeadersRead, ct);
            downloadResp.EnsureSuccessStatusCode();

            var tempZip = Path.Combine(Path.GetTempPath(), $"x3d-update-{Guid.NewGuid():N}.zip");
            var tempDir = Path.Combine(Path.GetTempPath(), $"x3d-update-{Guid.NewGuid():N}");

            try
            {
                await using (var fs = File.Create(tempZip))
                {
                    await downloadResp.Content.CopyToAsync(fs, ct);
                }

                // Extract ZIP and find the exe
                onProgress("Extracting...");
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, tempDir);

                var newExe = Directory.EnumerateFiles(tempDir, "X3DCcdOptimizer.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (newExe == null)
                {
                    Log.Warning("Update ZIP does not contain X3DCcdOptimizer.exe");
                    return false;
                }

                // Write updater script — waits for this process, replaces exe, relaunches
                onProgress("Applying...");
                var scriptPath = Path.Combine(Path.GetTempPath(), "x3d-updater.ps1");
                var esc = (string s) => s.Replace("'", "''");
                var script =
                    "Start-Sleep -Milliseconds 500\n" +
                    "$maxWait = 30\n" +
                    "$waited = 0\n" +
                    "while ((Get-Process -Name 'X3DCcdOptimizer' -ErrorAction SilentlyContinue) -and $waited -lt $maxWait) {\n" +
                    "    Start-Sleep -Seconds 1\n" +
                    "    $waited++\n" +
                    "}\n" +
                    $"Copy-Item -Path '{esc(newExe)}' -Destination '{esc(currentExe)}' -Force\n" +
                    $"Start-Process -FilePath '{esc(currentExe)}'\n" +
                    $"Remove-Item -Path '{esc(tempZip)}' -ErrorAction SilentlyContinue\n" +
                    $"Remove-Item -Path '{esc(tempDir)}' -Recurse -ErrorAction SilentlyContinue\n" +
                    $"Remove-Item -Path '{esc(scriptPath)}' -ErrorAction SilentlyContinue\n";

                await File.WriteAllTextAsync(scriptPath, script, ct);

                // Launch the updater and exit
                Log.Information("Launching updater script, app will restart");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                return true;
            }
            catch
            {
                // Clean up on failure
                try { File.Delete(tempZip); } catch { }
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update download/apply failed");
            return false;
        }
    }
}
