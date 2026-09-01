using System.Globalization;
using System.Windows.Data;

namespace MonSelect.App;

/// <summary>Convierte el AlternationIndex (base 0) de una fila en el número de prioridad que ve el usuario (base 1).</summary>
internal sealed class IndexPlusOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i ? (i + 1).ToString(culture) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
