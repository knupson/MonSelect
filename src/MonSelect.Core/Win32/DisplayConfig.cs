using System.Runtime.InteropServices;

namespace MonSelect.Core.Win32;

// Sólo se declaran los campos que MonSelect usa; el resto se rellena como
// padding con el tamaño correcto para que el marshalling no se corra.

[StructLayout(LayoutKind.Sequential)]
internal struct Luid
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    public Luid adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    public Luid adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint outputTechnology;
    public uint rotation;
    public uint scaling;
    public uint refreshNumerator;
    public uint refreshDenominator;
    public uint scanLineOrdering;
    [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    public DisplayConfigPathSourceInfo sourceInfo;
    public DisplayConfigPathTargetInfo targetInfo;
    public uint flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigModeInfo
{
    public uint infoType;
    public uint id;
    public Luid adapterId;
    // Union de 64 bytes: targetMode / sourceMode / desktopImageInfo.
    // MonSelect no la lee, sólo necesita el tamaño correcto.
    public ulong union0;
    public ulong union1;
    public ulong union2;
    public ulong union3;
    public ulong union4;
    public ulong union5;
    public ulong union6;
    public ulong union7;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDeviceInfoHeader
{
    public uint type;
    public uint size;
    public Luid adapterId;
    public uint id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigTargetDeviceName
{
    public DisplayConfigDeviceInfoHeader header;
    public uint flags;
    public uint outputTechnology;
    public ushort edidManufactureId;
    public ushort edidProductCodeId;
    public uint connectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string monitorFriendlyDeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string monitorDevicePath;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigSourceDeviceName
{
    public DisplayConfigDeviceInfoHeader header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string viewGdiDeviceName;
}

internal static class DisplayConfigNative
{
    internal const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    internal const uint DEVICE_INFO_GET_SOURCE_NAME = 1;
    internal const uint DEVICE_INFO_GET_TARGET_NAME = 2;
    internal const int ERROR_SUCCESS = 0;

    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        nint currentTopologyId);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName info);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName info);
}
