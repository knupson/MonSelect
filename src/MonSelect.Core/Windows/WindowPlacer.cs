using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <summary>
/// Aplica un <see cref="TargetPlacement"/> a una ventana concreta. No decide
/// nada: el qué lo calculó <see cref="PlacementCalculator"/>, acá sólo está el cómo.
/// </summary>
public sealed class WindowPlacer(IWindowSystem system, StyleStore styles)
{
    public void Apply(nint handle, uint processId, long processStartTicks, TargetPlacement target)
    {
        if (!system.IsWindow(handle))
            return;

        if (target.StripBorders)
            StripFrame(handle, processId, processStartTicks);

        // SetWindowPlacement fija showCmd y rcNormalPosition juntos. Hacerlo en
        // dos pasos haría que la ventana aparezca en el monitor viejo y salte.
        system.SetPlacement(handle, target.ShowCmd, target.NormalPosition);
    }

    /// <summary>Devuelve false si la ventana nunca fue convertida a borderless.</summary>
    public bool Revert(nint handle)
    {
        var record = styles.Forget(handle);
        if (record is null)
            return false;

        if (!system.IsWindow(handle))
            return false;

        system.SetStyle(handle, record.OriginalStyle);
        system.ApplyFrameChange(handle);
        return true;
    }

    private void StripFrame(nint handle, uint processId, long processStartTicks)
    {
        var current = system.GetStyle(handle);

        // Sólo se guarda si todavía tiene marco. Si ya es borderless, el style
        // actual no es el original y guardarlo perdería la posibilidad de revertir.
        if (!StyleMath.IsBorderless(current))
        {
            styles.Remember(new BorderlessRecord(handle, processId, processStartTicks, current));
            styles.Save();
        }

        system.SetStyle(handle, StyleMath.StripBorders(current));

        // Sin SWP_FRAMECHANGED el cambio de style no se refleja: la ventana
        // conserva el área no cliente hasta el próximo recálculo.
        system.ApplyFrameChange(handle);
    }
}
