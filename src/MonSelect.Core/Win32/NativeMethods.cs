using System.Runtime.InteropServices;

namespace MonSelect.Core.Win32;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfoEx
{
    public int cbSize;
    public Rect rcMonitor;
    public Rect rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;

    public static MonitorInfoEx Create() => new()
    {
        cbSize = Marshal.SizeOf<MonitorInfoEx>(),
        szDevice = string.Empty,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowPlacement
{
    public int length;
    public int flags;
    public int showCmd;
    public Point ptMinPosition;
    public Point ptMaxPosition;
    public Rect rcNormalPosition;

    public static WindowPlacement Create() => new()
    {
        length = Marshal.SizeOf<WindowPlacement>(),
    };
}

internal static class NativeMethods
{
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    internal delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref Rect rect, nint data);

    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfoW(nint hMonitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hwnd, int cmd);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(
        nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(nint hwnd, [Out] char[] buffer, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(nint hwnd, [Out] char[] buffer, int max);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    internal const uint GW_CHILD = 5;
    internal const uint GW_HWNDNEXT = 2;

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hwnd, uint cmd);

    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    internal const int OBJID_WINDOW = 0;
    internal const int CHILDID_SELF = 0;

    internal delegate void WinEventProc(
        nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(
        uint min, uint max, nint module, WinEventProc callback, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(nint hook);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public Point pt;
    }

    [DllImport("user32.dll")]
    internal static extern int GetMessageW(out Msg msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessageW(ref Msg msg);

    [DllImport("user32.dll")]
    internal static extern bool PostThreadMessageW(uint thread, uint msg, nint wParam, nint lParam);

    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_DONOTROUND = 1;

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        nint hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        nint hwnd, int attribute, out Rect value, int size);

    internal const uint WM_TIMER = 0x0113;

    [DllImport("user32.dll")]
    internal static extern nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);

    [DllImport("user32.dll")]
    internal static extern bool KillTimer(nint hWnd, nuint uIDEvent);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
}
