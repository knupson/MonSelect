using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.App;

/// <summary>Una fila de la tabla "Ventanas abiertas" y, a la vez, el rectángulo que le corresponde en el mapa.</summary>
internal sealed class OpenWindowRow
{
    public required nint Handle { get; init; }
    public required string Title { get; init; }
    public required string Process { get; init; }
    public required string ExePath { get; init; }
    public required string ClassName { get; init; }
    public required string CommandLine { get; init; }
    public required string MonitorLabel { get; init; }
    public required string StateLabel { get; init; }
    public required string MatchedRule { get; init; }
    public required WindowInfo Info { get; init; }

    /// <summary>
    /// GetVisibleBounds, no GetBounds: es lo que dibuja el mapa y lo que se
    /// escribe en una regla nueva (spec §5, §7 — un rect en este producto es el
    /// rectángulo visible).
    /// </summary>
    public required Rect VisibleBounds { get; init; }
}
