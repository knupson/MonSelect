using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class RuleMatcherTests
{
    private static WindowInfo RustDesk(
        string? exe = @"C:\Program Files\RustDesk\rustdesk.exe",
        string? cmdline = @"""C:\Program Files\RustDesk\rustdesk.exe"" --connect 123456789",
        string className = "RustdeskMultiWindow",
        string title = "WK-EJEMPLO-01 - Remote Desktop - RustDesk",
        string? aumid = null)
        => new(
            Handle: 1234,
            ProcessId: 23340,
            ExePath: exe,
            CommandLine: cmdline,
            ClassName: className,
            Title: title,
            Aumid: aumid,
            Bounds: Rect.FromLtrb(3000, 0, 4920, 1080),
            CurrentState: WindowState.Maximized);

    private static Rule RuleWith(string name, MatchCriteria match)
        => new(name, match, new RulePlacement(new[] { "benq" }, WindowState.Borderless));

    [Fact]
    public void An_empty_criteria_matches_anything()
    {
        Assert.True(RuleMatcher.Matches(RuleWith("todo", MatchCriteria.Any), RustDesk()));
    }

    [Fact]
    public void Exe_paths_compare_case_insensitively_and_normalise_separators()
    {
        var rule = RuleWith("exe", new MatchCriteria(
            Exe: "c:/program files/rustdesk/RUSTDESK.EXE"));

        Assert.True(RuleMatcher.Matches(rule, RustDesk()));
    }

    [Fact]
    public void A_different_exe_does_not_match()
    {
        var rule = RuleWith("exe", new MatchCriteria(Exe: @"C:\Windows\notepad.exe"));

        Assert.False(RuleMatcher.Matches(rule, RustDesk()));
    }

    [Fact]
    public void Cmdline_matches_as_a_substring_by_default()
    {
        Assert.True(RuleMatcher.Matches(
            RuleWith("cmd", new MatchCriteria(CommandLine: "--connect 123456789")), RustDesk()));
    }

    [Fact]
    public void Cmdline_wrapped_in_slashes_is_a_regex()
    {
        Assert.True(RuleMatcher.Matches(
            RuleWith("cmd", new MatchCriteria(CommandLine: @"/--connect \d+/")), RustDesk()));
    }

    [Fact]
    public void A_criterion_on_a_missing_field_never_matches()
    {
        // Command line ausente: proceso elevado o de otro usuario.
        var rule = RuleWith("cmd", new MatchCriteria(CommandLine: "--connect 123456789"));

        Assert.False(RuleMatcher.Matches(rule, RustDesk(cmdline: null)));
    }

    [Fact]
    public void Class_name_compares_exactly()
    {
        Assert.True(RuleMatcher.Matches(
            RuleWith("c", new MatchCriteria(ClassName: "RustdeskMultiWindow")), RustDesk()));
        Assert.False(RuleMatcher.Matches(
            RuleWith("c", new MatchCriteria(ClassName: "Rustdesk")), RustDesk()));
    }

    [Fact]
    public void Title_is_always_a_regex()
    {
        Assert.True(RuleMatcher.Matches(
            RuleWith("t", new MatchCriteria(Title: "^WK-EJEMPLO-01.*")), RustDesk()));
        Assert.False(RuleMatcher.Matches(
            RuleWith("t", new MatchCriteria(Title: "^OTRA-MAQUINA")), RustDesk()));
    }

    [Fact]
    public void An_invalid_title_regex_never_matches_instead_of_throwing()
    {
        var rule = RuleWith("t", new MatchCriteria(Title: "[sin-cerrar"));

        Assert.False(RuleMatcher.Matches(rule, RustDesk()));
    }

    [Fact]
    public void All_present_criteria_must_hold()
    {
        var rule = RuleWith("and", new MatchCriteria(
            Exe: @"C:\Program Files\RustDesk\rustdesk.exe",
            ClassName: "RustdeskMultiWindow",
            Title: "^NO-COINCIDE"));

        Assert.False(RuleMatcher.Matches(rule, RustDesk()));
    }

    [Fact]
    public void The_first_matching_rule_wins_regardless_of_specificity()
    {
        var rules = new[]
        {
            RuleWith("generica", new MatchCriteria(Exe: @"C:\Program Files\RustDesk\rustdesk.exe")),
            RuleWith("especifica", new MatchCriteria(
                Exe: @"C:\Program Files\RustDesk\rustdesk.exe",
                CommandLine: "--connect 123456789")),
        };

        Assert.Equal("generica", RuleMatcher.FirstMatch(rules, RustDesk())!.Name);
    }

    [Fact]
    public void Disabled_rules_are_skipped()
    {
        var rules = new[]
        {
            RuleWith("apagada", MatchCriteria.Any) with { Enabled = false },
            RuleWith("prendida", MatchCriteria.Any),
        };

        Assert.Equal("prendida", RuleMatcher.FirstMatch(rules, RustDesk())!.Name);
    }

    [Fact]
    public void No_matching_rule_returns_null()
    {
        var rules = new[] { RuleWith("nada", new MatchCriteria(Exe: @"C:\Windows\notepad.exe")) };

        Assert.Null(RuleMatcher.FirstMatch(rules, RustDesk()));
    }

    [Fact]
    public void Aumid_compares_exactly_when_present()
    {
        var rule = RuleWith("uwp", new MatchCriteria(Aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));

        Assert.True(RuleMatcher.Matches(
            rule, RustDesk(aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")));
        Assert.False(RuleMatcher.Matches(rule, RustDesk(aumid: null)));
    }
}
