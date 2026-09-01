using System.Runtime.InteropServices;

namespace MonSelect.Core.Win32;

/// <summary>Rectángulo en coordenadas de pantalla, layout compatible con RECT de Win32.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static Rect FromLtrb(int left, int top, int right, int bottom)
        => new(left, top, right, bottom);

    public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Point(int X, int Y);

[Flags]
public enum WindowStyles : uint
{
    Popup = 0x80000000,
    Child = 0x40000000,
    Minimize = 0x20000000,
    Visible = 0x10000000,
    ClipSiblings = 0x04000000,
    Maximize = 0x01000000,
    Caption = 0x00C00000,
    Border = 0x00800000,
    DlgFrame = 0x00400000,
    SysMenu = 0x00080000,
    ThickFrame = 0x00040000,
    MinimizeBox = 0x00020000,
    MaximizeBox = 0x00010000,
}

public enum ShowCommand
{
    Hide = 0,
    Normal = 1,
    Minimized = 2,
    Maximized = 3,
    Restore = 9,
}

[Flags]
public enum SetWindowPosFlags : uint
{
    NoSize = 0x0001,
    NoMove = 0x0002,
    NoZOrder = 0x0004,
    NoActivate = 0x0010,
    FrameChanged = 0x0020,
}

public static class GwlIndex
{
    public const int Style = -16;
    public const int ExStyle = -20;
}

/// <summary>
/// Operaciones sobre el style de una ventana. Se aísla acá para poder testearla
/// sin ventanas reales: es la parte del borderless que más fácil se rompe.
/// </summary>
public static class StyleMath
{
    private const uint BorderBits = (uint)(WindowStyles.Caption | WindowStyles.ThickFrame);

    /// <summary>Quita caption y thickframe, dejando el resto del style intacto.</summary>
    public static uint StripBorders(uint style) => style & ~BorderBits;

    /// <summary>True si la ventana no tiene ni caption ni thickframe.</summary>
    public static bool IsBorderless(uint style) => (style & BorderBits) == 0;
}
