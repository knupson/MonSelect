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

    /// <summary>
    /// Regresión: un título capturado con "¡Actualizaciones Disponibles!"
    /// llegó a rules.yaml con el "¡" reemplazado por el carácter de
    /// reemplazo Unicode (U+FFFD) — alguna conversión en el camino perdía
    /// bytes no-ASCII. Este test cubre acentos, ¡¿ y un carácter fuera del
    /// BMP (un emoji, que en UTF-16 es un par subrogado) tanto en el título
    /// como en el nombre de la regla, para que una futura regresión de
    /// encoding la agarre acá y no en la máquina del usuario.
    /// </summary>
    [Fact]
    public void Round_trips_non_ascii_spanish_text_and_a_non_bmp_character()
    {
        const string name = "JDownloader 2 🎉 (¡ó, café!)";
        const string title = "JDownloader 2 - ¡Actualizaciones Disponibles! 🎉";

        var set = new RuleSet(
            1,
            new Dictionary<string, MonitorAlias>(),
            new[]
            {
                new Rule(
                    name,
                    new MatchCriteria(Title: title),
                    new RulePlacement(new[] { "display4" }, WindowState.Maximized, null)),
            });

        var dir = Directory.CreateTempSubdirectory("monselect-tests");
        try
        {
            var path = Path.Combine(dir.FullName, "rules.yaml");
            YamlStore.Save(path, set);

            var raw = File.ReadAllText(path);
            Assert.DoesNotContain('�', raw);
            Assert.Contains(name, raw);
            Assert.Contains(title, raw);

            var reloaded = YamlStore.Load(path);
            Assert.Equal(name, reloaded.Rules[0].Name);
            Assert.Equal(title, reloaded.Rules[0].Match.Title);
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
    public void RenderRule_produces_the_same_block_that_Save_would_write_for_that_rule()
    {
        var set = YamlStore.Parse(FullDocument);
        var rule = set.Rules[0];

        var rendered = YamlStore.RenderRule(rule);

        var dir = Directory.CreateTempSubdirectory("monselect-tests");
        try
        {
            var path = Path.Combine(dir.FullName, "rules.yaml");
            YamlStore.Save(path, set with { Rules = new[] { rule } });
            var fullText = File.ReadAllText(path);

            Assert.Contains(rendered.TrimEnd(), fullText);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // --- F2: bleed. "auto" (o ausente) es null; cualquier otro valor,
    // incluido 0, es un número explícito de píxeles que pisa la medición.

    [Fact]
    public void A_rule_without_bleed_defaults_to_auto()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[0];

        Assert.Null(rule.Bleed);
    }

    [Fact]
    public void Bleed_auto_is_the_same_as_omitting_it()
    {
        var set = YamlStore.Parse("""
            version: 1
            rules:
              - name: n
                place:
                  monitor: x
                  state: maximized
                bleed: auto
            """);

        Assert.Null(set.Rules[0].Bleed);
    }

    [Fact]
    public void Bleed_zero_is_an_explicit_override_not_auto()
    {
        var set = YamlStore.Parse("""
            version: 1
            rules:
              - name: n
                place:
                  monitor: x
                  state: maximized
                bleed: 0
            """);

        Assert.Equal(0, set.Rules[0].Bleed);
    }

    [Fact]
    public void An_explicit_bleed_value_is_parsed()
    {
        var set = YamlStore.Parse("""
            version: 1
            rules:
              - name: n
                place:
                  monitor: x
                  state: maximized
                bleed: 2
            """);

        Assert.Equal(2, set.Rules[0].Bleed);
    }

    [Fact]
    public void An_invalid_bleed_value_names_the_offending_rule_and_value()
    {
        var ex = Assert.Throws<RuleSetFormatException>(() => YamlStore.Parse("""
            version: 1
            rules:
              - name: mal bleed
                place:
                  monitor: x
                  state: maximized
                bleed: mucho
            """));

        Assert.Contains("mal bleed", ex.Message);
        Assert.Contains("mucho", ex.Message);
    }

    [Fact]
    public void Bleed_round_trips_through_save_and_load()
    {
        var set = YamlStore.Parse("""
            version: 1
            rules:
              - name: n
                place:
                  monitor: x
                  state: maximized
                bleed: 3
            """);
        var dir = Directory.CreateTempSubdirectory("monselect-tests");
        try
        {
            var path = Path.Combine(dir.FullName, "rules.yaml");
            YamlStore.Save(path, set);
            var reloaded = YamlStore.Load(path);

            Assert.Equal(3, reloaded.Rules[0].Bleed);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Auto_bleed_is_not_written_to_the_file_at_all()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[0]; // Bleed == null (auto)

        var rendered = YamlStore.RenderRule(rule);

        Assert.DoesNotContain("bleed", rendered);
    }

    // --- Validación: rect con el borde derecho o inferior antes que el
    // izquierdo o superior. F1 acceptance: "un rect cuyo right está a la
    // izquierda de su left".

    [Fact]
    public void A_rect_with_right_before_left_is_rejected()
    {
        var ex = Assert.Throws<RuleSetFormatException>(() => YamlStore.Parse("""
            version: 1
            rules:
              - name: rect invertido
                place:
                  monitor: x
                  state: normal
                  rect: [100, 0, 50, 200]
            """));

        Assert.Contains("100", ex.Message);
    }

    [Fact]
    public void A_rect_with_bottom_before_top_is_rejected()
    {
        Assert.Throws<RuleSetFormatException>(() => YamlStore.Parse("""
            version: 1
            rules:
              - name: rect invertido
                place:
                  monitor: x
                  state: normal
                  rect: [0, 200, 100, 50]
            """));
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
        Assert.Equal(expected.Bleed, actual.Bleed);
        Assert.Equal(expected.Match, actual.Match);
        Assert.Equal(expected.Place.State, actual.Place.State);
        Assert.Equal(expected.Place.Rect, actual.Place.Rect);
        Assert.Equal(expected.Place.MonitorAliases, actual.Place.MonitorAliases);
    }
}
