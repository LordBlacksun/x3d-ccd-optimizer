using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using Serilog;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.Core;

/// <summary>
/// Scans installed game launchers (Steam, Epic, GOG Galaxy) to discover installed games.
/// Returns ScannedGame models for LiteDB storage.
/// </summary>
public static class GameLibraryScanner
{
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
    /// Performs a full scan of all supported launchers. Safe to call from background thread.
    /// Returns ScannedGame models with source, SteamAppId, and install path metadata.
    /// </summary>
    public static List<ScannedGame> ScanAll()
    {
        var result = new List<ScannedGame>();
        int steamCount = 0, epicCount = 0, gogCount = 0;

        try
        {
            var steamGames = ScanSteam();
            steamCount = steamGames.Count;
            result.AddRange(steamGames);
        }
        catch (Exception ex) { Log.Warning(ex, "Steam library scan failed"); }

        try
        {
            var epicGames = ScanEpic();
            epicCount = epicGames.Count;
            result.AddRange(epicGames);
        }
        catch (Exception ex) { Log.Warning(ex, "Epic library scan failed"); }

        try
        {
            var gogGames = ScanGog();
            gogCount = gogGames.Count;
            result.AddRange(gogGames);
        }
        catch (Exception ex) { Log.Warning(ex, "GOG library scan failed"); }

        Log.Information("Library scan complete: {Steam} Steam, {Epic} Epic, {Gog} GOG ({Total} total)",
            steamCount, epicCount, gogCount, result.Count);
        return result;
    }

    #region Steam

    private static List<ScannedGame> ScanSteam()
    {
        var games = new List<ScannedGame>();
        var steamPath = GetSteamPath();
        if (steamPath == null)
        {
            Log.Debug("Steam not found");
            return games;
        }

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
                        ParseAcfAndScanExes(acfFile, steamApps, games);
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

        return games;
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

    private static void ParseAcfAndScanExes(string acfFile, string steamAppsDir, List<ScannedGame> result)
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

        // Extract SteamAppId from ACF filename: appmanifest_{appid}.acf
        int? steamAppId = null;
        var fileName = Path.GetFileNameWithoutExtension(acfFile);
        if (fileName.StartsWith("appmanifest_") && int.TryParse(fileName[12..], out var appId))
            steamAppId = appId;

        var fullPath = Path.Combine(steamAppsDir, "common", installDir);
        if (!Directory.Exists(fullPath)) return;

        ScanDirectoryForExes(fullPath, gameName, "steam", steamAppId, result);
    }

    #endregion

    #region Epic

    private static List<ScannedGame> ScanEpic()
    {
        var games = new List<ScannedGame>();
        var manifestDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        if (!Directory.Exists(manifestDir))
        {
            Log.Debug("Epic Games not found");
            return games;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                var installLoc = root.TryGetProperty("InstallLocation", out var locEl) ? locEl.GetString() : null;

                if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(launchExe))
                    continue;

                var exeName = Path.GetFileName(launchExe);
                if (!string.IsNullOrEmpty(exeName) && seen.Add(exeName))
                {
                    games.Add(new ScannedGame
                    {
                        ProcessName = exeName,
                        DisplayName = displayName,
                        Source = "epic",
                        InstallPath = installLoc
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Failed to parse Epic manifest {File}: {Error}", Path.GetFileName(itemFile), ex.Message);
            }
        }

        return games;
    }

    #endregion

    #region GOG Galaxy

    private static List<ScannedGame> ScanGog()
    {
        var games = new List<ScannedGame>();
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GOG.com", "Galaxy", "storage", "galaxy-2.0.db");

        if (!File.Exists(dbPath))
        {
            Log.Debug("GOG Galaxy not found");
            return games;
        }

        // Copy DB to temp file to avoid locking conflicts with running Galaxy client
        var tempDb = Path.Combine(Path.GetTempPath(), $"x3d_gog_{Guid.NewGuid():N}.db");
        try
        {
            File.Copy(dbPath, tempDb, overwrite: true);

            using var conn = new SqliteConnection($"Data Source={tempDb};Mode=ReadOnly");
            conn.Open();

            // GOG Galaxy 2.0 schema: GamePieces contains game metadata as JSON
            // We look for installed GOG games with their titles
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT DISTINCT gp.releaseKey, gp.value
                FROM GamePieces gp
                INNER JOIN GamePieceTypes gpt ON gp.gamePieceTypeId = gpt.id
                WHERE gpt.type = 'originalTitle'
                AND gp.releaseKey LIKE 'gog_%'";

            var gogTitles = new Dictionary<string, string>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var releaseKey = reader.GetString(0);
                    var titleJson = reader.GetString(1);
                    // Value is JSON like {"title":"Game Name"}
                    try
                    {
                        using var titleDoc = JsonDocument.Parse(titleJson);
                        if (titleDoc.RootElement.TryGetProperty("title", out var t))
                        {
                            var title = t.GetString();
                            if (!string.IsNullOrEmpty(title))
                                gogTitles[releaseKey] = title;
                        }
                    }
                    catch { }
                }
            }

            // Get install paths from registry
            try
            {
                using var gogKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
                if (gogKey != null)
                {
                    foreach (var subKeyName in gogKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var gameKey = gogKey.OpenSubKey(subKeyName);
                            if (gameKey == null) continue;

                            var gameName = gameKey.GetValue("GAMENAME") as string;
                            var gamePath = gameKey.GetValue("PATH") as string;
                            var gameExe = gameKey.GetValue("EXE") as string;

                            if (string.IsNullOrEmpty(gameExe)) continue;
                            var exeName = Path.GetFileName(gameExe);
                            if (string.IsNullOrEmpty(exeName) || ShouldSkipExe(exeName)) continue;

                            // Try to get a nicer name from Galaxy DB
                            var displayName = gameName ?? exeName;
                            foreach (var kv in gogTitles)
                            {
                                if (kv.Key.Contains(subKeyName))
                                {
                                    displayName = kv.Value;
                                    break;
                                }
                            }

                            if (seen.Add(exeName))
                            {
                                games.Add(new ScannedGame
                                {
                                    ProcessName = exeName,
                                    DisplayName = displayName,
                                    Source = "gog",
                                    InstallPath = gamePath
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug("Failed to read GOG registry entry {Key}: {Error}", subKeyName, ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Failed to read GOG registry: {Error}", ex.Message);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("GOG Galaxy database scan failed: {Error}", ex.Message);
        }
        finally
        {
            try { File.Delete(tempDb); } catch { }
        }

        return games;
    }

    #endregion

    #region Helpers

    private static void ScanDirectoryForExes(string dir, string gameName, string source,
        int? steamAppId, List<ScannedGame> result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var exePath in Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories))
            {
                var exeName = Path.GetFileName(exePath);
                if (ShouldSkipExe(exeName)) continue;

                if (seen.Add(exeName))
                {
                    result.Add(new ScannedGame
                    {
                        ProcessName = exeName,
                        DisplayName = gameName,
                        Source = source,
                        InstallPath = dir,
                        SteamAppId = steamAppId
                    });
                }
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
