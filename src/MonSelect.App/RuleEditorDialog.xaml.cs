using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Rect = MonSelect.Core.Win32.Rect;
using CoreWindowState = MonSelect.Core.Windows.WindowState;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

namespace MonSelect.App;

/// <summary>
/// F1: editor completo de una regla existente. Cada campo de <see cref="Rule"/>
/// tiene su control; validar antes de guardar reusa <see cref="RuleValidation"/>
/// (que a su vez reusa los mensajes de <see cref="YamlStore"/>) y se muestra en
/// el lugar. La columna derecha responde en vivo, con cada cambio, "¿a qué
/// ventanas abiertas les pega esto ahora mismo?" — RuleMatcher.Matches contra
/// las ventanas reales, no una simulación. No muta ninguna ventana: sólo lee.
/// </summary>
internal partial class RuleEditorDialog : Window
{
    private readonly Bootstrap _bootstrap;
    private readonly string _originalName;
    private bool _initializing = true;

    /// <summary>La regla editada, lista para reemplazar a la original, si el usuario guardó.</summary>
    public Rule? Result { get; private set; }

    public RuleEditorDialog(Bootstrap bootstrap, Rule rule)
    {
        _bootstrap = bootstrap;
        _originalName = rule.Name;

        InitializeComponent();
        TitleText.Text = $"Editar regla — {rule.Name}";

        StateCombo.ItemsSource = Enum.GetValues<CoreWindowState>();
        ApplyCombo.ItemsSource = Enum.GetValues<ApplyMode>();
        IfMissingCombo.ItemsSource = Enum.GetValues<IfMissing>();

        var aliases = string.Join(", ", _bootstrap.CurrentRuleSet.Monitors.Keys);
        KnownAliasesText.Text = aliases.Length > 0
            ? $"Alias declarados: {aliases}. Varios, separados por coma y en orden, si apply es rotate."
            : "No hay monitores declarados en el bloque monitors: de rules.yaml.";

        Load(rule);
        _initializing = false;
        UpdatePreview();
    }

    private void Load(Rule rule)
    {
        NameBox.Text = rule.Name;
        EnabledCheck.IsChecked = rule.Enabled;

        SetCriterion(ExeCheck, ExeBox, rule.Match.Exe);
        SetCriterion(CmdlineCheck, CmdlineBox, rule.Match.CommandLine);
        SetCriterion(ClassCheck, ClassBox, rule.Match.ClassName);
        SetCriterion(TitleCheck, TitleBox, rule.Match.Title);
        SetCriterion(AumidCheck, AumidBox, rule.Match.Aumid);

        MonitorsBox.Text = string.Join(", ", rule.Place.MonitorAliases);
        StateCombo.SelectedItem = rule.Place.State;
        ApplyCombo.SelectedItem = rule.Apply;
        IfMissingCombo.SelectedItem = rule.IfMissing;
        RetryBox.Text = string.Join(", ", rule.EffectiveRetryMs);

        var rect = rule.Place.Rect;
        RectLeftBox.Text = rect?.Left.ToString() ?? "";
        RectTopBox.Text = rect?.Top.ToString() ?? "";
        RectRightBox.Text = rect?.Right.ToString() ?? "";
        RectBottomBox.Text = rect?.Bottom.ToString() ?? "";
        UpdateRectEnabled();

        switch (rule.Bleed)
        {
            case null:
                BleedAutoRadio.IsChecked = true;
                break;
            case 0:
                BleedNeverRadio.IsChecked = true;
                break;
            default:
                BleedFixedRadio.IsChecked = true;
                BleedFixedBox.Text = rule.Bleed.Value.ToString();
                break;
        }
    }

    private static void SetCriterion(CheckBox check, TextBox box, string? value)
    {
        check.IsChecked = value is not null;
        box.Text = value ?? "";
        box.IsEnabled = value is not null;
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        SyncCriterionEnabled();
        UpdatePreview();
    }

    private void State_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateRectEnabled();
        UpdatePreview();
    }

    private void UpdateRectEnabled()
    {
        var isNormal = StateCombo.SelectedItem is CoreWindowState.Normal;
        RectLeftBox.IsEnabled = isNormal;
        RectTopBox.IsEnabled = isNormal;
        RectRightBox.IsEnabled = isNormal;
        RectBottomBox.IsEnabled = isNormal;
    }

    private void SyncCriterionEnabled()
    {
        ExeBox.IsEnabled = ExeCheck.IsChecked == true;
        CmdlineBox.IsEnabled = CmdlineCheck.IsChecked == true;
        ClassBox.IsEnabled = ClassCheck.IsChecked == true;
        TitleBox.IsEnabled = TitleCheck.IsChecked == true;
        AumidBox.IsEnabled = AumidCheck.IsChecked == true;
        BleedFixedBox.IsEnabled = BleedFixedRadio.IsChecked == true;
    }

    // ---------------------------------------------------------------
    // Construir la regla candidata a partir de los controles
    // ---------------------------------------------------------------

    private static string? Optional(CheckBox check, TextBox box)
        => check.IsChecked == true && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;

    /// <summary>
    /// Arma la regla que reflejan los controles ahora mismo. No tira: un campo
    /// numérico ilegible (rect, retry, bleed fijo) se reporta como error de
    /// parseo en <paramref name="parseErrors"/> en vez de tumbar la vista previa.
    /// </summary>
    private Rule Build(List<string> parseErrors)
    {
        var name = NameBox.Text.Trim();
        var match = new MatchCriteria(
            Optional(ExeCheck, ExeBox),
            Optional(CmdlineCheck, CmdlineBox),
            Optional(ClassCheck, ClassBox),
            Optional(TitleCheck, TitleBox),
            Optional(AumidCheck, AumidBox));

        var aliases = MonitorsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        var state = StateCombo.SelectedItem as CoreWindowState? ?? CoreWindowState.Normal;
        Rect? rect = state == CoreWindowState.Normal ? ParseRect(parseErrors) : null;

        var apply = ApplyCombo.SelectedItem as ApplyMode? ?? ApplyMode.All;
        var ifMissing = IfMissingCombo.SelectedItem as IfMissing? ?? IfMissing.Skip;
        var retry = ParseIntList(RetryBox.Text, "reintentos", parseErrors);
        var bleed = ParseBleed(parseErrors);

        return new Rule(
            name, match, new RulePlacement(aliases, state, rect),
            EnabledCheck.IsChecked == true, apply, ifMissing, retry, bleed);
    }

    private Rect? ParseRect(List<string> parseErrors)
    {
        var texts = new[] { RectLeftBox.Text, RectTopBox.Text, RectRightBox.Text, RectBottomBox.Text };
        if (texts.All(string.IsNullOrWhiteSpace))
            return null;

        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(texts[i], out values[i]))
            {
                parseErrors.Add("El rect tiene un valor que no es un número entero.");
                return null;
            }
        }

        return Rect.FromLtrb(values[0], values[1], values[2], values[3]);
    }

    private static IReadOnlyList<int>? ParseIntList(string text, string field, List<string> parseErrors)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var value))
            {
                parseErrors.Add($"El campo de {field} tiene un valor que no es un número entero: '{part}'.");
                return null;
            }
            values.Add(value);
        }

        return values;
    }

    private int? ParseBleed(List<string> parseErrors)
    {
        if (BleedNeverRadio.IsChecked == true)
            return 0;

        if (BleedFixedRadio.IsChecked == true)
        {
            if (int.TryParse(BleedFixedBox.Text, out var value))
                return value;

            parseErrors.Add("El bleed fijo tiene que ser un número entero de píxeles.");
            return null;
        }

        return null; // auto
    }

    // ---------------------------------------------------------------
    // Vista previa en vivo: validación + ventanas que matchean + YAML
    // ---------------------------------------------------------------

    private void UpdatePreview()
    {
        if (_initializing)
            return;

        var parseErrors = new List<string>();
        var candidate = Build(parseErrors);
        Result = candidate;

        var errors = new List<string>(parseErrors);
        errors.AddRange(RuleValidation.Validate(candidate, _bootstrap.CurrentRuleSet));

        RenderErrors(errors);
        RenderMatches(candidate);
        RenderYaml(candidate, errors);

        SaveButton.IsEnabled = errors.Count == 0;
    }

    private void RenderErrors(IReadOnlyList<string> errors)
    {
        ErrorsPanel.Children.Clear();
        var visible = errors.Count > 0;
        ErrorsHeader.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ErrorsBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        foreach (var error in errors)
        {
            ErrorsPanel.Children.Add(new TextBlock
            {
                Text = "• " + error,
                Foreground = (SolidColorBrush)FindResource("Alarm"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 12,
            });
        }
    }

    private void RenderMatches(Rule candidate)
    {
        MatchesPanel.Children.Clear();

        var matches = TopLevelWindows.Enumerate()
            .Select(_bootstrap.Probe.Describe)
            .Where(info => info is not null && info.Title.Length > 0)
            .Select(info => info!)
            .Where(info => TryMatches(candidate, info))
            .ToList();

        MatchesPanel.Children.Add(new TextBlock
        {
            Text = matches.Count == 0
                ? "Ninguna ventana abierta matchea esta regla ahora mismo."
                : $"{matches.Count} ventana(s) abierta(s) matchean ahora:",
            Foreground = matches.Count == 0 ? (SolidColorBrush)FindResource("Muted") : (SolidColorBrush)FindResource("MatchBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            FontSize = 12,
        });

        foreach (var info in matches)
        {
            MatchesPanel.Children.Add(new TextBlock
            {
                Text = $"· {info.Title}  ({ProcessName(info)})",
                Foreground = Brushes.LightGray,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 3),
            });
        }

        UpdateMeasuredBleed(matches.FirstOrDefault());
    }

    /// <summary>RuleMatcher no tira por criterios raros, pero un rect null con estado normal u otra combinación a medio construir sí podría — se prefiere "no matchea nada" a tumbar la vista previa.</summary>
    private static bool TryMatches(Rule candidate, WindowInfo info)
    {
        try
        {
            return RuleMatcher.Matches(candidate, info);
        }
        catch
        {
            return false;
        }
    }

    private void UpdateMeasuredBleed(WindowInfo? sample)
    {
        if (sample is null)
        {
            BleedMeasuredText.Text = "Sin ventana abierta que matchee: no se puede medir el borde ahora.";
            return;
        }

        var measured = _bootstrap.WindowSystem.MeasureContentInset(sample.Handle);
        BleedMeasuredText.Text = measured > 0
            ? $"Medido ahora contra \"{sample.Title}\": la app dibuja su propio borde de {measured}px."
            : $"Medido ahora contra \"{sample.Title}\": no se detecta borde propio (0px).";
    }

    private static string ProcessName(WindowInfo info)
    {
        try
        {
            return Process.GetProcessById((int)info.ProcessId).ProcessName;
        }
        catch
        {
            return info.ExePath is { } exe ? System.IO.Path.GetFileNameWithoutExtension(exe) : "?";
        }
    }

    private void RenderYaml(Rule candidate, IReadOnlyList<string> errors)
    {
        if (errors.Count > 0)
        {
            YamlPreview.Text = "# corregí lo de arriba antes de guardar";
            return;
        }

        try
        {
            YamlPreview.Text = YamlStore.RenderRule(candidate);
        }
        catch (Exception ex)
        {
            YamlPreview.Text = $"# no se puede renderizar: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
        if (Result is null || !SaveButton.IsEnabled)
        {
            MessageBox.Show(this, "Corregí los problemas marcados en rojo antes de guardar.", "MonSelect",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Nombre de la regla ANTES de editarla, para que el llamador sepa cuál reemplazar.</summary>
    public string OriginalName => _originalName;
}
