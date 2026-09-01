using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class YamlStoreTests
{
    private const string FullDocument = """
        version: 1
        monitors:
          benq:
            path: '\\?\DISPLAY#BNQ7820#1&aaaa&0&UID268#{guid}'
            label: "BenQ (derecha)"
          vertical:
            path: '\\?\DISPLAY#GSM57EE#1&aaaa&0&UID264#{guid}'
            label: "LG (vertical)"
        defaults:
          if_missing: skip
          retry_ms: [0, 150, 400, 800]
        rules:
          - name: RustDesk
            enabled: true
            match:
              exe: "C:/Program Files/RustDesk/rustdesk.exe"
              cmdline: "--connect 123456789"
              class: RustdeskMultiWindow
              title: "^WK-EJEMPLO-01.*"
            place:
              monitor: benq
              state: borderless
            apply: all
          - name: Chrome rotando
            match:
              exe: "C:/Program Files/Google/Chrome/Application/chrome.exe"
            place:
              monitor: [benq, vertical]
              state: maximized
            apply: rotate
            if_missing: primary
            retry_ms: [0, 500]
        """;

    [Fact]
    public void Parses_monitor_aliases()
    {
        var set = YamlStore.Parse(FullDocument);

        Assert.Equal(2, set.Monitors.Count);
        Assert.Equal("BenQ (derecha)", set.Monitors["benq"].Label);
        Assert.Contains("UID268", set.Monitors["benq"].Path);
    }

    [Fact]
    public void Parses_all_match_criteria()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[0];

        Assert.Equal("C:/Program Files/RustDesk/rustdesk.exe", rule.Match.Exe);
        Assert.Equal("--connect 123456789", rule.Match.CommandLine);
        Assert.Equal("RustdeskMultiWindow", rule.Match.ClassName);
        Assert.Equal("^WK-EJEMPLO-01.*", rule.Match.Title);
        Assert.Null(rule.Match.Aumid);
    }

    [Fact]
    public void A_single_monitor_becomes_a_one_element_list()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[0];

        Assert.Equal(new[] { "benq" }, rule.Place.MonitorAliases);
        Assert.Equal(WindowState.Borderless, rule.Place.State);
    }

    [Fact]
    public void A_monitor_list_is_preserved_in_order()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[1];

        Assert.Equal(new[] { "benq", "vertical" }, rule.Place.MonitorAliases);
        Assert.Equal(ApplyMode.Rotate, rule.Apply);
    }

    [Fact]
    public void Rules_default_to_enabled_all_and_the_global_defaults()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[0];

        Assert.True(rule.Enabled);
        Assert.Equal(ApplyMode.All, rule.Apply);
        Assert.Equal(IfMissing.Skip, rule.IfMissing);
        Assert.Equal(new[] { 0, 150, 400, 800 }, rule.RetryMs);
    }

    [Fact]
    public void A_rule_overrides_the_global_defaults()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[1];

        Assert.Equal(IfMissing.Primary, rule.IfMissing);
        Assert.Equal(new[] { 0, 500 }, rule.RetryMs);
    }

    [Fact]
    public void Round_trips_through_save_and_load()
    {
        var original = YamlStore.Parse(FullDocument);
        var dir = Directory.CreateTempSubdirectory("monselect-tests");
        try
        {
            var path = Path.Combine(dir.FullName, "rules.yaml");
            YamlStore.Save(path, original);
            var reloaded = YamlStore.Load(path);

            Assert.Equal(original.Monitors.Count, reloaded.Monitors.Count);
            Assert.Equal(original.Rules.Count, reloaded.Rules.Count);

            foreach (var (alias, monitor) in original.Monitors)
                Assert.Equal(monitor, reloaded.Monitors[alias]);

            for (var i = 0; i < original.Rules.Count; i++)
                AssertRuleEqual(original.Rules[i], reloaded.Rules[i]);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Round_trips_values_that_need_yaml_escaping()
    {
        const string yaml = """
            version: 1
            monitors:
              benq:
                path: '\\?\DISPLAY#BNQ7820#1&aaaa&0&UID268#{guid}'
                label: "Monitor con \\ backslash"
            rules:
              - name: "Regla con \"comillas\" adentro"
                match:
                  title: "^WK-\\d+\\s*$"
                place:
                  monitor: benq
                  state: normal
            """;

        var original = YamlStore.Parse(yaml);
        var dir = Directory.CreateTempSubdirectory("monselect-tests");
        try
        {
            var path = Path.Combine(dir.FullName, "rules.yaml");
            YamlStore.Save(path, original);
            var reloaded = YamlStore.Load(path);

            Assert.Equal("Monitor con \\ backslash", reloaded.Monitors["benq"].Label);
            Assert.Equal("Regla con \"comillas\" adentro", reloaded.Rules[0].Name);
            Assert.Equal("^WK-\\d+\\s*$", reloaded.Rules[0].Match.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_yaml_throws_a_readable_error()
    {
        var ex = Assert.Throws<RuleSetFormatException>(
            () => YamlStore.Parse("rules:\n  - name: roto\n   place: mal indentado\n"));

        Assert.Contains("rules.yaml", ex.Message);
    }

    [Fact]
    public void An_unknown_state_names_the_offending_value()
    {
        var ex = Assert.Throws<RuleSetFormatException>(() => YamlStore.Parse("""
            version: 1
            rules:
              - name: estado invalido
                place:
                  monitor: benq
                  state: pantalla-completa
            """));

        Assert.Contains("pantalla-completa", ex.Message);
    }

    [Fact]
    public void An_empty_document_yields_an_empty_rule_set()
    {
        var set = YamlStore.Parse("version: 1\n");

        Assert.Empty(set.Rules);
        Assert.Empty(set.Monitors);
    }

    /// <summary>
    /// Compares every field of two <see cref="Rule"/> values. Not a plain
    /// Assert.Equal(expected, actual): the record-generated Equals compares
    /// RetryMs and Place.MonitorAliases via EqualityComparer&lt;IReadOnlyList&lt;T&gt;&gt;.Default,
    /// which falls back to reference equality for arrays, so two lists with
    /// identical elements from different Save/Load instances would compare
    /// unequal. Comparing each collection field directly instead lets xunit's
    /// own Assert.Equal apply its structural (element-by-element) comparison.
    /// </summary>
    private static void AssertRuleEqual(Rule expected, Rule actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.Apply, actual.Apply);
        Assert.Equal(expected.IfMissing, actual.IfMissing);
        Assert.Equal(expected.RetryMs, actual.RetryMs);
        Assert.Equal(expected.Match, actual.Match);
        Assert.Equal(expected.Place.State, actual.Place.State);
        Assert.Equal(expected.Place.Rect, actual.Place.Rect);
        Assert.Equal(expected.Place.MonitorAliases, actual.Place.MonitorAliases);
    }
}
