namespace X3DCcdInspector.Models;

public static class ProtectedProcesses
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "csrss", "smss", "services", "wininit",
        "lsass", "winlogon", "dwm", "audiodg", "fontdrvhost",
        "Registry", "Memory Compression", "svchost", "X3DCcdInspector"
    };
}
