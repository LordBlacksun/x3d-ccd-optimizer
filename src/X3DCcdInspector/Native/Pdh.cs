using System.Runtime.InteropServices;

namespace X3DCcdInspector.Native;

internal static class Pdh
{
    internal const uint PDH_FMT_DOUBLE = 0x00000200;
    internal const int PDH_CSTATUS_VALID_DATA = 0x00000000;
    internal const int PDH_CSTATUS_NEW_DATA = 0x00000001;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern int PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern int PdhAddEnglishCounter(
        IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll")]
    internal static extern int PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll")]
    internal static extern int PdhGetFormattedCounterValue(
        IntPtr hCounter, uint dwFormat, out IntPtr lpdwType, out PDH_FMT_COUNTERVALUE pValue);

    [DllImport("pdh.dll")]
    internal static extern int PdhCloseQuery(IntPtr hQuery);
}
