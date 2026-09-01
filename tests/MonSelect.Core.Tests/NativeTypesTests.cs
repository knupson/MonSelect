using MonSelect.Core.Win32;

namespace MonSelect.Core.Tests;

public class NativeTypesTests
{
    [Fact]
    public void Rect_computes_width_and_height()
    {
        var r = Rect.FromLtrb(3000, 0, 4920, 1080);
        Assert.Equal(1920, r.Width);
        Assert.Equal(1080, r.Height);
        Assert.False(r.IsEmpty);
    }

    [Fact]
    public void Rect_with_zero_area_is_empty()
    {
        Assert.True(Rect.FromLtrb(10, 10, 10, 400).IsEmpty);
        Assert.True(Rect.FromLtrb(10, 10, 400, 10).IsEmpty);
    }

    // El style medido en la ventana de RustDesk en el spec, seccion 3.3.
    private const uint RustDeskBorderless = 0x150B0000;

    [Fact]
    public void Recognises_the_measured_borderless_style()
    {
        Assert.True(StyleMath.IsBorderless(RustDeskBorderless));
    }

    [Fact]
    public void A_normal_overlapped_window_is_not_borderless()
    {
        uint overlapped = (uint)(WindowStyles.Visible
            | WindowStyles.Caption
            | WindowStyles.ThickFrame);
        Assert.False(StyleMath.IsBorderless(overlapped));
    }

    [Fact]
    public void Stripping_borders_produces_a_borderless_style_and_keeps_the_rest()
    {
        uint overlapped = (uint)(WindowStyles.Visible
            | WindowStyles.Caption
            | WindowStyles.ThickFrame);

        uint stripped = StyleMath.StripBorders(overlapped);

        Assert.True(StyleMath.IsBorderless(stripped));
        Assert.Equal((uint)WindowStyles.Visible, stripped & (uint)WindowStyles.Visible);
    }

    [Fact]
    public void Stripping_borders_twice_changes_nothing_further()
    {
        uint once = StyleMath.StripBorders(0x00CF0000);
        Assert.Equal(once, StyleMath.StripBorders(once));
    }
}
