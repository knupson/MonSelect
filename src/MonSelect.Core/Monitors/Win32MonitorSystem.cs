using MonSelect.Core.Win32;

namespace MonSelect.Core.Monitors;

/// <summary>
/// Enumera monitores con EnumDisplayMonitors y les asigna identidad estable
/// cruzando el nombre GDI (\\.\DISPLAYn) contra los device paths que devuelve
/// QueryDisplayConfig.
/// </summary>
public sealed class Win32MonitorSystem : IMonitorSystem
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var pathsByGdiName = BuildDevicePathMap();
        var result = new List<MonitorInfo>();

        bool Callback(nint hMonitor, nint hdc, ref Rect rect, nint data)
        {
            var info = MonitorInfoEx.Create();
            if (!NativeMethods.GetMonitorInfoW(hMonitor, ref info))
                return true;

            var gdiName = info.szDevice;
            string devicePath;
            if (pathsByGdiName.TryGetValue(gdiName, out var p))
            {
                devicePath = p;
            }
            else
            {
                // Sin device path la identidad no es estable, pero es mejor
                // degradar al nombre GDI que descartar el monitor entero.
                devicePath = gdiName;
                Console.Error.WriteLine(
                    $"MonSelect: no se encontró device path para {gdiName}; " +
                    "su identidad de monitor usa el nombre GDI y no es estable entre reconexiones.");
            }

            result.Add(new MonitorInfo(
                new MonitorId(devicePath),
                gdiName,
                info.rcMonitor,
                info.rcWork,
                (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));

            return true;
        }

        NativeMethods.EnumDisplayMonitors(0, 0, Callback, 0);
        return result;
    }

    public MonitorInfo? GetMonitorForRect(Rect rect)
    {
        var handleRect = rect;
        var handle = NativeMethods.MonitorFromRect(
            ref handleRect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (handle == 0)
            return null;

        var info = MonitorInfoEx.Create();
        if (!NativeMethods.GetMonitorInfoW(handle, ref info))
            return null;

        return GetMonitors().FirstOrDefault(m => m.GdiName == info.szDevice);
    }

    /// <summary>Mapea \\.\DISPLAYn al monitorDevicePath estable de cada salida activa.</summary>
    private static Dictionary<string, string> BuildDevicePathMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!TryQueryDisplayConfig(out var paths, out var pathCount))
        {
            Console.Error.WriteLine(
                "MonSelect: QueryDisplayConfig falló; todos los monitores van a usar " +
                "su nombre GDI como id, que no es estable entre reconexiones.");
            return map;
        }

        for (var i = 0; i < pathCount; i++)
        {
            var source = new DisplayConfigSourceDeviceName
            {
                header = new DisplayConfigDeviceInfoHeader
                {
                    type = DisplayConfigNative.DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)System.Runtime.InteropServices.Marshal
                        .SizeOf<DisplayConfigSourceDeviceName>(),
                    adapterId = paths[i].sourceInfo.adapterId,
                    id = paths[i].sourceInfo.id,
                },
                viewGdiDeviceName = string.Empty,
            };

            var target = new DisplayConfigTargetDeviceName
            {
                header = new DisplayConfigDeviceInfoHeader
                {
                    type = DisplayConfigNative.DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)System.Runtime.InteropServices.Marshal
                        .SizeOf<DisplayConfigTargetDeviceName>(),
                    adapterId = paths[i].targetInfo.adapterId,
                    id = paths[i].targetInfo.id,
                },
                monitorFriendlyDeviceName = string.Empty,
                monitorDevicePath = string.Empty,
            };

            if (DisplayConfigNative.DisplayConfigGetDeviceInfo(ref source) != DisplayConfigNative.ERROR_SUCCESS)
                continue;
            if (DisplayConfigNative.DisplayConfigGetDeviceInfo(ref target) != DisplayConfigNative.ERROR_SUCCESS)
                continue;

            map[source.viewGdiDeviceName] = target.monitorDevicePath;
        }

        return map;
    }

    /// <summary>
    /// Ejecuta GetDisplayConfigBufferSizes + QueryDisplayConfig. Si
    /// QueryDisplayConfig devuelve ERROR_INSUFFICIENT_BUFFER — la topología
    /// cambió entre las dos llamadas, algo documentado como esperable —
    /// reintenta una vez con buffers recién pedidos. Cualquier otro fallo,
    /// o un segundo fallo tras el reintento, devuelve false.
    /// </summary>
    private static bool TryQueryDisplayConfig(out DisplayConfigPathInfo[] paths, out uint pathCount)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (DisplayConfigNative.GetDisplayConfigBufferSizes(
                    DisplayConfigNative.QDC_ONLY_ACTIVE_PATHS,
                    out pathCount, out var modeCount) != DisplayConfigNative.ERROR_SUCCESS)
            {
                paths = [];
                return false;
            }

            paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];

            var result = DisplayConfigNative.QueryDisplayConfig(
                DisplayConfigNative.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount, paths, ref modeCount, modes, 0);

            if (result == DisplayConfigNative.ERROR_SUCCESS)
                return true;

            if (result != DisplayConfigNative.ERROR_INSUFFICIENT_BUFFER)
                return false;

            // Topología cambió entre las dos llamadas; reintentar una vez
            // con buffers nuevos es el remedio documentado.
        }

        paths = [];
        pathCount = 0;
        return false;
    }
}
