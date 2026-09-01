using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

public sealed class Win32WindowSystem : IWindowSystem
{
    private const uint FrameChangeFlags =
        (uint)(SetWindowPosFlags.NoMove | SetWindowPosFlags.NoSize
               | SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate
               | SetWindowPosFlags.FrameChanged);

    public bool IsWindow(nint handle) => NativeMethods.IsWindow(handle);

    public bool IsVisible(nint handle) => NativeMethods.IsWindowVisible(handle);

    public Rect GetBounds(nint handle)
        => NativeMethods.GetWindowRect(handle, out var rect) ? rect : default;

    public void SetSquareCorners(nint handle)
    {
        var preference = NativeMethods.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    public Rect GetVisibleBounds(nint handle)
    {
        // Si DWM no sabe responder (ventana sin composición), el rect externo es
        // la mejor aproximación disponible y el ajuste queda en cero.
        var size = System.Runtime.InteropServices.Marshal.SizeOf<Rect>();
        return NativeMethods.DwmGetWindowAttribute(
            handle, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out var visible, size) == 0
            ? visible
            : GetBounds(handle);
    }

    public uint GetStyle(nint handle)
        => (uint)NativeMethods.GetWindowLongPtr(handle, GwlIndex.Style).ToInt64();

    public void SetStyle(nint handle, uint style)
        => NativeMethods.SetWindowLongPtr(handle, GwlIndex.Style, (nint)style);

    public void ApplyFrameChange(nint handle)
        => NativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0, FrameChangeFlags);

    public void SetPlacement(nint handle, ShowCommand showCmd, Rect normalPosition)
    {
        var placement = WindowPlacement.Create();
        if (!NativeMethods.GetWindowPlacement(handle, ref placement))
            return;

        placement.showCmd = (int)showCmd;
        placement.rcNormalPosition = normalPosition;

        NativeMethods.SetWindowPlacement(handle, ref placement);
    }

    public void Show(nint handle, ShowCommand showCmd)
        => NativeMethods.ShowWindow(handle, (int)showCmd);
}
