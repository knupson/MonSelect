using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class WindowToRuleTests
{
    private static WindowInfo MakeWindow(WindowState state = WindowState.Normal)
        => new(
            Handle: 42,
            ProcessId: 100,
            ExePath: @"C:\Program Files\RustDesk\rustdesk.exe",
            CommandLine: "--connect 123456789",
            ClassName: "RustdeskMultiWindow",
            Title: "WK-EJEMPLO-01 - Remote Desktop - RustDesk",
            Aumid: null,
            Bounds: Rect.FromLtrb(3000, 0, 4920, 1080),
            CurrentState: state);

    [Fact]
    public void Matches_on_exe_and_class_by_default()
    {
        var rule = WindowToRule.Convert(
            MakeWindow(), Rect.FromLtrb(3000, 0, 4920, 1080), "benq", "RustDesk",
            includeCommandLine: false, includeTitle: false);

        Assert.Equal(@"C:\Program Files\RustDesk\rustdesk.exe", rule.Match.Exe);
        Assert.Equal("RustdeskMultiWindow", rule.Match.ClassName);
        Assert.Null(rule.Match.CommandLine);
        Assert.Null(rule.Match.Title);
    }

    [Fact]
    public void Includes_command_line_only_when_asked()
    {
        var rule = WindowToRule.Convert(
            MakeWindow(), Rect.FromLtrb(3000, 0, 4920, 1080), "benq", "RustDesk",
            includeCommandLine: true, includeTitle: false);

        Assert.Equal("--connect 123456789", rule.Match.CommandLine);
    }

    [Fact]
    public void Derives_an_anchored_escaped_regex_from_the_title_when_no_override_is_given()
    {
        var window = MakeWindow() with { Title = "Weird [Title] (with) special.chars" };

        var rule = WindowToRule.Convert(
            window, Rect.FromLtrb(3000, 0, 4920, 1080), "benq", "n",
            includeCommandLine: false, includeTitle: true);

        Assert.Equal(WindowToRule.DefaultTitleRegex(window.Title), rule.Match.Title);
        Assert.Matches(rule.Match.Title!, window.Title);
    }

    [Fact]
    public void A_title_override_is_used_verbatim_instead_of_the_derived_regex()
    {
        var rule = WindowToRule.Convert(
            MakeWindow(), Rect.FromLtrb(3000, 0, 4920, 1080), "benq", "n",
            includeCommandLine: false, includeTitle: true, titleRegex: "^WK-EJEMPLO-01.*");

        Assert.Equal("^WK-EJEMPLO-01.*", rule.Match.Title);
    }

    [Fact]
    public void The_alias_and_state_are_taken_from_the_window()
    {
        var rule = WindowToRule.Convert(
            MakeWindow(WindowState.Maximized), Rect.FromLtrb(3000, 0, 4920, 1080), "benq", "n",
            includeCommandLine: false, includeTitle: false);

        Assert.Equal(new[] { "benq" }, rule.Place.MonitorAliases);
        Assert.Equal(WindowState.Maximized, rule.Place.State);
    }

    [Fact]
    public void A_normal_window_captures_the_visible_bounds_not_the_outer_bounds()
    {
        var visible = Rect.FromLtrb(3057, 253, 4486, 857);

        var rule = WindowToRule.Convert(
            MakeWindow(WindowState.Normal), visible, "benq", "n",
            includeCommandLine: false, includeTitle: false);

        Assert.Equal(visible, rule.Place.Rect);
    }

    [Theory]
    [InlineData(WindowState.Maximized)]
    [InlineData(WindowState.Minimized)]
    [InlineData(WindowState.Borderless)]
    public void Only_normal_windows_carry_an_explicit_rect(WindowState state)
    {
        var rule = WindowToRule.Convert(
            MakeWindow(state), Rect.FromLtrb(3057, 253, 4486, 857), "benq", "n",
            includeCommandLine: false, includeTitle: false);

        Assert.Null(rule.Place.Rect);
    }

    [Fact]
    public void The_rule_name_comes_from_the_caller_not_the_window_title()
    {
        var rule = WindowToRule.Convert(
            MakeWindow(), Rect.FromLtrb(3000, 0, 4920, 1080), "benq", "RustDesk EJEMPLO-01",
            includeCommandLine: false, includeTitle: false);

        Assert.Equal("RustDesk EJEMPLO-01", rule.Name);
    }

    // --- F2/F3: la captura guiada tiene que reproducir exactamente los mismos
    // píxeles al aplicar la regla. El rect que se guarda es el visible
    // CAPTURADO encogido por el bleed medido (WindowPlacer.ExpandForBleed lo
    // vuelve a expandir al aplicar), y el bleed se graba explícito — no
    // "auto" — para no depender de una remedición futura.

    [Fact]
    public void A_captured_rect_is_shrunk_by_the_measured_bleed()
    {
        var captured = Rect.FromLtrb(1919, -843, 3001, 103); // lo que se ve, borde incluido

        var rule = WindowToRule.Convert(
            MakeWindow(), captured, "benq", "n",
            includeCommandLine: false, includeTitle: false, bleed: 1);

        Assert.Equal(Rect.FromLtrb(1920, -842, 3000, 102), rule.Place.Rect);
    }

    [Fact]
    public void The_measured_bleed_is_recorded_explicitly_on_the_rule_not_as_auto()
    {
        var rule = WindowToRule.Convert(
            MakeWindow(), Rect.FromLtrb(3000, 0, 4920, 1080), "benq", "n",
            includeCommandLine: false, includeTitle: false, bleed: 2);

        Assert.Equal(2, rule.Bleed);
    }

    [Fact]
    public void Zero_bleed_leaves_the_captured_rect_untouched()
    {
        var captured = Rect.FromLtrb(3000, 0, 4920, 1080);

        var rule = WindowToRule.Convert(
            MakeWindow(), captured, "benq", "n",
            includeCommandLine: false, includeTitle: false, bleed: 0);

        Assert.Equal(captured, rule.Place.Rect);
        Assert.Equal(0, rule.Bleed);
    }

    [Fact]
    public void An_unresolved_monitor_alias_throws_instead_of_writing_a_broken_rule()
    {
        Assert.Throws<ArgumentException>(() => WindowToRule.Convert(
            MakeWindow(), Rect.FromLtrb(3000, 0, 4920, 1080), monitorAlias: "",
            ruleName: "n", includeCommandLine: false, includeTitle: false));
    }
}
