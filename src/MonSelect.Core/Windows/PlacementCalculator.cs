using MonSelect.Core.Monitors;
using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <summary>
/// Traduce "este monitor, este estado" a los valores concretos que consume
/// SetWindowPlacement. Puro y sin efectos: es la aritmética que más fácil se
/// equivoca y la que más barato sale testear.
/// </summary>
public static class PlacementCalculator
{
    /// <summary>
    /// Si rcNormalPosition vive en coordenadas de workspace en vez de pantalla,
    /// hay que restar el offset del área de trabajo antes de escribirlo.
    /// El valor sale de la verificación empírica de la Task 7; ver
    /// docs/superpowers/findings/windowplacement-coordinates.md.
    /// </summary>
    public const bool WorkspaceOffsetApplies = false;

    public static TargetPlacement Compute(
        MonitorInfo monitor,
        WindowState state,
        Rect? explicitRect,
        Rect currentBounds)
    {
        return state switch
        {
            WindowState.Borderless => new TargetPlacement(
                ShowCommand.Maximized,
                ToPlacementSpace(CentreOn(monitor.WorkArea, currentBounds), monitor),
                StripBorders: true,
                // Sin caption ni thickframe, una ventana maximizada se expande
                // al monitor completo y no al área de trabajo. Es la firma que
                // se midió en RustDesk (spec, sección 3.3).
                ExpectedBounds: monitor.Bounds),

            WindowState.Maximized => new TargetPlacement(
                ShowCommand.Maximized,
                ToPlacementSpace(CentreOn(monitor.WorkArea, currentBounds), monitor),
                StripBorders: false,
                ExpectedBounds: monitor.WorkArea),

            WindowState.Minimized => new TargetPlacement(
                ShowCommand.Minimized,
                ToPlacementSpace(CentreOn(monitor.WorkArea, currentBounds), monitor),
                StripBorders: false,
                // Minimizada no tiene bounds observables; el retry no compara.
                ExpectedBounds: Rect.FromLtrb(0, 0, 0, 0)),

            WindowState.Normal => NormalPlacement(monitor, explicitRect, currentBounds),

            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Estado desconocido."),
        };
    }

    private static TargetPlacement NormalPlacement(
        MonitorInfo monitor, Rect? explicitRect, Rect currentBounds)
    {
        var rect = explicitRect ?? CentreOn(monitor.WorkArea, currentBounds);

        return new TargetPlacement(
            ShowCommand.Normal,
            ToPlacementSpace(rect, monitor),
            StripBorders: false,
            ExpectedBounds: rect);
    }

    /// <summary>
    /// Coloca un rect del tamaño de <paramref name="size"/> centrado dentro de
    /// <paramref name="area"/>, recortándolo si no entra.
    /// </summary>
    private static Rect CentreOn(Rect area, Rect size)
    {
        var w = Math.Min(size.Width, area.Width);
        var h = Math.Min(size.Height, area.Height);

        var left = area.Left + (area.Width - w) / 2;
        var top = area.Top + (area.Height - h) / 2;

        return Rect.FromLtrb(left, top, left + w, top + h);
    }

    private static Rect ToPlacementSpace(Rect screenRect, MonitorInfo monitor)
    {
        if (!WorkspaceOffsetApplies)
            return screenRect;

#pragma warning disable CS0162
        var dx = monitor.WorkArea.Left - monitor.Bounds.Left;
        var dy = monitor.WorkArea.Top - monitor.Bounds.Top;

        return Rect.FromLtrb(
            screenRect.Left - dx,
            screenRect.Top - dy,
            screenRect.Right - dx,
            screenRect.Bottom - dy);
#pragma warning restore CS0162
    }
}
