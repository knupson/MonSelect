using System.Windows;
using System.Windows.Controls;
using MonSelect.Core.Rules;
using MessageBox = System.Windows.MessageBox;

namespace MonSelect.App;

/// <summary>
/// Vista previa editable de la regla que "Crear regla desde esta ventana" está
/// por escribir. WindowToRule (Core, puro) arma la regla; acá sólo se muestra
/// el YAML resultante y se deja cambiar el nombre antes de confirmar.
/// </summary>
internal partial class CreateRuleDialog : Window
{
    private readonly OpenWindowRow _row;
    private readonly string _monitorAlias;
    private readonly bool _includeCommandLine;
    private readonly bool _includeTitle;

    /// <summary>
    /// Borde propio de la app (F2), medido contra la ventana en el momento de
    /// capturarla — 0 si no se pidió medir. Se pasa a WindowToRule para que el
    /// rect guardado, encogido por este tanto, reproduzca al aplicar la regla
    /// exactamente los mismos píxeles que se ven ahora.
    /// </summary>
    private readonly int _bleed;

    /// <summary>La regla final, lista para agregar a rules.yaml, si el usuario guardó.</summary>
    public Rule? Result { get; private set; }

    public CreateRuleDialog(
        OpenWindowRow row, string monitorAlias, bool includeCommandLine, bool includeTitle, int bleed = 0)
    {
        _row = row;
        _monitorAlias = monitorAlias;
        _includeCommandLine = includeCommandLine;
        _includeTitle = includeTitle;
        _bleed = bleed;

        InitializeComponent();
        NameBox.Text = DefaultName(row);
        NameBox.SelectAll();

        SetupTitleField();
        SetupCommandLineField();

        UpdatePreview();
    }

    private static string DefaultName(OpenWindowRow row)
        => string.IsNullOrWhiteSpace(row.Process) ? row.Title : row.Process;

    /// <summary>
    /// Precarga TitleBox con la parte estable del título (F: "el usuario opta
    /// por el título, no por el título entero anclado") y explica en qué caso
    /// no hay nada estable para sugerir.
    /// </summary>
    private void SetupTitleField()
    {
        if (!_includeTitle)
        {
            TitleFieldPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var suggested = WindowToRule.DefaultTitleRegex(_row.Info.Title);
        TitleBox.Text = suggested;
        TitleHint.Text = suggested.Length == 0
            ? "El título no empieza con una letra: no hay una parte estable para sugerir. " +
              "Escribí un patrón a mano, o dejá esto vacío para no incluir título en la regla."
            : "Coincide como substring en cualquier parte del título — no está anclado, así que " +
              "sobrevive a que le cambien un contador o un banner alrededor. Editalo si hace falta.";
    }

    /// <summary>
    /// Precarga CmdlineBox con sólo los argumentos (F: "no el exe completo
    /// entre comillas, que ya está en match.exe") y avisa cuando no hay
    /// argumentos en vez de guardar un patrón vacío-pero-no-null.
    /// </summary>
    private void SetupCommandLineField()
    {
        if (!_includeCommandLine)
        {
            CmdlineFieldPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var args = WindowToRule.ArgumentsOnly(_row.Info.CommandLine);
        if (args is null)
        {
            CmdlineBox.Text = string.Empty;
            CmdlineHint.Text = "Esta ventana no tiene argumentos de línea de comando; no se va a agregar " +
                                "cmdline a la regla.";
        }
        else
        {
            CmdlineBox.Text = args;
            CmdlineHint.Text = "Sólo los argumentos — el ejecutable ya lo identifica match.exe. Editalo si hace falta.";
        }
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void CmdlineBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "(sin nombre)" : NameBox.Text.Trim();

        // Vacío (título sin parte estable, ventana sin argumentos, o el
        // usuario borró todo a mano) significa "no ofrecer nada" — un patrón
        // vacío-pero-no-null matchearía de más, que es peor que omitir el
        // criterio.
        var titleOverride = _includeTitle && !string.IsNullOrWhiteSpace(TitleBox.Text) ? TitleBox.Text.Trim() : null;
        var cmdlineOverride = _includeCommandLine && !string.IsNullOrWhiteSpace(CmdlineBox.Text) ? CmdlineBox.Text.Trim() : null;
        var includeTitle = titleOverride is not null;
        var includeCommandLine = cmdlineOverride is not null;

        try
        {
            var rule = WindowToRule.Convert(
                _row.Info, _row.VisibleBounds, _monitorAlias, name, includeCommandLine, includeTitle,
                titleRegex: titleOverride, commandLineArguments: cmdlineOverride, bleed: _bleed);
            Result = rule;
            YamlPreview.Text = YamlStore.RenderRule(rule);
        }
        catch (Exception ex)
        {
            Result = null;
            YamlPreview.Text = $"# no se puede armar la regla: {ex.Message}";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "La regla necesita un nombre.", "MonSelect",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdatePreview();
        if (Result is null)
            return;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
