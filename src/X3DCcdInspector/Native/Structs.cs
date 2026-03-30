using System.Runtime.InteropServices;

namespace X3DCcdInspector.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct GROUP_AFFINITY
{
    public UIntPtr Mask;
    public ushort Group;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CACHE_RELATIONSHIP
{
    public byte Level;
    public byte Associativity;
    public ushort LineSize;
    public uint CacheSize;
    public int Type; // PROCESSOR_CACHE_TYPE enum

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
    public byte[] Reserved;

    public ushort GroupCount;
    public GROUP_AFFINITY GroupMask; // First element; for multi-group there may be more
}

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
{
    public int Relationship;
    public uint Size;
    // Union data follows — parsed manually via pointer arithmetic
}

[StructLayout(LayoutKind.Sequential)]
internal struct PDH_FMT_COUNTERVALUE
{
    public int CStatus;
    public double doubleValue;
}
