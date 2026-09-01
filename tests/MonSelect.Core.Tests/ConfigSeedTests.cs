using MonSelect.Core.Rules;
using MonSelect.Core.Tests.Fakes;

namespace MonSelect.Core.Tests;

public class ConfigSeedTests
{
    [Fact]
    public void Seeds_one_alias_per_connected_monitor()
    {
        var set = ConfigSeed.Seed(new FakeMonitorSystem().GetMonitors());

        Assert.Equal(4, set.Monitors.Count);
    }

    [Fact]
    public void Aliases_are_short_lowercase_and_unique()
    {
        var set = ConfigSeed.Seed(new FakeMonitorSystem().GetMonitors());

        Assert.All(set.Monitors.Keys, a => Assert.Equal(a.ToLowerInvariant(), a));
        Assert.Equal(set.Monitors.Count, set.Monitors.Keys.Distinct().Count());
    }

    [Fact]
    public void The_primary_monitor_is_aliased_primary()
    {
        var set = ConfigSeed.Seed(new FakeMonitorSystem().GetMonitors());

        Assert.True(set.Monitors.ContainsKey("primary"));
        Assert.Equal(FakeMonitorSystem.Primary.Id.DevicePath, set.Monitors["primary"].Path);
    }

    [Fact]
    public void Each_alias_carries_the_full_device_path()
    {
        var set = ConfigSeed.Seed(new FakeMonitorSystem().GetMonitors());

        Assert.All(set.Monitors.Values, m => Assert.Contains("DISPLAY#", m.Path));
    }

    [Fact]
    public void The_seed_has_no_rules()
    {
        Assert.Empty(ConfigSeed.Seed(new FakeMonitorSystem().GetMonitors()).Rules);
    }

    [Fact]
    public void Seeding_with_no_monitors_yields_an_empty_set()
    {
        var set = ConfigSeed.Seed(Array.Empty<MonSelect.Core.Monitors.MonitorInfo>());

        Assert.Empty(set.Monitors);
    }
}
