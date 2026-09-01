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
    public void Maximized_sets_placement_without_touching_the_style()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: false));

        Assert.Equal(Overlapped, _system[Hwnd].Style);
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

    [Fact]
    public void Applying_to_a_dead_handle_does_nothing_instead_of_throwing()
    {
        _placer.Apply(9999, 1, 1, Target(ShowCommand.Maximized, strip: false));

        Assert.Empty(_system.Calls);
    }
}
