using System.IO;
using Serilog;

namespace X3DCcdInspector.Core;

/// <summary>
/// Handles dirty shutdown recovery. If the app previously set AMD driver preferences
/// and crashed before restoring, this ensures the next launch cleans up.
/// </summary>
public static class RecoveryManager
{
    private static readonly string RecoveryDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "X3DCCDInspector");

    private static readonly string RecoveryPath = Path.Combine(RecoveryDir, "recovery.json");

    /// <summary>
    /// Check if a dirty shutdown occurred and recover if needed.
    /// Restores AMD driver preference to default and cleans up any recovery file
    /// left by a previous version of the app.
    /// </summary>
    public static void RecoverFromDirtyShutdown()
    {
        try
        {
            if (!File.Exists(RecoveryPath))
                return;

            Log.Warning("Recovery file found — previous session ended unexpectedly");

            // Restore AMD driver preference to default (safe no-op if driver not installed)
            if (VCacheDriverManager.RestoreDefault())
                Log.Information("Recovery: restored amd3dvcache DefaultType to PREFER_FREQ (default)");
            else
                Log.Information("Recovery: AMD driver not present or already at default — no action needed");

            DeleteRecoveryFile();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Recovery failed — deleting recovery file and continuing");
            DeleteRecoveryFile();
        }
    }

    /// <summary>
    /// Called on clean exit. Deletes any leftover recovery file.
    /// </summary>
    public static void OnDisengage()
    {
        DeleteRecoveryFile();
    }

    public static bool IsRecoveryNeeded() => File.Exists(RecoveryPath);

    private static void DeleteRecoveryFile()
    {
        try
        {
            if (File.Exists(RecoveryPath))
                File.Delete(RecoveryPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete recovery file");
        }
    }
}
