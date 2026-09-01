using System.Globalization;
using System.Windows.Data;

namespace MonSelect.App;

/// <summary>
/// Acorta un path o command line largo dejando la COLA visible: el nombre del
/// ejecutable, al final de "C:\Program Files\...\rustdesk.exe", es lo que
/// identifica el programa — no el prefijo "C:\Program Files\". El texto
/// completo va al ToolTip de la celda, bindeado aparte contra la propiedad sin
/// recortar.
/// </summary>
internal sealed class TailTruncateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrEmpty(text))
            return string.Empty;

        var max = parameter is string p && int.TryParse(p, out var n) ? n : 40;
        if (text.Length <= max)
            return text;

        return "…" + text[^(max - 1)..];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
