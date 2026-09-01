using MonSelect.Core.Monitors;
using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;

namespace MonSelect.Core.Tests;

public class MonitorRegistryTests
{
    private static MonitorRegistry NewRegistry() => new(new FakeMonitorSystem());

    [Fact]
    public void Resolves_a_connected_monitor_by_device_path()
    {
        var found = NewRegistry().Resolve(
            FakeMonitorSystem.Right.Id, IfMissing.Skip, Rect.FromLtrb(0, 0, 100, 100));

        Assert.NotNull(found);
        Assert.Equal(@"\\.\DISPLAY4", found!.GdiName);
    }

    [Fact]
    public void Device_paths_compare_case_insensitively()
    {
        var upper = new MonitorId(FakeMonitorSystem.Right.Id.DevicePath.ToUpperInvariant());

        Assert.NotNull(NewRegistry().Resolve(upper, IfMissing.Skip, default));
    }

    [Fact]
    public void Skip_returns_null_when_the_monitor_is_absent()
    {
        var found = NewRegistry().Resolve(
            FakeMonitorSystem.Disconnected.Id, IfMissing.Skip, Rect.FromLtrb(0, 0, 100, 100));

        Assert.Null(found);
    }

    [Fact]
    public void Primary_policy_falls_back_to_the_primary_monitor()
    {
        var found = NewRegistry().Resolve(
            FakeMonitorSystem.Disconnected.Id, IfMissing.Primary, Rect.FromLtrb(0, 0, 100, 100));

        Assert.NotNull(found);
        Assert.True(found!.IsPrimary);
    }

    [Fact]
    public void Nearest_policy_picks_the_monitor_closest_to_the_anchor()
    {
        // Ancla dentro de DISPLAY4, a la derecha del todo.
        var anchor = Rect.FromLtrb(4000, 400, 4400, 700);

        var found = NewRegistry().Resolve(FakeMonitorSystem.Disconnected.Id, IfMissing.Nearest, anchor);

        Assert.NotNull(found);
        Assert.Equal(@"\\.\DISPLAY4", found!.GdiName);
    }

    [Fact]
    public void Nearest_policy_handles_anchors_in_negative_coordinate_space()
    {
        // Ancla dentro de DISPLAY2, que vive enteramente en Y negativo.
        var anchor = Rect.FromLtrb(200, -900, 600, -600);

        var found = NewRegistry().Resolve(FakeMonitorSystem.Disconnected.Id, IfMissing.Nearest, anchor);

        Assert.NotNull(found);
        Assert.Equal(@"\\.\DISPLAY2", found!.GdiName);
    }

    [Fact]
    public void Primary_returns_the_flagged_monitor()
    {
        Assert.Equal(@"\\.\DISPLAY1", NewRegistry().Primary()!.GdiName);
    }

    [Fact]
    public void Primary_returns_null_when_no_monitor_is_flagged()
    {
        var registry = new MonitorRegistry(
            new FakeMonitorSystem(new[] { FakeMonitorSystem.Right }));

        Assert.Null(registry.Primary());
    }
}
