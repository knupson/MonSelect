using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <summary>Lo que el motor necesita saber de una ventana. Existe para poder stubbearlo.</summary>
public interface IWindowDescriber
{
    WindowInfo? Describe(nint handle);
    long StartTicksOf(uint pid);
}

/// <summary>
/// Construye el <see cref="WindowInfo"/> de un hwnd. Cachea exe path y command
/// line por pid: no cambian mientras el proceso viva, y leer el PEB en cada
/// evento sería caro.
/// </summary>
public sealed class WindowProbe(IWindowSystem system) : IWindowDescriber
{
    private readonly Dictionary<uint, (string? Exe, string? CommandLine, long StartTicks)> _byPid = new();

    public WindowInfo? Describe(nint handle)
    {
        if (!system.IsWindow(handle))
            return null;

        NativeMethods.GetWindowThreadProcessId(handle, out var pid);

        var (exe, commandLine, _) = ProcessFacts(pid);
        var style = system.GetStyle(handle);
        var bounds = system.GetBounds(handle);

        return new WindowInfo(
            handle,
            pid,
            exe,
            commandLine,
            ClassName(handle),
            Title(handle),
            Aumid: null, // F1 no lee AppUserModelID; se agrega con el soporte de apps de Store.
            bounds,
            CurrentState(style));
    }

    public long StartTicksOf(uint pid) => ProcessFacts(pid).StartTicks;

    private (string? Exe, string? CommandLine, long StartTicks) ProcessFacts(uint pid)
    {
        if (_byPid.TryGetValue(pid, out var cached))
            return cached;

        var facts = (
            ProcessQuery.GetExePath(pid),
            ProcessQuery.GetCommandLine(pid),
            ProcessQuery.GetStartTicks(pid));

        _byPid[pid] = facts;
        return facts;
    }

    /// <summary>Olvida el cache de un proceso que murió, para no envenenar un pid reciclado.</summary>
    public void ForgetProcess(uint pid) => _byPid.Remove(pid);

    private static WindowState CurrentState(uint style)
    {
        if ((style & (uint)WindowStyles.Minimize) != 0)
            return WindowState.Minimized;

        if ((style & (uint)WindowStyles.Maximize) != 0)
            return StyleMath.IsBorderless(style) ? WindowState.Borderless : WindowState.Maximized;

        return WindowState.Normal;
    }

    private static string ClassName(nint handle)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassNameW(handle, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static string Title(nint handle)
    {
        var length = NativeMethods.GetWindowTextLengthW(handle);
        if (length <= 0)
            return string.Empty;

        var buffer = new char[length + 1];
        var written = NativeMethods.GetWindowTextW(handle, buffer, buffer.Length);
        return written > 0 ? new string(buffer, 0, written) : string.Empty;
    }
}
