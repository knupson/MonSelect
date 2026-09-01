using System.Runtime.InteropServices;

namespace MonSelect.App;

internal static class TopLevelWindows
{
    private delegate bool EnumProc(nint hwnd, nint param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, nint param);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(nint hwnd);

    public static IEnumerable<nint> Enumerate()
    {
        var found = new List<nint>();

        EnumWindows((hwnd, _) =>
        {
            // Sin título visible no es una ventana que le importe al usuario.
            if (IsWindowVisible(hwnd) && GetWindowTextLengthW(hwnd) > 0)
                found.Add(hwnd);

            return true;
        }, 0);

        return found;
    }
}
