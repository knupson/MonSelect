using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Rules;

/// <summary>Qué hacer cuando una regla matchea varias ventanas.</summary>
public enum ApplyMode
{
    /// <summary>Aplicar a cada ventana que matchee.</summary>
    All,

    /// <summary>Aplicar sólo a la primera ventana mientras el proceso viva.</summary>
    First,

    /// <summary>Recorrer la lista de monitores, uno por ventana, reciclando al agotarla.</summary>
    Rotate,
}

/// <param name="MonitorAliases">
/// Alias definidos en el bloque monitors. Un solo alias para All y First;
/// la lista ordenada que recorre Rotate.
/// </param>
/// <param name="Rect">Sólo se usa con <see cref="WindowState.Normal"/>.</param>
public sealed record RulePlacement(
    IReadOnlyList<string> MonitorAliases,
    WindowState State,
    Rect? Rect = null);
