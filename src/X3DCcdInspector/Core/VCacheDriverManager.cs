// AMD V-Cache driver registry interface discovered and documented by cocafe
// https://github.com/cocafe/vcache-tray
//
// The amd3dvcache driver exposes CCD scheduling preferences via the registry at:
//   HKLM\SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences
//
// DefaultType (REG_DWORD):
//   0 = PREFER_FREQ  (frequency-preferred CCD — driver default)
//   1 = PREFER_CACHE (V-Cache-preferred CCD)

using Microsoft.Win32;
using Serilog;

namespace X3DCcdInspector.Core;

public static class VCacheDriverManager
{
    private const string RegKeyPath = @"SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences";
    private const string RegValueName = "DefaultType";
    private const int PREFER_FREQ = 0;
    private const int PREFER_CACHE = 1;

    private static readonly Lazy<bool> _isDriverAvailable = new(CheckDriverInstalled);

    public static bool IsDriverAvailable => _isDriverAvailable.Value;

    /// <summary>
    /// Read the current DefaultType preference from the driver registry.
    /// Returns null if the driver is not installed or the value cannot be read.
    /// </summary>
    public static int? GetCurrentPreference()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegKeyPath);
            if (key == null)
                return null;

            var value = key.GetValue(RegValueName);
            if (value is int intVal)
            {
                if (intVal is PREFER_FREQ or PREFER_CACHE)
                    return intVal;
                Log.Warning("amd3dvcache DefaultType has unexpected value: {Value}", intVal);
                return null;
            }

            if (value != null)
                Log.Warning("amd3dvcache DefaultType has unexpected type: {Type}", value.GetType().Name);

            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read amd3dvcache preference from registry");
            return null;
        }
    }

    /// <summary>
    /// Set the driver preference to PREFER_CACHE (V-Cache CCD preferred).
    /// </summary>
    public static bool SetCachePreferred()
    {
        return WritePreference(PREFER_CACHE);
    }

    /// <summary>
    /// Set the driver preference to PREFER_FREQ (frequency CCD preferred — driver default).
    /// </summary>
    public static bool SetFrequencyPreferred()
    {
        return WritePreference(PREFER_FREQ);
    }

    /// <summary>
    /// Restore the driver preference to its default (PREFER_FREQ).
    /// </summary>
    public static bool RestoreDefault()
    {
        return SetFrequencyPreferred();
    }

    // ── Per-App Profile Methods ──────────────────────────────────────
    //
    // Per-application CCD preferences are stored at:
    //   HKLM\...\amd3dvcache\Preferences\App\{ProfileName}
    //     EndsWith (REG_SZ): process name to match (e.g., "ffxiv_dx11.exe")
    //     Type (DWORD): 0 = PREFER_FREQ, 1 = PREFER_CACHE
    //
    // When the driver's service detects a matching foreground process, it
    // switches the system-wide CCD preference to match.

    private const string AppProfilesSubKey = @"SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences\App";

    /// <summary>
    /// Creates or updates a per-app profile in the AMD driver registry.
    /// </summary>
    public static bool SetAppProfile(string profileName, string exeName, int type)
    {
        if (type is not (PREFER_FREQ or PREFER_CACHE))
            throw new ArgumentOutOfRangeException(nameof(type));

        try
        {
            var keyPath = $@"{AppProfilesSubKey}\{profileName}";
            using var key = Registry.LocalMachine.CreateSubKey(keyPath);
            if (key == null)
            {
                Log.Error("Cannot create app profile registry key: {Path}", keyPath);
                return false;
            }

            key.SetValue("EndsWith", exeName, RegistryValueKind.String);
            key.SetValue("Type", type, RegistryValueKind.DWord);

            Log.Information("Set app profile: {Profile} → {Exe} = {Type}",
                profileName, exeName, type == PREFER_CACHE ? "PREFER_CACHE" : "PREFER_FREQ");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "Access denied creating app profile — run as administrator");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set app profile: {Profile}", profileName);
            return false;
        }
    }

    /// <summary>
    /// Removes a per-app profile from the AMD driver registry.
    /// </summary>
    public static bool RemoveAppProfile(string profileName)
    {
        try
        {
            using var appKey = Registry.LocalMachine.OpenSubKey(AppProfilesSubKey, writable: true);
            if (appKey == null) return true; // Parent key doesn't exist, nothing to remove

            appKey.DeleteSubKey(profileName, throwOnMissingSubKey: false);
            Log.Information("Removed app profile: {Profile}", profileName);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove app profile: {Profile}", profileName);
            return false;
        }
    }

    /// <summary>
    /// Reads an existing per-app profile. Returns null if not found.
    /// </summary>
    public static (string exeName, int type)? GetAppProfile(string profileName)
    {
        try
        {
            var keyPath = $@"{AppProfilesSubKey}\{profileName}";
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key == null) return null;

            var endsWith = key.GetValue("EndsWith") as string;
            var type = key.GetValue("Type");
            if (endsWith == null || type is not int typeInt)
                return null;

            return (endsWith, typeInt);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read app profile: {Profile}", profileName);
            return null;
        }
    }

    /// <summary>
    /// Enumerates all per-app profiles under Preferences\App\.
    /// Returns profileName → (exeName, type) dictionary.
    /// </summary>
    public static Dictionary<string, (string exeName, int type)> GetAllAppProfiles()
    {
        var result = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var appKey = Registry.LocalMachine.OpenSubKey(AppProfilesSubKey);
            if (appKey == null) return result;

            foreach (var subKeyName in appKey.GetSubKeyNames())
            {
                using var sub = appKey.OpenSubKey(subKeyName);
                if (sub == null) continue;

                var endsWith = sub.GetValue("EndsWith") as string;
                var type = sub.GetValue("Type");
                if (endsWith != null && type is int typeInt)
                    result[subKeyName] = (endsWith, typeInt);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to enumerate app profiles");
        }
        return result;
    }

    /// <summary>
    /// Sanitizes a game display name into a valid registry key name.
    /// Replaces spaces with underscores, removes special characters.
    /// </summary>
    public static string SanitizeProfileName(string displayName)
    {
        var sb = new System.Text.StringBuilder(displayName.Length);
        foreach (var ch in displayName)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch is ' ' or '-')
                sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString() : "Unknown";
    }

    /// <summary>
    /// Syncs LiteDB CCD preferences with actual registry state on startup.
    /// - Games with VCache/Frequency in DB but no registry profile: recreate the profile
    /// - Registry profiles with no matching DB preference: remove orphaned profile
    /// </summary>
    public static void SyncAppProfiles(GameDatabase gameDb)
    {
        if (!IsDriverAvailable)
        {
            Log.Information("AMD driver not available — skipping app profile sync");
            return;
        }

        var registryProfiles = GetAllAppProfiles();
        var dbGames = gameDb.GetGamesWithPreference();
        var syncedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Ensure DB preferences have corresponding registry profiles
        foreach (var game in dbGames)
        {
            var profileName = SanitizeProfileName(game.DisplayName);
            var type = game.CcdPreference == "VCache" ? PREFER_CACHE : PREFER_FREQ;
            syncedProfiles.Add(profileName);

            if (!registryProfiles.TryGetValue(profileName, out var existing) ||
                existing.exeName != game.ProcessName || existing.type != type)
            {
                SetAppProfile(profileName, game.ProcessName, type);
                Log.Information("Sync: recreated profile {Profile} for {Exe}", profileName, game.ProcessName);
            }
        }

        // Remove orphaned registry profiles that we created but are now Auto in DB
        foreach (var (profileName, _) in registryProfiles)
        {
            if (!syncedProfiles.Contains(profileName))
            {
                RemoveAppProfile(profileName);
                Log.Information("Sync: removed orphaned profile {Profile}", profileName);
            }
        }

        Log.Information("App profile sync complete: {Count} active profiles", syncedProfiles.Count);
    }

    private static bool CheckDriverInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegKeyPath);
            return key != null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to check amd3dvcache driver registry key");
            return false;
        }
    }

    private static bool WritePreference(int value)
    {
        if (value is not (PREFER_FREQ or PREFER_CACHE))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Expected 0 (PREFER_FREQ) or 1 (PREFER_CACHE)");

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegKeyPath, writable: true);
            if (key == null)
            {
                Log.Error("Cannot open amd3dvcache registry key for writing — driver may not be installed");
                return false;
            }

            key.SetValue(RegValueName, value, RegistryValueKind.DWord);

            // Verify write
            var readBack = key.GetValue(RegValueName);
            if (readBack is int written && written != value)
                Log.Warning("Registry write verification failed: wrote {Expected}, read {Actual}", value, written);

            Log.Information("Set amd3dvcache DefaultType={Value} ({Desc})",
                value, value == PREFER_CACHE ? "PREFER_CACHE" : "PREFER_FREQ");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "Access denied writing amd3dvcache registry — run as administrator");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to write amd3dvcache preference to registry");
            return false;
        }
    }
}
