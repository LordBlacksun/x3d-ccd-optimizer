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

namespace X3DCcdOptimizer.Core;

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
