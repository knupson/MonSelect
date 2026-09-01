using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

/// <summary>
/// F1: el editor de reglas valida antes de guardar y muestra el mensaje en el
/// lugar. Estas pruebas cubren el motor de validación puro, no la GUI.
/// </summary>
public class RuleValidationTests
{
    private static readonly RuleSet Set = new(
        1,
        new Dictionary<string, MonitorAlias>(StringComparer.OrdinalIgnoreCase)
        {
            ["benq"] = new(@"\\?\DISPLAY#BNQ7820#1&aaaa&0&UID268#{guid}", "BenQ"),
            ["vertical"] = new(@"\\?\DISPLAY#GSM57EE#1&aaaa&0&UID264#{guid}", "LG"),
        },
        Array.Empty<Rule>());

    private static Rule ValidRule(string name = "n") => new(
        name,
        new MatchCriteria(Exe: @"C:\app.exe"),
        new RulePlacement(new[] { "benq" }, WindowState.Maximized));

    [Fact]
    public void A_well_formed_rule_has_no_errors()
    {
        Assert.Empty(RuleValidation.Validate(ValidRule(), Set));
    }

    [Fact]
    public void An_unknown_monitor_alias_is_reported_by_name()
    {
        var rule = ValidRule() with { Place = new RulePlacement(new[] { "typo" }, WindowState.Maximized) };

        var errors = RuleValidation.Validate(rule, Set);

        Assert.Contains(errors, e => e.Contains("typo"));
    }

    [Fact]
    public void An_invalid_title_regex_is_reported()
    {
        var rule = ValidRule() with { Match = new MatchCriteria(Title: "(unclosed") };

        var errors = RuleValidation.Validate(rule, Set);

        Assert.Contains(errors, e => e.Contains("regex"));
    }

    [Fact]
    public void A_valid_title_regex_has_no_error()
    {
        var rule = ValidRule() with { Match = new MatchCriteria(Title: "^WK-\\d+$") };

        Assert.Empty(RuleValidation.Validate(rule, Set));
    }

    [Fact]
    public void Rotate_with_fewer_than_two_monitors_is_rejected()
    {
        var rule = ValidRule() with
        {
            Apply = ApplyMode.Rotate,
            Place = new RulePlacement(new[] { "benq" }, WindowState.Maximized),
        };

        var errors = RuleValidation.Validate(rule, Set);

        Assert.Contains(errors, e => e.Contains("rotate"));
    }

    [Fact]
    public void Rotate_with_two_or_more_monitors_is_accepted()
    {
        var rule = ValidRule() with
        {
            Apply = ApplyMode.Rotate,
            Place = new RulePlacement(new[] { "benq", "vertical" }, WindowState.Maximized),
        };

        Assert.Empty(RuleValidation.Validate(rule, Set));
    }

    [Fact]
    public void An_inverted_rect_is_rejected_with_the_YamlStore_message()
    {
        var rule = ValidRule() with
        {
            Place = new RulePlacement(new[] { "benq" }, WindowState.Normal, Rect.FromLtrb(100, 0, 50, 200)),
        };

        var errors = RuleValidation.Validate(rule, Set);

        Assert.Contains(errors, e => e.Contains("right") && e.Contains("left"));
    }

    [Fact]
    public void A_well_formed_rect_has_no_error()
    {
        var rule = ValidRule() with
        {
            Place = new RulePlacement(new[] { "benq" }, WindowState.Normal, Rect.FromLtrb(0, 0, 100, 100)),
        };

        Assert.Empty(RuleValidation.Validate(rule, Set));
    }

    [Fact]
    public void A_blank_name_is_rejected()
    {
        var errors = RuleValidation.Validate(ValidRule(name: "  "), Set);

        Assert.Contains(errors, e => e.Contains("nombre"));
    }

    [Fact]
    public void No_monitor_at_all_is_rejected()
    {
        var rule = ValidRule() with { Place = new RulePlacement(Array.Empty<string>(), WindowState.Maximized) };

        var errors = RuleValidation.Validate(rule, Set);

        Assert.Contains(errors, e => e.Contains("monitor"));
    }
}
