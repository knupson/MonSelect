using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class PlacementCalculatorTests
{
    // Ventana chica sentada en el monitor primario, antes de ser movida.
    private static readonly Rect Current = Rect.FromLtrb(100, 100, 900, 700);

    [Fact]
    public void Maximized_targets_the_work_area_so_the_taskbar_stays_visible()
    {
        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Maximized, null, Current);

        Assert.Equal(ShowCommand.Maximized, result.ShowCmd);
        Assert.Equal(FakeMonitorSystem.Right.WorkArea, result.ExpectedBounds);
        Assert.False(result.StripBorders);
    }

    [Fact]
    public void Maximized_puts_the_restore_rect_inside_the_target_monitor()
    {
        // Windows maximiza donde la ventana ya está, así que rcNormalPosition
        // tiene que caer dentro del monitor destino o la ventana se maximiza
        // en el monitor equivocado.
        var target = FakeMonitorSystem.Right;

        var result = PlacementCalculator.Compute(target, WindowState.Maximized, null, Current);

        Assert.True(result.NormalPosition.Left >= target.WorkArea.Left);
        Assert.True(result.NormalPosition.Top >= target.WorkArea.Top);
        Assert.True(result.NormalPosition.Right <= target.WorkArea.Right);
        Assert.True(result.NormalPosition.Bottom <= target.WorkArea.Bottom);
    }

    [Fact]
    public void Maximized_preserves_the_current_window_size_in_the_restore_rect()
    {
        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Maximized, null, Current);

        Assert.Equal(Current.Width, result.NormalPosition.Width);
        Assert.Equal(Current.Height, result.NormalPosition.Height);
    }

    [Fact]
    public void Borderless_targets_the_full_monitor_and_strips_the_frame()
    {
        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Borderless, null, Current);

        Assert.Equal(ShowCommand.Maximized, result.ShowCmd);
        Assert.Equal(FakeMonitorSystem.Right.Bounds, result.ExpectedBounds);
        Assert.True(result.StripBorders);
    }

    [Fact]
    public void Borderless_reproduces_the_rect_measured_on_RustDesk()
    {
        // Spec seccion 3.3: (3000,0)-(4920,1080), tapando la taskbar.
        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Borderless, null, Current);

        Assert.Equal(Rect.FromLtrb(3000, 0, 4920, 1080), result.ExpectedBounds);
    }

    [Fact]
    public void Minimized_still_moves_the_restore_rect_to_the_target_monitor()
    {
        // Al restaurarla, la ventana tiene que aparecer en el monitor de la regla.
        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Vertical, WindowState.Minimized, null, Current);

        Assert.Equal(ShowCommand.Minimized, result.ShowCmd);
        Assert.True(result.NormalPosition.Left >= FakeMonitorSystem.Vertical.WorkArea.Left);
        Assert.True(result.NormalPosition.Top >= FakeMonitorSystem.Vertical.WorkArea.Top);
    }

    [Fact]
    public void Normal_without_an_explicit_rect_centres_the_current_size_on_the_monitor()
    {
        var target = FakeMonitorSystem.Right;

        var result = PlacementCalculator.Compute(target, WindowState.Normal, null, Current);

        Assert.Equal(ShowCommand.Normal, result.ShowCmd);
        Assert.Equal(Current.Width, result.NormalPosition.Width);
        Assert.Equal(Current.Height, result.NormalPosition.Height);

        var expectedLeft = target.WorkArea.Left + (target.WorkArea.Width - Current.Width) / 2;
        Assert.Equal(expectedLeft, result.NormalPosition.Left);
    }

    [Fact]
    public void Normal_with_an_explicit_rect_uses_it_verbatim()
    {
        var wanted = Rect.FromLtrb(3100, 50, 4000, 800);

        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Normal, wanted, Current);

        Assert.Equal(wanted, result.NormalPosition);
        Assert.Equal(wanted, result.ExpectedBounds);
    }

    [Fact]
    public void Works_on_a_monitor_whose_origin_is_negative()
    {
        var target = FakeMonitorSystem.Above; // (0,-1080)-(1920,0)

        var result = PlacementCalculator.Compute(target, WindowState.Maximized, null, Current);

        Assert.Equal(target.WorkArea, result.ExpectedBounds);
        Assert.True(result.NormalPosition.Top < 0);
        Assert.True(result.NormalPosition.Bottom <= target.WorkArea.Bottom);
    }

    [Fact]
    public void A_window_larger_than_the_target_monitor_is_clamped_to_the_work_area()
    {
        var huge = Rect.FromLtrb(0, 0, 4000, 3000);
        var target = FakeMonitorSystem.Right;

        var result = PlacementCalculator.Compute(target, WindowState.Normal, null, huge);

        Assert.True(result.NormalPosition.Width <= target.WorkArea.Width);
        Assert.True(result.NormalPosition.Height <= target.WorkArea.Height);
        Assert.True(result.NormalPosition.Left >= target.WorkArea.Left);
        Assert.True(result.NormalPosition.Top >= target.WorkArea.Top);
    }

    [Fact]
    public void An_explicit_rect_is_ignored_for_states_other_than_normal()
    {
        var wanted = Rect.FromLtrb(3100, 50, 4000, 800);

        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Maximized, wanted, Current);

        Assert.Equal(FakeMonitorSystem.Right.WorkArea, result.ExpectedBounds);
    }

    [Fact]
    public void The_computation_is_deterministic()
    {
        var a = PlacementCalculator.Compute(FakeMonitorSystem.Right, WindowState.Maximized, null, Current);
        var b = PlacementCalculator.Compute(FakeMonitorSystem.Right, WindowState.Maximized, null, Current);

        Assert.Equal(a, b);
    }
}
