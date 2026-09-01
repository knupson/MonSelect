using System.Windows.Media;

namespace MonSelect.App;

/// <summary>
/// Paleta y tipografía de la GUI: grafito frío con un ámbar de calibración como
/// acento. Un solo lugar para los valores hex, para que el mapa de escritorio
/// (dibujado a mano en <see cref="DesktopMap"/>) y el resto de la ventana
/// (definido en XAML) no se desincronicen.
/// </summary>
internal static class Tokens
{
    public const string InkHex = "#0E1116";
    public const string PanelHex = "#161B22";
    public const string LineHex = "#2A313B";
    public const string TextHex = "#D6DCE4";
    public const string MutedHex = "#7C8899";
    public const string AccentHex = "#E8A33D";
    public const string MatchHex = "#5FBF8F";
    public const string AlarmHex = "#D2635A";

    public static readonly SolidColorBrush Ink = Brush(InkHex);
    public static readonly SolidColorBrush Panel = Brush(PanelHex);
    public static readonly SolidColorBrush Line = Brush(LineHex);
    public static readonly SolidColorBrush Text = Brush(TextHex);
    public static readonly SolidColorBrush Muted = Brush(MutedHex);
    public static readonly SolidColorBrush Accent = Brush(AccentHex);
    public static readonly SolidColorBrush Match = Brush(MatchHex);
    public static readonly SolidColorBrush Alarm = Brush(AlarmHex);

    /// <summary>Relleno tenue del acento, para el interior de una ventana con regla.</summary>
    public static readonly SolidColorBrush AccentFill = Brush("#33E8A33D");

    public const string UiFont = "Segoe UI Variable, Segoe UI";
    public const string MonoFont = "Cascadia Mono, Consolas";

    private static SolidColorBrush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
