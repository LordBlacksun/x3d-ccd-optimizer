using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Serilog;

namespace X3DCcdOptimizer.Core;

/// <summary>
/// Scans installed game launchers (Steam, Epic) to build an exe → display name dictionary.
/// Results are cached to installed_games.json in %APPDATA%\X3DCCDOptimizer.
/// GOG Galaxy is not supported (requires SQLite dependency).
/// </summary>
public static class GameLibraryScanner
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "X3DCCDOptimizer", "installed_games.json");

    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(7);

    // Exe names to skip when scanning game directories
    private static readonly HashSet<string> SkipExePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "unins", "uninstall", "setup", "install", "redist", "vcredist",
        "dxsetup", "dxwebsetup", "dotnetfx", "ndp", "ue4prereq",
        "crashreport", "crashhandl", "unitycrashhndl",
        "UnityCrashHandler", "UnityCrashHandler64", "UnityCrashHandler32",
        "CrashReporter", "CrashSender", "BugSplat",
        "7z", "winrar"
    };

    /// <summary>
    /// Loads cached results if fresh enough, otherwise scans synchronously.
    /// Returns exe name (with .exe) → display name dictionary.
    /// </summary>
    public static Dictionary<string, string> LoadOrScan()
    {
        var cached = LoadCache();
        if (cached != null)
        {
            Log.Information("Loaded {Count} launcher-scanned games from cache", cached.Count);
            return cached;
        }

        var games = ScanAll();
        SaveCache(games);
        return games;
    }

    /// <summary>
    /// Returns true if cache exists but is older than 7 days.
    /// </summary>
    public static bool IsCacheStale()
    {
        try
        {
            if (!File.Exists(CachePath)) return true;
            var json = File.ReadAllText(CachePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("lastScanUtc", out var ts))
            {
                if (DateTime.TryParse(ts.GetString(), out var lastScan))
                    return (DateTime.UtcNow - lastScan) > CacheMaxAge;
            }
        }
        catch { }
        return true;
    }

    /// <summary>
    /// Performs a full scan of all supported launchers. Safe to call from background thread.
    /// </summary>
    public static Dictionary<string, string> ScanAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try { ScanSteam(result); }
        catch (Exception ex) { Log.Warning(ex, "Steam library scan failed"); }

        try { ScanEpic(result); }
        catch (Exception ex) { Log.Warning(ex, "Epic library scan failed"); }

        Log.Information("Launcher scan complete: {Count} games found (Steam + Epic)", result.Count);
        return result;
    }

    /// <summary>
    /// Saves scan results and updates cache file.
    /// </summary>
    public static void SaveCache(Dictionary<string, string> games)
    {
        try
        {
            var dir = Path.GetDirectoryName(CachePath)!;
            Directory.CreateDirectory(dir);

            var entries = games.Select(kv => new { exe = kv.Key, name = kv.Value }).ToArray();
            var cache = new { lastScanUtc = DateTime.UtcNow.ToString("o"), games = entries };
            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });

            var tempPath = CachePath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, CachePath, overwrite: true);
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save launcher scan cache");
        }
    }

    #region Steam

    private static void ScanSteam(Dictionary<string, string> result)
    {
        var steamPath = GetSteamPath();
        if (steamPath == null) return;

        var libraryFolders = GetSteamLibraryFolders(steamPath);
        Log.Debug("Found {Count} Steam library folders", libraryFolders.Count);

        foreach (var libFolder in libraryFolders)
        {
            var steamApps = Path.Combine(libFolder, "steamapps");
            if (!Directory.Exists(steamApps)) continue;

            try
            {
                foreach (var acfFile in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
                {
                    try
                    {
                        ParseAcfAndScanExes(acfFile, steamApps, result);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Failed to parse {File}: {Error}", Path.GetFileName(acfFile), ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Failed to enumerate Steam library {Path}: {Error}", libFolder, ex.Message);
            }
        }
    }

    private static string? GetSteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = key?.GetValue("SteamPath") as string;
            if (path != null && Directory.Exists(path))
                return path;
        }
        catch (Exception ex)
        {
            Log.Debug("Steam registry lookup failed: {Error}", ex.Message);
        }
        return null;
    }

    private static List<string> GetSteamLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) return folders;

        try
        {
            var content = File.ReadAllText(vdfPath);
            var parsed = VdfParser.Parse(content);

            // libraryfolders.vdf has numbered keys "0", "1", etc. each with a "path" value
            if (parsed.TryGetValue("libraryfolders", out var root) && root is Dictionary<string, object> rootDict)
            {
                foreach (var kv in rootDict)
                {
                    if (kv.Value is Dictionary<string, object> entry &&
                        entry.TryGetValue("path", out var pathObj) &&
                        pathObj is string libPath &&
                        Directory.Exists(libPath))
                    {
                        if (!folders.Contains(libPath, StringComparer.OrdinalIgnoreCase))
                            folders.Add(libPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Failed to parse libraryfolders.vdf: {Error}", ex.Message);
        }

        return folders;
    }

    private static void ParseAcfAndScanExes(string acfFile, string steamAppsDir, Dictionary<string, string> result)
    {
        var content = File.ReadAllText(acfFile);
        var parsed = VdfParser.Parse(content);

        if (!parsed.TryGetValue("AppState", out var stateObj) || stateObj is not Dictionary<string, object> state)
            return;

        if (!state.TryGetValue("name", out var nameObj) || nameObj is not string gameName)
            return;
        if (!state.TryGetValue("installdir", out var dirObj) || dirObj is not string installDir)
            return;

        if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(installDir))
            return;

        var fullPath = Path.Combine(steamAppsDir, "common", installDir);
        if (!Directory.Exists(fullPath)) return;

        ScanDirectoryForExes(fullPath, gameName, result);
    }

    #endregion

    #region Epic

    private static void ScanEpic(Dictionary<string, string> result)
    {
        var manifestDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        if (!Directory.Exists(manifestDir)) return;

        foreach (var itemFile in Directory.EnumerateFiles(manifestDir, "*.item"))
        {
            try
            {
                var json = File.ReadAllText(itemFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("DisplayName", out var nameEl)) continue;
                if (!root.TryGetProperty("LaunchExecutable", out var exeEl)) continue;

                var displayName = nameEl.GetString();
                var launchExe = exeEl.GetString();

                if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(launchExe))
                    continue;

                var exeName = Path.GetFileName(launchExe);
                if (!string.IsNullOrEmpty(exeName) && !result.ContainsKey(exeName))
                    result[exeName] = displayName;
            }
            catch (Exception ex)
            {
                Log.Debug("Failed to parse Epic manifest {File}: {Error}", Path.GetFileName(itemFile), ex.Message);
            }
        }
    }

    #endregion

    #region Helpers

    private static void ScanDirectoryForExes(string dir, string gameName, Dictionary<string, string> result)
    {
        try
        {
            foreach (var exePath in Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories))
            {
                var exeName = Path.GetFileName(exePath);
                if (ShouldSkipExe(exeName)) continue;

                if (!result.ContainsKey(exeName))
                    result[exeName] = gameName;
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (Exception ex)
        {
            Log.Debug("Failed to scan {Dir}: {Error}", dir, ex.Message);
        }
    }

    private static bool ShouldSkipExe(string exeName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(exeName);

        foreach (var prefix in SkipExePrefixes)
        {
            if (nameWithoutExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Dictionary<string, string>? LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;

            var json = File.ReadAllText(CachePath);
            using var doc = JsonDocument.Parse(json);

            // Check freshness
            if (doc.RootElement.TryGetProperty("lastScanUtc", out var ts))
            {
                if (DateTime.TryParse(ts.GetString(), out var lastScan))
                {
                    if ((DateTime.UtcNow - lastScan) > CacheMaxAge)
                        return null; // Stale cache — caller will rescan
                }
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("games", out var gamesArr))
            {
                foreach (var entry in gamesArr.EnumerateArray())
                {
                    if (entry.TryGetProperty("exe", out var exeEl) &&
                        entry.TryGetProperty("name", out var nameEl))
                    {
                        var exe = exeEl.GetString();
                        var name = nameEl.GetString();
                        if (exe != null && name != null)
                            result[exe] = name;
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Debug("Failed to load launcher scan cache: {Error}", ex.Message);
            return null;
        }
    }

    #endregion
}

/// <summary>
/// Minimal parser for Valve KeyValues format (VDF/ACF files).
/// Handles nested blocks and quoted string values.
/// </summary>
internal static class VdfParser
{
    public static Dictionary<string, object> Parse(string content)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        ParseBlock(content, ref i, result);
        return result;
    }

    private static void ParseBlock(string content, ref int i, Dictionary<string, object> dict)
    {
        while (i < content.Length)
        {
            SkipWhitespace(content, ref i);
            if (i >= content.Length) break;

            if (content[i] == '}')
            {
                i++;
                return;
            }

            if (content[i] == '"')
            {
                var key = ReadQuotedString(content, ref i);
                SkipWhitespace(content, ref i);

                if (i < content.Length && content[i] == '"')
                {
                    // Key-value pair
                    var value = ReadQuotedString(content, ref i);
                    dict[key] = value;
                }
                else if (i < content.Length && content[i] == '{')
                {
                    // Nested block
                    i++; // skip '{'
                    var nested = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    ParseBlock(content, ref i, nested);
                    dict[key] = nested;
                }
            }
            else
            {
                i++; // skip unexpected characters
            }
        }
    }

    private static string ReadQuotedString(string content, ref int i)
    {
        if (i >= content.Length || content[i] != '"')
            return "";

        i++; // skip opening quote
        var start = i;

        while (i < content.Length && content[i] != '"')
        {
            if (content[i] == '\\' && i + 1 < content.Length)
                i++; // skip escaped char
            i++;
        }

        var result = content[start..i];
        if (i < content.Length)
            i++; // skip closing quote

        return result.Replace("\\\\", "\\").Replace("\\\"", "\"");
    }

    private static void SkipWhitespace(string content, ref int i)
    {
        while (i < content.Length)
        {
            if (char.IsWhiteSpace(content[i]))
            {
                i++;
            }
            else if (content[i] == '/' && i + 1 < content.Length && content[i + 1] == '/')
            {
                // Skip line comment
                while (i < content.Length && content[i] != '\n')
                    i++;
            }
            else
            {
                break;
            }
        }
    }
}
