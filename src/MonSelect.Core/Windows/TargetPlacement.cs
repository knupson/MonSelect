using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <param name="ShowCmd">Valor de showCmd que va en WINDOWPLACEMENT.</param>
/// <param name="NormalPosition">Rect de restauración. Determina en qué monitor maximiza Windows.</param>
/// <param name="StripBorders">True sólo para Borderless.</param>
/// <param name="ExpectedBounds">
/// Dónde debería quedar la ventana si la aplicación coopera. El RetryScheduler
/// compara contra esto para decidir si hace falta otro intento.
/// </param>
public sealed record TargetPlacement(
    ShowCommand ShowCmd,
    Rect NormalPosition,
    bool StripBorders,
    Rect ExpectedBounds);
