using System.Diagnostics;
using Microsoft.Win32;
using Serilog;

namespace X3DCcdInspector.Core;

public static class StartupManager
{
    private const string AppName = "X3DCcdInspector";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static void Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null && key != null)
            {
                key.SetValue(AppName, $"\"{exePath}\" --minimized");
                Log.Information("Registered start-with-Windows: {Path}", exePath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register start-with-Windows");
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(AppName, false);
            Log.Information("Unregistered start-with-Windows");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to unregister start-with-Windows");
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch (Exception ex)
        {
            Log.Debug("Failed to check startup registration: {Error}", ex.Message);
            return false;
        }
    }
}
