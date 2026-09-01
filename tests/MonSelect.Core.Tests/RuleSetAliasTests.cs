using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;

namespace MonSelect.Core.Tests;

public class RuleSetAliasTests
{
    private static RuleSet SetWith(params (string Alias, string Path)[] monitors)
        => new(
            1,
            monitors.ToDictionary(
                m => m.Alias,
                m => new MonitorAlias(m.Path, m.Alias),
                StringComparer.OrdinalIgnoreCase),
            Array.Empty<Rule>());

    [Fact]
    public void Finds_the_alias_declared_for_a_monitor_id()
    {
        var set = SetWith(("benq", @"\\?\DISPLAY#BNQ7820#7&1a2b3c4d&0&UID268#{guid}"));

        var alias = set.AliasFor(new MonitorId(@"\\?\DISPLAY#BNQ7820#7&1a2b3c4d&0&UID268#{guid}"));

        Assert.Equal("benq", alias);
    }

    [Fact]
    public void The_lookup_is_case_insensitive_like_MonitorId_equality()
    {
        var set = SetWith(("benq", @"\\?\DISPLAY#BNQ7820#7&1a2b3c4d&0&UID268#{guid}"));

        var alias = set.AliasFor(new MonitorId(@"\\?\DISPLAY#bnq7820#7&1A2B3C4D&0&uid268#{GUID}"));

        Assert.Equal("benq", alias);
    }

    [Fact]
    public void An_unknown_monitor_returns_null()
    {
        var set = SetWith(("benq", @"\\?\DISPLAY#BNQ7820#7&1a2b3c4d&0&UID268#{guid}"));

        Assert.Null(set.AliasFor(new MonitorId(@"\\?\DISPLAY#NOPE0000#1&aaaa&0&UID999#{guid}")));
    }
}
