using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class WindowPlacerTests : IDisposable
{
    private const nint Hwnd = 1234;
    private const uint Overlapped = 0x00CF0000; // caption + thickframe + botones

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("monselect-placer");
    private readonly FakeWindowSystem _system = new();
    private readonly StyleStore _styles;
    private readonly WindowPlacer _placer;

    public WindowPlacerTests()
    {
        _styles = new StyleStore(Path.Combine(_dir.FullName, "borderless.json"));
        _placer = new WindowPlacer(_system, _styles);
        _system.Add(Hwnd, Rect.FromLtrb(100, 100, 900, 700), Overlapped);
    }

    public void Dispose() => _dir.Delete(recursive: true);

    private static TargetPlacement Target(
        ShowCommand cmd, bool strip, Rect? normal = null, Rect? expected = null)
        => new(cmd,
               normal ?? Rect.FromLtrb(3100, 200, 3900, 800),
               strip,
               expected ?? Rect.FromLtrb(3000, 0, 4920, 1048));

    [Fact]
    public void Maximized_sets_placement_without_calling_SetStyle_directly()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: false));

        // WindowPlacer no llama SetStyle para Maximized: el bit WS_MAXIMIZE que
        // termina prendido acá es un efecto secundario de SetWindowPlacement en
        // el propio Win32 (lo reproduce el fake), no algo que WindowPlacer haga.
        Assert.Equal(Overlapped | (uint)WindowStyles.Maximize, _system[Hwnd].Style);
        Assert.Contains(_system.Calls, c => c.StartsWith("SetPlacement("));
        Assert.DoesNotContain(_system.Calls, c => c.StartsWith("SetStyle("));
    }

    [Fact]
    public void Borderless_strips_the_frame_before_setting_placement()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));

        var setStyle = _system.Calls.FindIndex(c => c.StartsWith("SetStyle("));
        var frame = _system.Calls.FindIndex(c => c.StartsWith("ApplyFrameChange("));
        var placement = _system.Calls.FindIndex(c => c.StartsWith("SetPlacement("));

        Assert.True(setStyle >= 0 && frame > setStyle && placement > frame,
            $"Orden incorrecto: {string.Join(" -> ", _system.Calls)}");
    }

    [Fact]
    public void Borderless_leaves_the_window_without_caption_or_thickframe()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));

        Assert.True(StyleMath.IsBorderless(_system[Hwnd].Style));
    }

    [Fact]
    public void Borderless_records_the_original_style_for_reverting()
    {
        _placer.Apply(Hwnd, 23340, 638000000000, Target(ShowCommand.Maximized, strip: true));

        Assert.True(_styles.TryGet(Hwnd, out var record));
        Assert.Equal(Overlapped, record.OriginalStyle);
        Assert.Equal(23340u, record.ProcessId);
    }

    [Fact]
    public void Applying_borderless_twice_keeps_the_first_original_style()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));

        _styles.TryGet(Hwnd, out var record);
        Assert.Equal(Overlapped, record.OriginalStyle);
    }

    [Fact]
    public void Revert_restores_the_original_style_and_clears_the_record()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));

        Assert.True(_placer.Revert(Hwnd));
        Assert.Equal(Overlapped, _system[Hwnd].Style);
        Assert.False(_styles.TryGet(Hwnd, out _));
    }

    [Fact]
    public void Revert_on_an_untouched_window_reports_that_it_did_nothing()
    {
        Assert.False(_placer.Revert(Hwnd));
    }

    [Fact]
    public void Minimized_moves_the_window_before_minimising_it()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Minimized, strip: false));

        // El rect de restauración tiene que quedar seteado, así al restaurarla
        // aparece en el monitor de la regla y no donde estaba antes.
        Assert.Equal(Rect.FromLtrb(3100, 200, 3900, 800), _system[Hwnd].NormalPosition);
        Assert.Equal(ShowCommand.Minimized, _system[Hwnd].ShowCmd);
    }

    // --- F2: compensación del borde que la propia app dibuja adentro de su
    // rect visible (bleed). Expande el rect PEDIDO antes de la conversión al
    // rect externo de DWM: son dos correcciones distintas que se componen.

    [Fact]
    public void Bleed_expands_the_wanted_rect_on_all_four_sides_before_the_DWM_conversion()
    {
        // Sin marco DWM (FrameInset = 0 por default): el bleed es la única
        // corrección en juego, así que el rect pedido a Windows tiene que
        // salir exactamente expandido por el bleed en las cuatro puntas.
        var target = Target(ShowCommand.Normal, strip: false, normal: Rect.FromLtrb(1920, -842, 3000, 102));

        _placer.Apply(Hwnd, 1, 1, target, bleed: 1);

        Assert.Equal(Rect.FromLtrb(1919, -843, 3001, 103), _system[Hwnd].NormalPosition);
    }

    [Fact]
    public void Zero_bleed_leaves_the_wanted_rect_untouched()
    {
        var target = Target(ShowCommand.Normal, strip: false, normal: Rect.FromLtrb(1920, -842, 3000, 102));

        _placer.Apply(Hwnd, 1, 1, target, bleed: 0);

        Assert.Equal(Rect.FromLtrb(1920, -842, 3000, 102), _system[Hwnd].NormalPosition);
    }

    [Fact]
    public void Bleed_and_the_DWM_frame_conversion_compose_instead_of_one_overriding_the_other()
    {
        // El marco invisible de DWM se mide contra los bounds ACTUALES de la
        // ventana (antes de moverla) — no cambia por el bleed, que sólo toca
        // el rect pedido. Con 7px de marco a los lados y abajo (como en Win11
        // real) y 1px de bleed, el rect final tiene que reflejar los dos.
        _system[Hwnd].FrameInset = 7;
        var target = Target(ShowCommand.Normal, strip: false, normal: Rect.FromLtrb(1920, -842, 3000, 102));

        _placer.Apply(Hwnd, 1, 1, target, bleed: 1);

        // Rect pedido expandido por bleed: (1919,-843)-(3001,103).
        // GetBounds/GetVisibleBounds de Hwnd (100,100)-(900,700) con
        // FrameInset 7 dan un offset de 7px a izquierda/derecha/abajo, 0 arriba.
        Assert.Equal(Rect.FromLtrb(1912, -843, 3008, 110), _system[Hwnd].NormalPosition);
    }

    [Fact]
    public void Applying_to_a_dead_handle_does_nothing_instead_of_throwing()
    {
        _placer.Apply(9999, 1, 1, Target(ShowCommand.Maximized, strip: false));

        Assert.Empty(_system.Calls);
    }

    [Fact]
    public void Reverting_a_dead_window_preserves_the_record_for_later_recovery()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));
        Assert.True(_styles.TryGet(Hwnd, out _)); // Confirm record exists

        _system.Remove(Hwnd); // Window dies

        Assert.False(_placer.Revert(Hwnd)); // Returns false because window is gone
        Assert.True(_styles.TryGet(Hwnd, out _)); // Record still exists, not consumed
    }

    // --- Defecto 3: nada en la app llamaba WindowPlacer.Revert. RevertAll es
    // lo que el menú de bandeja invoca para revertir todo lo registrado.

    private const nint OtherHwnd = 5678;

    [Fact]
    public void RevertAll_restores_every_live_borderless_window_and_reports_how_many()
    {
        _system.Add(OtherHwnd, Rect.FromLtrb(200, 200, 1000, 800), Overlapped);
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));
        _placer.Apply(OtherHwnd, 2, 2, Target(ShowCommand.Maximized, strip: true));

        Assert.Equal(2, _placer.RevertAll());

        Assert.False(_styles.TryGet(Hwnd, out _));
        Assert.False(_styles.TryGet(OtherHwnd, out _));
        Assert.Equal(Overlapped, _system[Hwnd].Style);
        Assert.Equal(Overlapped, _system[OtherHwnd].Style);
    }

    [Fact]
    public void RevertAll_discards_records_for_windows_that_no_longer_exist()
    {
        _system.Add(OtherHwnd, Rect.FromLtrb(200, 200, 1000, 800), Overlapped);
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));
        _placer.Apply(OtherHwnd, 2, 2, Target(ShowCommand.Maximized, strip: true));

        _system.Remove(Hwnd); // esta ventana ya no existe

        Assert.Equal(1, _placer.RevertAll()); // sólo OtherHwnd contó como restaurada

        Assert.False(_styles.TryGet(Hwnd, out _), "el registro basura tiene que descartarse igual");
        Assert.False(_styles.TryGet(OtherHwnd, out _));
    }

    [Fact]
    public void RevertAll_persists_the_cleared_store_to_disk()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));
        _placer.RevertAll();

        // Una StyleStore nueva sobre el mismo archivo prueba que se persistió,
        // no sólo que se limpió el diccionario en memoria de este test.
        var reloaded = new StyleStore(Path.Combine(_dir.FullName, "borderless.json"));
        reloaded.Load();

        Assert.False(reloaded.TryGet(Hwnd, out _));
    }

    [Fact]
    public void RevertAll_on_an_empty_store_does_nothing_and_reports_zero()
    {
        Assert.Equal(0, _placer.RevertAll());
    }
}
