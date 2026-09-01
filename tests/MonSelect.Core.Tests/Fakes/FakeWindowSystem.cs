using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests.Fakes;

/// <summary>
/// Ventanas de mentira que registran lo que se les hizo. Sirve para dos cosas:
/// verificar el orden de las operaciones, y simular apps rebeldes que se
/// vuelven a mover después de que las colocamos.
/// </summary>
public sealed class FakeWindowSystem : IWindowSystem
{
    public sealed class Window
    {
        public Rect Bounds { get; set; }
        public uint Style { get; set; }
        public ShowCommand ShowCmd { get; set; } = ShowCommand.Normal;
        public Rect NormalPosition { get; set; }

        /// <summary>
        /// Rect al que la app se mueve sola después de cada intento, simulando
        /// una app que pelea. Null significa que coopera.
        /// </summary>
        public Rect? FightsBackTo { get; set; }

        /// <summary>Cuántos intentos resiste antes de rendirse.</summary>
        public int FightsForAttempts { get; set; }
    }

    private readonly Dictionary<nint, Window> _windows = new();

    public List<string> Calls { get; } = new();

    public Window Add(nint handle, Rect bounds, uint style)
    {
        var w = new Window { Bounds = bounds, Style = style, NormalPosition = bounds };
        _windows[handle] = w;
        return w;
    }

    public void Remove(nint handle) => _windows.Remove(handle);

    public Window this[nint handle] => _windows[handle];

    public bool IsWindow(nint handle) => _windows.ContainsKey(handle);

    public bool IsVisible(nint handle) => _windows.ContainsKey(handle);

    public Rect GetBounds(nint handle) => _windows[handle].Bounds;

    public uint GetStyle(nint handle) => _windows[handle].Style;

    public void SetStyle(nint handle, uint style)
    {
        Calls.Add($"SetStyle({handle},0x{style:X8})");
        _windows[handle].Style = style;
    }

    public void ApplyFrameChange(nint handle) => Calls.Add($"ApplyFrameChange({handle})");

    public void SetPlacement(nint handle, ShowCommand showCmd, Rect normalPosition)
    {
        Calls.Add($"SetPlacement({handle},{showCmd},{normalPosition})");

        var w = _windows[handle];
        w.ShowCmd = showCmd;
        w.NormalPosition = normalPosition;
        w.Bounds = showCmd == ShowCommand.Normal ? normalPosition : w.Bounds;

        Settle(w);
    }

    public void Show(nint handle, ShowCommand showCmd)
    {
        Calls.Add($"Show({handle},{showCmd})");
        _windows[handle].ShowCmd = showCmd;
    }

    /// <summary>Deja que la ventana se rebele, si el test la configuró para eso.</summary>
    private static void Settle(Window w)
    {
        if (w.FightsBackTo is { } rebel && w.FightsForAttempts > 0)
        {
            w.FightsForAttempts--;
            w.Bounds = rebel;
        }
    }

    /// <summary>
    /// Fuerza los bounds observables y deja que la ventana se rebele, igual que
    /// hace SetPlacement. Es lo que usan los tests de retry como "intento".
    /// </summary>
    public void SetObservedBounds(nint handle, Rect bounds)
    {
        var w = _windows[handle];
        w.Bounds = bounds;
        Settle(w);
    }
}
