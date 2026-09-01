using MonSelect.Core.Engine;
using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class RuleEngineTests : IDisposable
{
    private const string Exe = @"C:\Program Files\RustDesk\rustdesk.exe";

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("monselect-engine");
    private readonly FakeWindowSystem _windows = new();
    private readonly FakeMonitorSystem _monitors = new();

    public void Dispose() => _dir.Delete(recursive: true);

    private sealed class NoDelay : IDelay
    {
        public Task WaitAsync(int milliseconds, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Probe de mentira: devuelve el WindowInfo que el test decida.</summary>
    private sealed class StubProbe(Dictionary<nint, WindowInfo> windows) : IWindowDescriber
    {
        public WindowInfo? Describe(nint handle)
            => windows.TryGetValue(handle, out var info) ? info : null;

        public long StartTicksOf(uint pid) => 1;
    }

    private static RuleSet SetWith(params Rule[] rules) => new(
        1,
        new Dictionary<string, MonitorAlias>(StringComparer.OrdinalIgnoreCase)
        {
            ["benq"] = new(FakeMonitorSystem.Right.Id.DevicePath, "BenQ"),
            ["vertical"] = new(FakeMonitorSystem.Vertical.Id.DevicePath, "LG"),
            ["fantasma"] = new(FakeMonitorSystem.Disconnected.Id.DevicePath, "No conectado"),
        },
        rules);

    private (RuleEngine Engine, ApplyLog Log) Build(
        RuleSet set, Dictionary<nint, WindowInfo> described)
    {
        var styles = new StyleStore(Path.Combine(_dir.FullName, "borderless.json"));
        var log = new ApplyLog();
        var engine = new RuleEngine(
            new StubProbe(described),
            new MonitorRegistry(_monitors),
            new WindowPlacer(_windows, styles),
            new RetryScheduler(_windows, new NoDelay()),
            log);

        engine.UpdateRules(set);
        return (engine, log);
    }

    private WindowInfo Describe(nint handle, string title = "RustDesk")
        => new(handle, 100, Exe, "--connect 1", "RustdeskMultiWindow", title, null,
               Rect.FromLtrb(100, 100, 900, 700), WindowState.Normal);

    private static Rule MakeRule(
        string name,
        WindowState state = WindowState.Maximized,
        ApplyMode apply = ApplyMode.All,
        IfMissing ifMissing = IfMissing.Skip,
        IReadOnlyList<string>? monitors = null)
        => new(name,
               new MatchCriteria(Exe: Exe),
               new RulePlacement(monitors ?? new[] { "benq" }, state),
               true,
               apply,
               ifMissing,
               new[] { 0 });

    [Fact]
    public async Task A_window_with_no_matching_rule_is_left_alone()
    {
        _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        var described = new Dictionary<nint, WindowInfo>
        {
            [1] = Describe(1) with { ExePath = @"C:\Windows\notepad.exe" },
        };
        var (engine, _) = Build(SetWith(MakeRule("rustdesk")), described);

        Assert.Equal(ApplyResult.NoMatch, await engine.HandleAsync(1, CancellationToken.None));
        Assert.Empty(_windows.Calls);
    }

    [Fact]
    public async Task A_matching_rule_places_the_window_and_logs_it()
    {
        _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        _windows.SetObservedBounds(1, FakeMonitorSystem.Right.WorkArea);
        var (engine, log) = Build(
            SetWith(MakeRule("rustdesk")),
            new Dictionary<nint, WindowInfo> { [1] = Describe(1) });

        var result = await engine.HandleAsync(1, CancellationToken.None);

        Assert.Equal(ApplyResult.Applied, result);
        Assert.Contains(log.Recent(), e => e.RuleName == "rustdesk" && e.Result == ApplyResult.Applied);
    }

    [Fact]
    public async Task A_missing_monitor_with_skip_does_not_place_the_window()
    {
        _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        var (engine, log) = Build(
            SetWith(MakeRule("fantasma", monitors: new[] { "fantasma" })),
            new Dictionary<nint, WindowInfo> { [1] = Describe(1) });

        Assert.Equal(ApplyResult.Skipped, await engine.HandleAsync(1, CancellationToken.None));
        Assert.Empty(_windows.Calls);
        Assert.Contains(log.Recent(), e => e.Result == ApplyResult.Skipped);
    }

    [Fact]
    public async Task A_missing_monitor_with_primary_falls_back()
    {
        _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        _windows.SetObservedBounds(1, FakeMonitorSystem.Primary.WorkArea);
        var (engine, _) = Build(
            SetWith(MakeRule("fantasma", ifMissing: IfMissing.Primary, monitors: new[] { "fantasma" })),
            new Dictionary<nint, WindowInfo> { [1] = Describe(1) });

        Assert.Equal(ApplyResult.Applied, await engine.HandleAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task An_alias_that_is_not_declared_is_skipped_and_logged()
    {
        _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        var (engine, log) = Build(
            SetWith(MakeRule("typo", monitors: new[] { "beqn" })),
            new Dictionary<nint, WindowInfo> { [1] = Describe(1) });

        Assert.Equal(ApplyResult.Skipped, await engine.HandleAsync(1, CancellationToken.None));
        Assert.Contains(log.Recent(), e => e.Detail is not null && e.Detail.Contains("beqn"));
    }

    [Fact]
    public async Task Apply_all_places_every_matching_window()
    {
        foreach (nint h in new nint[] { 1, 2, 3 })
        {
            _windows.Add(h, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
            _windows.SetObservedBounds(h, FakeMonitorSystem.Right.WorkArea);
        }

        var described = new Dictionary<nint, WindowInfo>
        {
            [1] = Describe(1), [2] = Describe(2), [3] = Describe(3),
        };
        var (engine, _) = Build(SetWith(MakeRule("todas")), described);

        foreach (nint h in new nint[] { 1, 2, 3 })
            Assert.Equal(ApplyResult.Applied, await engine.HandleAsync(h, CancellationToken.None));
    }

    [Fact]
    public async Task Apply_first_only_places_the_first_window_of_a_process()
    {
        foreach (nint h in new nint[] { 1, 2 })
        {
            _windows.Add(h, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
            _windows.SetObservedBounds(h, FakeMonitorSystem.Right.WorkArea);
        }

        var described = new Dictionary<nint, WindowInfo> { [1] = Describe(1), [2] = Describe(2) };
        var (engine, _) = Build(SetWith(MakeRule("primera", apply: ApplyMode.First)), described);

        Assert.Equal(ApplyResult.Applied, await engine.HandleAsync(1, CancellationToken.None));
        Assert.Equal(ApplyResult.Ignored, await engine.HandleAsync(2, CancellationToken.None));
    }

    [Fact]
    public async Task Apply_rotate_cycles_through_the_monitor_list()
    {
        foreach (nint h in new nint[] { 1, 2, 3 })
            _windows.Add(h, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);

        var described = new Dictionary<nint, WindowInfo>
        {
            [1] = Describe(1), [2] = Describe(2), [3] = Describe(3),
        };
        var (engine, log) = Build(
            SetWith(MakeRule("rotando", apply: ApplyMode.Rotate, monitors: new[] { "benq", "vertical" })),
            described);

        foreach (nint h in new nint[] { 1, 2, 3 })
            await engine.HandleAsync(h, CancellationToken.None);

        var details = log.Recent().Select(e => e.Detail ?? "").ToList();
        Assert.Contains(details, d => d.Contains("DISPLAY4"));
        Assert.Contains(details, d => d.Contains("DISPLAY3"));
        // La tercera ventana recicla al primer monitor de la lista.
        Assert.Equal(2, details.Count(d => d.Contains("DISPLAY4")));
    }

    [Fact]
    public async Task A_window_that_never_settles_is_logged_as_resisted()
    {
        var window = _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        window.FightsBackTo = Rect.FromLtrb(100, 100, 900, 700);
        window.FightsForAttempts = 99;

        var (engine, log) = Build(
            SetWith(MakeRule("rebelde") with { RetryMs = new[] { 0, 0 } }),
            new Dictionary<nint, WindowInfo> { [1] = Describe(1) });

        Assert.Equal(ApplyResult.Resisted, await engine.HandleAsync(1, CancellationToken.None));
        Assert.Contains(log.Recent(), e => e.Result == ApplyResult.Resisted && e.Attempts == 2);
    }

    [Fact]
    public void The_log_keeps_only_the_most_recent_entries()
    {
        var log = new ApplyLog(capacity: 3);
        for (var i = 0; i < 10; i++)
            log.Add(new ApplyEntry(DateTimeOffset.Now, i, $"w{i}", null, ApplyResult.NoMatch, 0, null));

        Assert.Equal(3, log.Recent().Count);
        Assert.Equal("w9", log.Recent()[^1].Title);
    }
}
