using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <summary>
/// Aplica un <see cref="TargetPlacement"/> a una ventana concreta. No decide
/// nada: el qué lo calculó <see cref="PlacementCalculator"/>, acá sólo está el cómo.
/// </summary>
public sealed class WindowPlacer(IWindowSystem system, StyleStore styles)
{
    /// <param name="bleed">
    /// Píxeles que la propia app se come de su rect visible dibujando su
    /// propio borde (F2). Se expande el rect pedido por este tanto en las
    /// cuatro puntas antes de la conversión a rect externo — son dos
    /// correcciones distintas que se componen, no una sola: ésta arregla lo
    /// que la app le hace a su contenido; <see cref="ToOuterRect"/> arregla lo
    /// que DWM le agrega a la ventana entera.
    /// </param>
    public void Apply(
        nint handle, uint processId, long processStartTicks, TargetPlacement target, int bleed = 0)
    {
        if (!system.IsWindow(handle))
            return;

        if (target.StripBorders)
            StripFrame(handle, processId, processStartTicks);

        // SetWindowPlacement fija showCmd y rcNormalPosition juntos. Hacerlo en
        // dos pasos haría que la ventana aparezca en el monitor viejo y salte.
        // Una ventana colocada en un rect exacto se quiere pegada a algo: al
        // borde del monitor o a otra ventana. El redondeo de Win11 deja ver el
        // escritorio justo en esas juntas.
        if (target.ShowCmd == ShowCommand.Normal)
            system.SetSquareCorners(handle);

        var wanted = ExpandForBleed(target.NormalPosition, bleed);
        system.SetPlacement(handle, target.ShowCmd, ToOuterRect(handle, wanted));
    }

    /// <summary>
    /// Expande el rect pedido por <paramref name="bleed"/> píxeles a cada
    /// lado: si la app se come 1px de borde propio en las cuatro puntas, hay
    /// que pedirle a Windows 1px más de lo escrito en la regla para que el
    /// contenido termine exactamente donde la regla dice.
    /// </summary>
    internal static Rect ExpandForBleed(Rect rect, int bleed)
        => bleed == 0
            ? rect
            : Rect.FromLtrb(
                rect.Left - bleed, rect.Top - bleed, rect.Right + bleed, rect.Bottom + bleed);

    /// <summary>Devuelve false si la ventana nunca fue convertida a borderless.</summary>
    public bool Revert(nint handle)
    {
        if (!system.IsWindow(handle))
            return false;

        var record = styles.Forget(handle);
        if (record is null)
            return false;

        system.SetStyle(handle, record.OriginalStyle);
        system.ApplyFrameChange(handle);
        return true;
    }

    /// <summary>
    /// Revierte todas las ventanas borderless que <see cref="StyleStore"/> tiene
    /// registradas. Pensado para el ítem de menú de bandeja: sin esto, un
    /// borderless nunca tiene vuelta atrás desde la app (spec §9). Debe correr
    /// en el hilo dueño de la mutación de ventanas — ver <c>WindowWatcher.Post</c>.
    /// </summary>
    /// <returns>Cuántas ventanas se restauraron de verdad.</returns>
    public int RevertAll()
    {
        var restored = 0;
        var changed = false;

        // Instantánea: Revert() y el descarte de abajo mutan styles por dentro
        // (styles.Forget), y styles.All() expone la colección viva.
        foreach (var record in styles.All().ToList())
        {
            var handle = (nint)record.Handle;

            if (Revert(handle))
            {
                restored++;
                changed = true;
                continue;
            }

            // La ventana ya no existe: el registro es basura acumulada, se
            // descarta en silencio en vez de quedar para siempre en el store.
            if (!system.IsWindow(handle) && styles.Forget(record.Handle) is not null)
                changed = true;
        }

        if (changed)
            styles.Save();

        return restored;
    }

    /// <summary>
    /// Un rect escrito por una persona describe lo que se ve. Windows posiciona
    /// por el rect externo, que incluye el marco invisible de DWM (7px a los
    /// lados y abajo en Win11). Sin esta conversión, una ventana pegada al borde
    /// del monitor deja una franja de escritorio visible — que es exactamente lo
    /// que el snap de Windows no hace.
    /// </summary>
    private Rect ToOuterRect(nint handle, Rect wanted)
    {
        var outer = system.GetBounds(handle);
        var visible = system.GetVisibleBounds(handle);

        if (outer.IsEmpty || visible.IsEmpty)
            return wanted;

        return Rect.FromLtrb(
            wanted.Left - (visible.Left - outer.Left),
            wanted.Top - (visible.Top - outer.Top),
            wanted.Right + (outer.Right - visible.Right),
            wanted.Bottom + (outer.Bottom - visible.Bottom));
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
